using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AnoMech.Core.Game.PartyHp;

// The one path HP goes up by: a heal landing, or a regen tick, applied per member after its own server
// tick, capped at max HP and never on the dead. Both HP models take it: the client's own HP when
// mitigation practice is off, practice's simulated pools when it is on.
internal sealed class PartyHealing(SimWorld world)
{
    private const float RaidwideWindow = 1f;
    private const int RaidwideMembers = 4;
    private const float RaidwideQuiet = 1.5f;

    // Every regen ticks on this one beat, as the server's does.
    public const float ServerTickSeconds = 3f;
    // FFLogs FRU clears show Regeneration ticks at exactly 1.0% of max HP (median and p25..p75: 0.010).
    private const float NaturalRegenFraction = 0.01f;
    private const float GroundStatusSeconds = 3.5f;

    private sealed class Regen(SimCharacter caster, HealAction heal, float endsAt, Vector3 center, List<SimCharacter> members)
    {
        public SimCharacter Caster { get; } = caster;
        public HealAction Heal { get; } = heal;
        public float EndsAt { get; } = endsAt;
        public Vector3 Center { get; } = center;
        public List<SimCharacter> Members { get; } = members;
    }

    private readonly List<(float At, SimCharacter Who)> recentHits = [];
    private readonly List<Regen> regens = [];
    private readonly HashSet<(SimCharacter Who, ushort StatusId, GameObjectId Source)> statuses = new();
    private float? nextTick;

    // The latest hit that caught most of the party at once, on the scenario clock; Id counts them.
    public float? RaidwideAt { get; private set; }
    public int RaidwideId { get; private set; }
    // The raidwide a healer has already answered with an oGCD heal.
    public int AnsweredRaidwide { get; set; }

    // The member's HP as a share of max, 0 for the dead and an unknown member.
    public float Fraction(SimCharacter who)
    {
        if (!who.IsAlive() || who.Proxy is not { Exists: true } chara) return 0f;
        if (world.MitPractice.HpFraction(who) is { } modelled) return modelled;
        return chara.MaxHealth == 0 ? 0f : (float)chara.Health / chara.MaxHealth;
    }

    // Members that have appeared; one still spawning has no HP yet.
    public IEnumerable<SimCharacter> Members() => world.Party.ActiveMembers().Where(m => m is not SimPartyNpc { PacketSpawnPending: true } && m.Proxy is { Exists: true, Health: > 0 });

    public float Average()
    {
        var members = Members().ToList();
        return members.Count == 0 ? 1f : members.Average(Fraction);
    }

    // The average, pulled down by whoever is lowest: one member near death counts as a party that needs healing.
    public float Need()
    {
        var members = Members().Select(Fraction).ToList();
        return members.Count == 0 ? 1f : Math.Min(members.Average(), members.Min() + 0.2f);
    }

    // `fractionOfMax` lands on the member after its own server tick. `landed` gets the HP the bar shows.
    public void Heal(SimCharacter who, float fractionOfMax, Action<uint>? landed = null)
    {
        var delay = world.PartyHp.RollTickDelay();
        world.Events.Add(delay, () =>
        {
            if (Apply(who, fractionOfMax) is { } health) landed?.Invoke(health);
        });
    }

    // The HP the member has after the heal, or null when it can't take one.
    public uint? Apply(SimCharacter who, float fractionOfMax)
    {
        if (who is not ISimPartyMember || !who.IsAlive() || who.Proxy is not { Exists: true } chara) return null;
        if (world.MitPractice.Heal(who, fractionOfMax) is { } modelled) return modelled;
        var health = (uint)Math.Min((double)chara.MaxHealth, chara.Health + Math.Round(fractionOfMax * chara.MaxHealth));
        who.WriteHealth(health);
        return health;
    }

    // A hit landed on `who`, for the healers' sense of a raidwide.
    public void NoteHit(SimCharacter who)
    {
        var now = world.Events.Elapsed;
        recentHits.RemoveAll(h => now - h.At > RaidwideWindow);
        recentHits.Add((now, who));
        if (recentHits.Select(h => h.Who).Distinct().Count() < RaidwideMembers) return;
        if (RaidwideAt is { } last && now - last < RaidwideQuiet) return;
        RaidwideAt = now;
        RaidwideId++;
    }

    // Where the shared 3 s beat falls on the scenario clock, drawn once per run from a stream of its own.
    public float TickPhase => world.Stream("server-tick").Fork("phase").NextSingle() * ServerTickSeconds;

    // Regens still running, per action, for the healers to see what is already up.
    public bool RegenUp(uint actionId) => regens.Any(r => r.Heal.Id == actionId && r.EndsAt > world.Events.Elapsed);

    public int ActiveRegens => regens.Count(r => r.EndsAt > world.Events.Elapsed);

    public void Tick()
    {
        var now = world.Events.Elapsed;
        if (nextTick is null)
        {
            var phase = TickPhase;
            nextTick = phase + MathF.Ceiling((now - phase) / ServerTickSeconds) * ServerTickSeconds;
        }
        while (nextTick <= now)
        {
            ServerTick(nextTick.Value);
            nextTick += ServerTickSeconds;
        }
    }

    private void ServerTick(float at)
    {
        foreach (var member in Members()) Heal(member, NaturalRegenFraction);
        regens.RemoveAll(r => r.EndsAt < at);
        foreach (var regen in regens)
        {
            var tick = regen.Heal.Tick!.Value;
            var inside = regen.Heal.Shape == HealShape.Ground
                ? Members().Where(m => DistanceXZ(m.Position, regen.Center) <= regen.Heal.Radius).ToList()
                : regen.Members.Where(m => m.IsAlive()).ToList();
            foreach (var who in inside)
            {
                if (regen.Heal.Shape == HealShape.Ground) ShowStatus(regen.Heal, who, regen.Caster, GroundStatusSeconds);
                Heal(who, Roll(tick));
            }
        }
    }

    // A size within p25..p75, centred on the median, from the heals stream.
    public float Roll(HealRange range)
    {
        var u = world.Stream("heals").NextSingle();
        return u < 0.5f
            ? range.P25 + (range.Median - range.P25) * (u * 2f)
            : range.Median + (range.P75 - range.Median) * ((u - 0.5f) * 2f);
    }

    private void ShowStatus(HealAction heal, SimCharacter who, SimCharacter caster, float seconds)
    {
        if (heal.StatusId == 0) return;
        var source = caster.GameObjectId;
        if (who.AddStatus(heal.StatusId, seconds, sourceObject: source) != null) statuses.Add((who, heal.StatusId, source));
    }

    // The action goes off: its direct heal lands per member after its own server tick, and a regen starts
    // ticking on the shared beat. A bot's ActionEffect carries the heals so the client shows them.
    public void Perform(SimCharacter caster, HealAction heal, SimCharacter? target)
    {
        if (!caster.IsAlive()) return;
        var now = world.Events.Elapsed;
        var reached = heal.Shape switch
        {
            HealShape.Single => (target ?? LowestMember()) is { } one && one.IsAlive() ? [one] : [],
            HealShape.Ground => [],
            _ => Members().Where(m => DistanceXZ(m.Position, caster.Position) <= HealCatalogue.PartyRange).ToList(),
        };
        if (heal.Direct is { } direct && reached.Count > 0)
        {
            var amounts = reached.Select(m => (Who: m, Fraction: Roll(direct))).ToList();
            uint sequence = 0;
            var shown = new List<SimCharacter>();
            if (caster is SimPartyNpc bot)
            {
                var entries = amounts.Where(a => a.Who.Proxy is { Exists: true } && Natives.BattleCharas.IsInCharacterManager(a.Who.GameObjectId.ObjectId))
                    .Select(a => (a.Who, Amount: (uint)MathF.Round(a.Fraction * a.Who.Proxy!.MaxHealth))).ToList();
                shown = entries.Select(e => e.Who).ToList();
                sequence = bot.PlayHeal(heal.Id, entries, reached[0]);
            }
            foreach (var (who, fraction) in amounts)
            {
                var index = shown.IndexOf(who);
                Heal(who, fraction, health =>
                {
                    if (sequence != 0 && index >= 0 && who.Proxy is { Exists: true } chara) chara.ReceiveEffectResult(sequence, (byte)index, health);
                });
            }
        }
        else if (caster is SimPartyNpc caster2) caster2.PlayAction(heal.Id);
        if (heal.Tick is null) return;
        var members = heal.Shape == HealShape.Party ? reached : [];
        regens.Add(new Regen(caster, heal, now + heal.Seconds, caster.Position, members));
        foreach (var who in members) ShowStatus(heal, who, caster, heal.Seconds);
    }

    private SimCharacter? LowestMember() => Members().MinBy(Fraction);

    private static float DistanceXZ(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    public void Clear()
    {
        regens.Clear();
        nextTick = null;
        foreach (var (who, statusId, source) in statuses)
            if (who.IsActive) who.RemoveStatus(statusId, source);
        statuses.Clear();
        recentHits.Clear();
        RaidwideAt = null;
        RaidwideId = 0;
        AnsweredRaidwide = 0;
    }
}

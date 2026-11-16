using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.PartyHp;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;

namespace AnoMech.Core.Game.Party;

// When a healer bot heals instead of dealing damage, and with what. It answers a raidwide within a few
// seconds (an oGCD heal from one healer, a regen if none is up), spends every other GCD on an AoE heal
// while the party is low or a hit is about to land on a party that is not full, and heals a tank that falls
// under half. Otherwise it plays its rotation, so its burst windows stay as they are.
internal sealed class BotHealing(SimWorld world)
{
    private const float LowAverage = 0.80f;
    private const float VeryLowAverage = 0.65f;
    private const float RecentRaidwideSeconds = 8f;
    private const float RecentRaidwideAverage = 0.90f;
    private const float OgcdWeaveAverage = 0.70f;
    private const float BackupBelow = 0.90f;
    private const float TopUpBelow = 0.95f;
    private const float IncomingWithin = 6f;
    private const float IncomingOgcdWithin = 4f;
    private const float TankLow = 0.50f;
    private const float TankCritical = 0.30f;
    private const float MemberCritical = 0.35f;
    private const float RaidwideReactMin = 1f;
    private const float RaidwideReactMax = 3f;
    private const float BackupAfter = 1.5f;
    private const float RaidwideHealBelow = 0.88f;
    private const float TankReserveSeconds = 3f;
    private const float HitTooCloseForABar = 2.5f;
    private const float SwiftcastBelow = 0.70f;
    public const uint Swiftcast = 7561;

    private sealed class State
    {
        public int SeenRaidwide;
        public float ReactAt;
        public int GcdCount;
    }

    private readonly Dictionary<SimCharacter, State> states = new();
    private readonly Dictionary<SimCharacter, float> reservedTanks = new();

    private sealed class Autopilot
    {
        public readonly Rot Rot = new();
        public float NextGcd;
        public float? WeaveAt;
    }

    private readonly Dictionary<SimCharacter, Autopilot> autopilot = new();

    // A healer seat that is bot-driven without being a bot (the player, in a headless run) heals by the
    // same rules, so a clean clear stays clean. A real player heals with their own buttons.
    public void TickAutopilot(SimPlayer player, bool botDriven)
    {
        if (!botDriven || !player.IsAlive() || HealCatalogue.For((JobId)player.ClassJob).Count == 0) return;
        var now = world.Events.Elapsed;
        if (!autopilot.TryGetValue(player, out var pilot)) autopilot[player] = pilot = new Autopilot { NextGcd = now };
        pilot.Rot.Now = now;
        if (now >= pilot.NextGcd)
        {
            pilot.NextGcd = now + 2.5f;
            pilot.WeaveAt = now + 0.7f;
            if (PickGcd(player, pilot.Rot, _ => true) is { } gcd) Perform(player, pilot, gcd);
        }
        else if (pilot.WeaveAt is { } weave && now >= weave)
        {
            pilot.WeaveAt = null;
            if (PickOgcd(player, pilot.Rot) is { } ogcd) Perform(player, pilot, ogcd);
        }
    }

    private void Perform(SimPlayer player, Autopilot pilot, (HealAction Heal, SimCharacter? Target) pick)
    {
        var cast = Natives.Data.Action(pick.Heal.Id)?.CastSeconds ?? 0f;
        pilot.Rot.Record(pick.Heal.Id, Natives.Data.Action(pick.Heal.Id)?.RecastSeconds is > 2.5f and var recast ? recast : 0f, false);
        if (pick.Heal.UsesLily) pilot.Rot.Gauge = Math.Max(0, pilot.Rot.Gauge - 1);
        if (cast > 0f) world.Events.Add(cast, () => world.Healing.Perform(player, pick.Heal, pick.Target));
        else world.Healing.Perform(player, pick.Heal, pick.Target);
    }

    public static bool HasSwiftcast(SimCharacter who) => (JobId)((ISimPartyMember)who).ClassJob is JobId.WhiteMage or JobId.Scholar or JobId.Astrologian or JobId.Sage;

    public bool IsHealer(SimCharacter who) => HealCatalogue.For((JobId)((ISimPartyMember)who).ClassJob).Count > 0;

    public void Reset()
    {
        autopilot.Clear();
        states.Clear();
        reservedTanks.Clear();
    }

    public (HealAction Heal, SimCharacter? Target)? PickOgcd(SimCharacter bot, Rot rot)
    {
        var options = Options(bot, rot, gcd: false);
        if (options.Count == 0) return null;
        var now = world.Events.Elapsed;
        var healing = world.Healing;

        if (Critical() is { } tank && !(reservedTanks.TryGetValue(tank, out var until) && until > now))
        {
            var single = options.Where(h => h.Shape == HealShape.Single).ToList();
            var pick = healing.Fraction(tank) < TankCritical ? single.MaxBy(Size) : single.MinBy(Size);
            if (pick != null)
            {
                reservedTanks[tank] = now + TankReserveSeconds;
                return (pick, tank);
            }
        }

        if (HitIncoming(IncomingOgcdWithin) && healing.Members().Any(m => healing.Fraction(m) < TopUpBelow)
            && options.Where(h => h.Shape == HealShape.Party && h.Direct is not null).MaxBy(Size) is { } before)
            return (before, null);

        if (healing.RaidwideAt is not { } at) return LowAverageOgcd(options, healing);
        var state = StateOf(bot);
        if (state.SeenRaidwide != healing.RaidwideId)
        {
            state.SeenRaidwide = healing.RaidwideId;
            state.ReactAt = at + RaidwideReactMin + world.Stream("heals").NextSingle() * (RaidwideReactMax - RaidwideReactMin);
        }
        // With the next hit already close, there is no waiting a few seconds to answer this one.
        if (HitIncoming(IncomingOgcdWithin)) state.ReactAt = MathF.Min(state.ReactAt, at + 0.3f);
        if (now < state.ReactAt || healing.Need() >= RaidwideHealBelow) return LowAverageOgcd(options, healing);
        var turn = Healers().OrderBy(h => h.Role).ToList();
        var mine = turn.Count == 0 || ReferenceEquals(turn[healing.RaidwideId % turn.Count], bot);
        if (!mine && now < state.ReactAt + BackupAfter) return LowAverageOgcd(options, healing);

        if (healing.AnsweredRaidwide != healing.RaidwideId || (!mine && healing.Need() < BackupBelow))
        {
            var direct = options.Where(h => h.Shape == HealShape.Party && h.Direct is not null).MaxBy(Size);
            if (direct != null)
            {
                healing.AnsweredRaidwide = healing.RaidwideId;
                return (direct, null);
            }
        }
        if (healing.ActiveRegens == 0)
        {
            var regen = options.Where(h => h.Tick is not null).MaxBy(h => h.Tick!.Value.Median * h.Seconds);
            if (regen != null) return (regen, null);
        }
        return LowAverageOgcd(options, healing);
    }

    public bool HitIncoming(float within)
        => world.MitPractice.NextHitAt(world.Events.Elapsed) is { } hit && hit - world.Events.Elapsed <= within;

    // A party that stays low gets every ready oGCD heal, whatever the last raidwide was.
    private static (HealAction Heal, SimCharacter? Target)? LowAverageOgcd(List<HealAction> options, PartyHealing healing)
        => healing.Need() < OgcdWeaveAverage && options.Where(h => h.Shape == HealShape.Party).MaxBy(Size) is { } heal ? (heal, null) : null;

    public (HealAction Heal, SimCharacter? Target)? PickGcd(SimCharacter bot, Rot rot, Func<float, bool> castFits)
    {
        var healing = world.Healing;
        var average = healing.Need();
        var members = healing.Members().ToList();
        var incoming = HitIncoming(IncomingWithin) && members.Any(m => healing.Fraction(m) < TopUpBelow);
        var recent = healing.RaidwideAt is { } at && world.Events.Elapsed - at <= RecentRaidwideSeconds && average < RecentRaidwideAverage;
        if (average >= LowAverage && !incoming && !recent) return null;
        var state = StateOf(bot);
        state.GcdCount++;
        if (average >= VeryLowAverage && !incoming && !recent && state.GcdCount % 2 == 1) return null;

        var options = Options(bot, rot, gcd: true)
            .Where(h => h.Shape == HealShape.Party)
            .Where(h => Natives.Data.Action(h.Id) is { } row && (row.CastSeconds <= 0f || castFits(row.CastSeconds)))
            .ToList();
        // A hit this close lands before a bar would fill: Swiftcast makes the cast heal instant.
        if (HitIncoming(HitTooCloseForABar) && average < TopUpBelow && rot.Ready(Swiftcast) && HasSwiftcast(bot)
            && Options(bot, rot, gcd: true).Where(h => h.Shape == HealShape.Party && h.Direct is not null).MaxBy(Size) is { } soon)
            return (soon, null);
        // A regen already up is not worth another cast.
        var pick = options.Where(h => h.Direct is not null || !healing.RegenUp(h.Id)).MaxBy(h => Size(h) + (h.UsesLily ? 1f : 0f));
        if (pick != null) return (pick, null);
        // Nothing it can stand and cast: with Swiftcast ready, a cast heal goes off instantly instead.
        if (average >= SwiftcastBelow || !rot.Ready(Swiftcast) || !HasSwiftcast(bot)) return null;
        var swift = Options(bot, rot, gcd: true)
            .Where(h => h.Shape == HealShape.Party && (h.Direct is not null || !healing.RegenUp(h.Id)) && Natives.Data.Action(h.Id)?.CastSeconds > 0f)
            .MaxBy(Size);
        return swift == null ? null : (swift, null);
    }

    // What the healer can use now: its job's heals that are ready, with their resource, leaving the
    // buttons the mitigation plan presses to the plan.
    private List<HealAction> Options(SimCharacter bot, Rot rot, bool gcd)
    {
        var job = (JobId)((ISimPartyMember)bot).ClassJob;
        if (job == JobId.WhiteMage) BotScripts.TopUpLilies(rot);
        return HealCatalogue.For(job)
            .Where(h => h.Gcd == gcd && rot.Ready(h.Id) && (!h.UsesLily || rot.Gauge > 0))
            .Where(h => gcd || !(world.MitPractice.IsActive && MitCatalogue.Find(h.Id) != null))
            .ToList();
    }

    private static float Size(HealAction heal)
        => (heal.Direct?.Median ?? 0f) + (heal.Tick is { } tick ? tick.Median * MathF.Floor(heal.Seconds / PartyHealing.ServerTickSeconds) : 0f);

    // A tank under half, or anyone under a third, needs a heal of their own.
    private SimCharacter? Critical()
    {
        var healing = world.Healing;
        if (LowestTank() is { } tank && healing.Fraction(tank) < TankLow) return tank;
        return healing.Members().MinBy(healing.Fraction) is { } lowest && healing.Fraction(lowest) < MemberCritical ? lowest : null;
    }

    private SimCharacter? LowestTank()
        => world.Healing.Members().Where(m => m is ISimPartyMember { Role: PartyRole.MainTank or PartyRole.OffTank }).MinBy(world.Healing.Fraction);

    private IEnumerable<ISimPartyMember> Healers()
        => world.Party.ActiveMembers().OfType<ISimPartyMember>().Where(m => HealCatalogue.For((JobId)m.ClassJob).Count > 0);

    private State StateOf(SimCharacter bot)
    {
        if (!states.TryGetValue(bot, out var state)) states[bot] = state = new State();
        return state;
    }
}

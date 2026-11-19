using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.SimObjects;
using AnoMech.Core.Game.PartyHp;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Core.Game.Party;

// Makes the party bots look like they fight: auto-attacks and each job's rotation played at the boss,
// animation only (no effect entries, so no damage, flytext or sim change). Never turns a bot: gazes
// kill whoever the sim says faces the source, and strats set facing themselves. A GCD with a cast
// time raises a real cast bar, and only starts while the bot stands still with no strat move due
// before the bar fills; a bot that starts moving mid-cast calls it off.
internal sealed class BotCombat(SimWorld world)
{
    private const float GcdSeconds = 2.5f;
    private const float GcdJitterSeconds = 0.2f;
    private const float SlidecastSeconds = 0.5f;
    // A healer finishes a heal a strat move has already called it away from, up to this long before the bar fills.
    private const float HealSlideSeconds = 1f;
    // And a bar already up is only called off for a move that starts before this much is left.
    private const float HealCancelSlideSeconds = 1.5f;
    private const float AutoSeconds = 3f;
    private const float WeaveDelaySeconds = 0.7f;
    private const float RetrySeconds = 0.3f;
    private const float PressMarginSeconds = 0.3f;
    private const int WeavesAfterInstant = 2;
    private const float AutoLock = 0.1f;
    private const float ActionLock = 0.6f;
    private const float MeleeReach = 3.5f;
    private const float RangedReach = 25f;

    private sealed class BotClock
    {
        public float NextAuto;
        public float NextGcd;
        public float? WeaveAt;
        public int WeavesLeft;
        public byte AutoCount;
        public readonly Rot Rot = new();
        public (uint Id, float EndsAt, SimEnemy Boss)? Cast;
        public bool Planned;
        public SimCharacter? HealTarget;
        public float CastSlide = SlidecastSeconds;
        public readonly List<PendingBurst> Bursts = new();
    }

    // A burst the bot has picked its moment for: from `At` on it plays these as weave slots allow, until
    // `Deadline` (scenario clock).
    internal sealed record PendingBurst(string Window, bool Raid, float At, float Deadline, List<uint> Left);

    private const float BurstGraceSeconds = 8f;
    private const float RaidRecastSeconds = 110f;

    private readonly HashSet<(SimCharacter Who, ushort StatusId, FFXIVClientStructs.FFXIV.Client.Game.Object.GameObjectId Source)> buffs = new();
    private readonly BotHealing healing = new(world);
    private IScenario? planned;
    private IReadOnlyList<BurstAnchor> anchors = [];

    private readonly Dictionary<SimPartyNpc, BotClock> clocks = new();
    private float now;

    // Comes off with everything the bots put up: the run is over or combat is off.
    public void Halt()
    {
        CancelCasts();
        healing.Reset();
        foreach (var (who, statusId, source) in buffs)
            if (who.IsActive) who.RemoveStatus(statusId, source);
        buffs.Clear();
    }

    public void Reset()
    {
        Halt();
        planned = null;
        clocks.Clear();
        now = 0f;
    }

    // Every bar in progress is called off: the run is over, the target is gone or combat is off.
    public void CancelCasts()
    {
        foreach (var (bot, clock) in clocks)
        {
            if (clock.Cast == null) continue;
            clock.Cast = null;
            bot.CancelCast(CastCancelReason.SelfCancelled);
        }
    }

    // The healing a bot-driven player seat would do (headless runs), on the same rules as the bots.
    public void TickPlayerHealer(SimPlayer? player, bool botDriven)
    {
        if (player != null) healing.TickAutopilot(player, botDriven);
    }

    public void Tick(float deltaSeconds, IEnumerable<SimPartyNpc> bots)
    {
        now += deltaSeconds;
        if (FindTarget() is not { } boss)
        {
            Halt();
            return;
        }
        var rng = world.Stream("bot-combat");
        var scenario = world.SimEvents.Listener as IScenario;
        if (!ReferenceEquals(scenario, planned))
        {
            planned = scenario;
            anchors = scenario?.BurstAnchors ?? [];
            foreach (var other in clocks.Values) other.Planned = false;
        }
        foreach (var bot in bots)
        {
            if (!clocks.TryGetValue(bot, out var clock))
            {
                if (!CanFight(bot)) continue;
                clocks[bot] = clock = new BotClock
                {
                    NextAuto = now + rng.NextSingle() * AutoSeconds,
                    NextGcd = now + rng.NextSingle() * GcdSeconds,
                };
            }
            clock.Rot.Now = now;
            clock.Rot.Rng = rng;
            if (!clock.Planned) PlanBursts(bot, clock, rng);
            if (clock.Cast != null) TickCast(bot, clock, rng);
            else if (CanFight(bot)) Fight(bot, clock, FindTarget(bot.Role) ?? boss, rng);
        }
    }

    // A bar is up: moving now would call it off.
    internal bool IsCasting(SimPartyNpc bot)
        => bot.Casting.IsCasting || clocks.TryGetValue(bot, out var clock) && clock.Cast != null;

    // `role` lets a scenario that opts in (IBotTargetPicker) send a seat to its own boss; any other gets the shared one.
    internal SimEnemy? FindTarget(PartyRole? role = null)
    {
        if (role is { } seat && world.SimEvents.Listener is IBotTargetPicker picker
            && picker.TargetFor(seat) is { IsActive: true, Targetable: true } picked)
            return picked;
        SimEnemy? first = null;
        foreach (var enemy in world.Children.OfType<SimEnemy>())
        {
            if (!enemy.IsActive || !enemy.Targetable) continue;
            if (enemy.InEnemyList) return enemy;
            first ??= enemy;
        }
        return first;
    }

    private static bool CanFight(SimPartyNpc bot)
        => bot.IsAlive() && !bot.ActionsLocked && !bot.PacketSpawnPending && !bot.CombatHeld
           && !bot.Casting.IsBusy && bot.Proxy is { Exists: true, IsCasting: false };

    private void TickCast(SimPartyNpc bot, BotClock clock, Rng rng)
    {
        var (id, endsAt, boss) = clock.Cast!.Value;
        if (now < endsAt - clock.CastSlide && !bot.Casting.IsCasting)
        {
            clock.Cast = null;
            return;
        }
        if (!bot.IsAlive() || bot.ActionsLocked || (bot.Relocating && now < endsAt - clock.CastSlide) || !boss.IsActive || !boss.Targetable)
        {
            clock.Cast = null;
            bot.CancelCast(CastCancelReason.SelfCancelled);
            return;
        }
        if (now < endsAt) return;
        clock.Cast = null;
        Release(bot, clock, boss, id);
        clock.WeavesLeft = Math.Max(rng.Next(2), DueBurst(clock) == null ? 0 : 1);
        clock.WeaveAt = clock.WeavesLeft > 0 ? now + WeaveDelaySeconds : null;
    }

    private void Fight(SimPartyNpc bot, BotClock clock, SimEnemy boss, Rng rng)
    {
        var job = (JobId)bot.ClassJob;
        var script = BotScripts.For(bot.ClassJob);
        var distance = Distance(bot, boss);

        if (now >= clock.NextAuto && distance <= AutoAttackHandler.ReachOf(job) + bot.HitboxRadius + boss.HitboxRadius)
        {
            clock.NextAuto = MathF.Max(clock.NextAuto + AutoSeconds, now);
            bot.PlayActionOn(AutoAttackHandler.ActionIdOf(job), boss, AutoLock, (byte)(clock.AutoCount++ % 3));
            return;
        }
        if (script == null) return;
        if (distance > (script.Melee ? boss.HitboxRadius + MeleeReach : RangedReach))
        {
            // Buffs need no reach, so a melee out of range still presses the window's.
            if (DueBurst(clock) is { } buff) PlayBurst(bot, clock, boss, buff);
            else if (healing.IsHealer(bot)) HealOutOfReach(bot, clock, boss, script, rng);
            return;
        }

        if (now >= clock.NextGcd) StartGcd(bot, clock, boss, script, rng);
        else if (clock.WeaveAt is { } weaveAt && now >= weaveAt)
        {
            clock.WeaveAt = null;
            if (healing.PickOgcd(bot, clock.Rot) is { } healOgcd)
            {
                clock.HealTarget = healOgcd.Target;
                Release(bot, clock, boss, healOgcd.Heal.Id);
            }
            else if (DueBurst(clock) is { } due) PlayBurst(bot, clock, boss, due);
            else if (FirstUsable(script.Ogcd(clock.Rot), clock.Rot, _ => false) is { } ogcd) Release(bot, clock, boss, ogcd);
            else return;
            if (--clock.WeavesLeft > 0) clock.WeaveAt = now + WeaveDelaySeconds;
        }
    }

    // Where in each window this bot goes: one moment per window and kind, drawn from a stream of its
    // own so a drill and the full phase put it at the same offset from the anchor.
    private void PlanBursts(SimPartyNpc bot, BotClock clock, Rng rng)
    {
        clock.Planned = true;
        clock.Bursts.Clear();
        var script = BotScripts.For(bot.ClassJob);
        if (script == null) return;
        var scenarioNow = world.Events.Elapsed;
        foreach (var window in BurstWindow.All)
        {
            if (window.AnchorTime(anchors) is not { } anchor) continue;
            foreach (var (range, raid) in new[] { (window.Raid, true), ((BurstRange?)window.Personal, false) })
            {
                if (range is not { } r) continue;
                var ids = script.Burst.Where(id => (RecastOf(id) >= RaidRecastSeconds) == raid).ToList();
                if (ids.Count == 0) continue;
                var deadline = anchor + r.To + BurstGraceSeconds;
                if (deadline < scenarioNow) continue;
                clock.Bursts.Add(new PendingBurst(window.Name, raid, anchor + BurstWindow.Pick(rng, bot.Role, window.Name, raid, r), deadline, ids));
            }
        }
    }

    // What the bot has planned for this run, as scenario-clock moments.
    internal IReadOnlyList<PendingBurst> PlannedBursts(SimPartyNpc bot)
        => clocks.TryGetValue(bot, out var clock) ? [.. clock.Bursts] : [];

    private static float RecastOf(uint id) => Natives.Data.Action(id)?.RecastSeconds ?? 0f;

    // The first burst action whose window has come and which is ready, dropping windows that ran out.
    private (PendingBurst Burst, uint Id)? DueBurst(BotClock clock)
    {
        var scenarioNow = world.Events.Elapsed;
        clock.Bursts.RemoveAll(b => scenarioNow > b.Deadline || b.Left.Count == 0);
        foreach (var burst in clock.Bursts)
        {
            if (scenarioNow < burst.At) continue;
            foreach (var id in burst.Left)
                if (clock.Rot.Ready(id)) return (burst, id);
        }
        return null;
    }

    private void PlayBurst(SimPartyNpc bot, BotClock clock, SimEnemy boss, (PendingBurst Burst, uint Id) due)
    {
        Release(bot, clock, boss, due.Id);
        due.Burst.Left.Remove(due.Id);
    }

    // A healer too far from the boss to attack still heals: its heals reach the party, not the boss.
    private void HealOutOfReach(SimPartyNpc bot, BotClock clock, SimEnemy boss, JobScript script, Rng rng)
    {
        if (now >= clock.NextGcd) StartGcd(bot, clock, boss, script, rng, healOnly: true);
        else if (clock.WeaveAt is { } weaveAt && now >= weaveAt)
        {
            clock.WeaveAt = null;
            if (healing.PickOgcd(bot, clock.Rot) is not { } ogcd) return;
            clock.HealTarget = ogcd.Target;
            Release(bot, clock, boss, ogcd.Heal.Id);
            if (--clock.WeavesLeft > 0) clock.WeaveAt = now + WeaveDelaySeconds;
        }
    }

    private void StartGcd(SimPartyNpc bot, BotClock clock, SimEnemy boss, JobScript script, Rng rng, bool healOnly = false)
    {
        clock.HealTarget = null;
        var heal = healing.PickGcd(bot, clock.Rot, Fits(bot, HealSlideSeconds, ignorePress: true));
        if (heal is { } pickedHeal) clock.HealTarget = pickedHeal.Target;
        if ((heal?.Heal.Id ?? (healOnly ? null : FirstUsable(script.Gcd(clock.Rot), clock.Rot, Fits(bot)))) is not { } gcd)
        {
            clock.NextGcd = now + RetrySeconds;
            // With no GCD to play (it cannot stand and cast), a healer still presses its oGCD heals.
            if (healing.PickOgcd(bot, clock.Rot) is { } stillHeals)
            {
                clock.HealTarget = stillHeals.Target;
                Release(bot, clock, boss, stillHeals.Heal.Id);
            }
            return;
        }
        var cast = Natives.Data.Action(gcd)?.CastSeconds ?? 0f;
        // A cast heal the bot cannot stand for goes off instantly under Swiftcast.
        if (heal is not null && cast > 0f && (!Fits(bot, HealSlideSeconds, ignorePress: true)(cast) || healing.HitIncoming(cast)))
        {
            clock.Rot.Record(BotHealing.Swiftcast, RecastOf(BotHealing.Swiftcast), false);
            bot.PlayAction(BotHealing.Swiftcast);
            cast = 0f;
        }
        var jitter = rng.NextSingle() * GcdJitterSeconds;
        if (cast > 0f)
        {
            clock.CastSlide = heal is null ? SlidecastSeconds : HealCancelSlideSeconds;
            clock.NextGcd = now + MathF.Max(GcdSeconds, cast - (heal is null ? SlidecastSeconds : HealSlideSeconds)) + jitter;
            clock.Cast = (gcd, now + cast, boss);
            clock.WeaveAt = null;
            bot.BeginCast(gcd, cast, boss);
            return;
        }
        clock.NextGcd = now + GcdSeconds + jitter;
        clock.WeavesLeft = WeavesAfterInstant;
        clock.WeaveAt = now + WeaveDelaySeconds;
        Release(bot, clock, boss, gcd);
    }

    // A strat move due before the bar would fill has the bot walking off mid-cast; one in the
    // bar's last half second is a slidecast, which the game lets through.
    // A planned mitigation press is never pressed over a bar either: it has to come after the bar
    // ends, with a beat to spare.
    private Func<float, bool> Fits(SimPartyNpc bot, float slide = SlidecastSeconds, bool ignorePress = false) => cast =>
        !bot.Relocating && (world.Departures.NextAfter(world.Events.Elapsed, bot.Role) is not { } due || due >= world.Events.Elapsed + cast - slide)
        && (ignorePress || world.MitPractice.NextPressAt(bot) is not { } press || press >= world.Events.Elapsed + cast + PressMarginSeconds);

    private static uint? FirstUsable(IEnumerable<uint> candidates, Rot rot, Func<float, bool> castFits)
    {
        foreach (var id in candidates)
        {
            if (Natives.Data.Action(id) is not { } row) continue;
            if (row.CastSeconds > 0f && !castFits(row.CastSeconds)) continue;
            if (rot.Ready(id)) return id;
        }
        return null;
    }

    private void Release(SimPartyNpc bot, BotClock clock, SimEnemy boss, uint actionId)
    {
        var script = BotScripts.For(bot.ClassJob)!;
        var recast = Natives.Data.Action(actionId)?.RecastSeconds ?? 0f;
        if (HealCatalogue.Find(actionId) is { } heal)
        {
            clock.Rot.Record(actionId, recast > GcdSeconds ? recast : 0f, false);
            script.Used(clock.Rot, actionId);
            bot.ResolveEffects(actionId, clock.HealTarget);
            world.Healing.Perform(bot, heal, clock.HealTarget);
            return;
        }
        clock.Rot.Record(actionId, recast > GcdSeconds ? recast : 0f, Array.IndexOf(script.Burst, actionId) >= 0);
        script.Used(clock.Rot, actionId);
        foreach (var applied in BotBuffs.Apply(actionId, bot, boss, world.Party)) buffs.Add(applied);
        bot.PlayActionOn(actionId, boss, ActionLock);
    }

    private static float Distance(SimCharacter a, SimCharacter b)
    {
        var dx = a.Position.X - b.Position.X;
        var dz = a.Position.Z - b.Position.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}

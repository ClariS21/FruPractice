using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using Dalamud.Game.Text;

namespace AnoMech.Core.Game.PartyMit;

public enum MitRunEnd { Completed, Death, Reset }

// The running mitigation practice. Bots press the plan resolved for the real comp; hits move
// simulated HP, warning instead of killing; the local player's planned presses are graded when
// the run ends. Host-only, since it starts from scenario.Run, which a peer never runs. Keeps its
// own schedule rather than world.Events, so a scenario's end isn't held back by it.
public sealed class MitPractice
{
    // A planned press counts as missing from a hit landing this close to one of its covers.
    private const float MissingWithin = 1f;
    // A press a frame or two after its planned time doesn't make the next one early.
    private const float RecastSlack = 0.1f;

    private readonly SimWorld world;
    private MitPlanData? data;
    private IReadOnlyList<ResolvedPress> plan = [];
    private IReadOnlyDictionary<MitSource, Func<SimCharacter?>> sources = new Dictionary<MitSource, Func<SimCharacter?>>();
    private Func<float> clock;
    private readonly List<ResolvedPress> pending = [];
    private readonly Dictionary<(PartyRole Slot, uint Action), float> lastPress = [];
    private readonly List<MitPress> presses = [];
    private readonly Dictionary<SimCharacter, MitHpPool> pools = [];
    private readonly Dictionary<SimCharacter, ShieldLedger> ledgers = [];
    private readonly Dictionary<SimCharacter, float> warnedAt = [];
    // When each member's bar shows a hit that has landed in the model, staggered like PartyHp's.
    private readonly Dictionary<SimCharacter, float> writeDue = [];
    // Hits whose mitigation was judged at the snapshot but whose damage lands damageDelay later, when the model's HP drops.
    private readonly List<(float At, SimCharacter Victim, MitHitDef Hit, MitHitResult Result)> landings = [];
    private bool hpMode;
    private bool grading;
    private bool finished;

    internal MitPractice(SimWorld world)
    {
        this.world = world;
        clock = () => world.Events.Elapsed;
    }

    public bool Enabled { get; set; }
    public bool IsActive => data != null && !finished;
    // Hits move HP bars and show their own damage numbers.
    public bool MovesHp => IsActive && hpMode;

    internal bool Covers(SimCharacter who) => MovesHp && pools.ContainsKey(who);

    // At the scenario's start. `sources` resolves each MitSource to its live actor (the boss the
    // party's debuffs go on); `clock` is the scenario's, when it isn't world.Events.
    public void Begin(MitPlanData planData, IReadOnlyDictionary<MitSource, Func<SimCharacter?>>? actors = null, Func<float>? scenarioClock = null)
    {
        Clear();
        if (!Enabled || !world.Party.AllMembers().OfType<SimPartyNpc>().Any()) return;
        data = planData;
        sources = actors ?? new Dictionary<MitSource, Func<SimCharacter?>>();
        clock = scenarioClock ?? (() => world.Events.Elapsed);
        var jobs = world.Party.AllMembers().OfType<ISimPartyMember>().ToDictionary(m => m.Role, m => (JobId)m.ClassJob);
        plan = MitPlan.Resolve(planData.Entries, jobs);
        pending.AddRange(plan.Where(p => world.Party.Get(p.Slot) switch
                             {
                                 SimPartyNpc => planData.Entries[p.Entry].ActionId != MitPlanEntry.TankLb3,
                                 SimPlayer player => p.IsPreStart || world.Party.IsBotDriven(player),
                                 _ => false,
                             })
                             .OrderBy(p => p.At));
        hpMode = planData.Hits.Any(h => h.Raw != null);
        grading = Natives.UserActions.Enabled;
        if (hpMode)
            foreach (var member in world.Party.AllMembers())
                if (member is ISimPartyMember slot)
                {
                    var pool = new MitHpPool(planData.Profile.MaxHp(MitProfile.ClassOf((JobId)slot.ClassJob)));
                    pools[member] = pool;
                    Write(member, pool);
                }
        DiagnosticLog.Info($"[MitPractice] Begin: {plan.Count} planned presses for [{string.Join(", ", jobs.OrderBy(j => j.Key).Select(j => $"{j.Key}={j.Value}"))}], "
                           + $"{pending.Count} by bots, HP {(hpMode ? "on" : "off")}, grading {(grading ? "on" : "off")}.");
    }

    // When this bot's next planned press is due, on world.Events' clock, or null when it has none left.
    internal float? NextPressAt(ISimPartyMember member)
    {
        foreach (var press in pending)
            if (press.Slot == member.Role) return press.At - clock() + world.Events.Elapsed;
        return null;
    }

    internal void Tick()
    {
        if (!IsActive) return;
        var now = clock();
        while (pending.Count > 0 && pending[0].At <= now)
        {
            var press = pending[0];
            pending.RemoveAt(0);
            if (Wanted(press)) Press(press, now);
        }
        if (!hpMode) return;
        for (var i = 0; i < landings.Count; i++)
        {
            if (landings[i].At > now) continue;
            var (_, victim, hit, result) = landings[i];
            landings.RemoveAt(i--);
            if (victim is ISimPartyMember due && victim.IsAlive() && pools.TryGetValue(victim, out var duePool)) Damage(due, victim, duePool, hit, result, now);
        }
        foreach (var (member, pool) in pools)
        {
            if (!member.IsAlive()) continue;
            if (writeDue.TryGetValue(member, out var due))
            {
                if (due > now) continue;
                writeDue.Remove(member);
                member.ShieldOverride = Ledger(member).Remaining / pool.Max;
                Write(member, pool);
                continue;
            }
            var ledger = Ledger(member);
            ledger.Sync(member, pool.Max);
            var shield = ledger.Remaining / pool.Max;
            var shieldChanged = member.ShieldOverride != shield;
            member.ShieldOverride = shield;
            if (shieldChanged) Write(member, pool);
        }
    }

    // A row for the baiter is for that slot alone, once the run has chosen it.
    private bool Wanted(ResolvedPress p) => !data!.Entries[p.Entry].ForBaiter || data.Baiter?.Invoke() == p.Slot;

    private IEnumerable<ResolvedPress> Wanted() => plan.Where(Wanted);

    private void Press(ResolvedPress p, float now)
    {
        // The player's press from before the scenario began: there was no chance to make it.
        if (world.Party.Get(p.Slot) is SimPlayer player && p.IsPreStart)
        {
            JobActions.ClearStatuses(player, p.Action.ActionId);
            JobActions.ApplyEffects(player, p.Action.ActionId, (ulong)player.GameObjectId, Random.Shared);
            presses.Add(new MitPress(p.Slot, p.Action.ActionId, p.At));
            return;
        }
        if (world.Party.Get(p.Slot) is not { } who || !who.IsAlive()) return;
        var key = (p.Slot, p.Action.ActionId);
        if (p.Action.Recast > 0f && lastPress.TryGetValue(key, out var last) && now - last < p.Action.Recast - RecastSlack)
        {
            DiagnosticLog.Warn($"[MitPractice] {p.Slot} skips {p.Action.Name} at {now:F2}s: {now - last:F1}s into its {p.Action.Recast:F0}s recast ({p.Label}).");
            return;
        }
        if (p.Action.Requires != 0 && !who.HasStatus(p.Action.Requires))
        {
            DiagnosticLog.Warn($"[MitPractice] {p.Slot} skips {p.Action.Name} at {now:F2}s: needs {Natives.Data.StatusName(p.Action.Requires)} ({p.Label}).");
            return;
        }
        var bosses = p.Action.Kind == MitKind.Debuff ? DebuffTargets(p) : [];
        var target = bosses.FirstOrDefault() ?? (data!.Entries[p.Entry].Buddy ? world.Party.Get(OtherTank(p.Slot)) : null);
        if (who is SimPartyNpc bot)
        {
            // A real player never presses over a cast bar.
            bot.CancelCast(CastCancelReason.SelfCancelled);
            bot.UseAction(p.Action.ActionId, target);
        }
        else
        {
            JobActions.ClearStatuses(who, p.Action.ActionId);
            JobActions.ApplyEffects(who, p.Action.ActionId, (ulong)(target ?? who).GameObjectId, Random.Shared);
        }
        // A button that heals heals, on top of what the plan wants it for.
        if (PartyHp.HealCatalogue.Find(p.Action.ActionId) is { } heal) world.Healing.Perform(who, heal, target);
        // A real player presses a debuff in range of the boss, so it always lands.
        foreach (var boss in bosses)
            foreach (var (status, duration) in JobActions.GrantsOf(p.Action.ActionId))
                if (!boss.HasStatus(status)) boss.AddStatusParam(status, 0, duration);
        var pressedAt = p.IsPreStart ? p.At : now;
        lastPress[key] = pressedAt;
        presses.Add(new MitPress(p.Slot, p.Action.ActionId, pressedAt));
    }

    private static PartyRole OtherTank(PartyRole slot) => slot == PartyRole.MainTank ? PartyRole.OffTank : PartyRole.MainTank;

    // A heal landing on a member's simulated HP, as a share of its max; the HP the real bar shows after, or
    // null when the member isn't modelled here.
    internal uint? Heal(SimCharacter who, float fractionOfMax)
    {
        if (!MovesHp || !pools.TryGetValue(who, out var pool) || who.Proxy is not { Exists: true } chara) return null;
        pool.Heal(fractionOfMax * pool.Max);
        if (!writeDue.ContainsKey(who)) Write(who, pool);
        return (uint)Math.Clamp(MathF.Round(pool.Hp / pool.Max * chara.MaxHealth), 1f, chara.MaxHealth);
    }

    // The modelled share of max HP, or null when the member isn't modelled here.
    internal float? HpFraction(SimCharacter who) => MovesHp && pools.TryGetValue(who, out var pool) ? MathF.Max(0f, pool.Hp) / pool.Max : null;

    // The next hit the plan has scheduled after `now`, on the scenario's clock.
    internal float? NextHitAt(float now)
        => data?.Schedule.Where(h => h.At > now).Select(h => (float?)h.At).Min();

    // The local player's press, from UserActions.
    internal void RecordPress(PartyRole slot, uint actionId)
    {
        if (!IsActive || MitCatalogue.Find(actionId) == null) return;
        presses.Add(new MitPress(slot, actionId, clock()));
    }

    public void Hit(SimCharacter? victim, string key)
    {
        if (victim != null) Hit([victim], key);
    }

    public void HitParty(string key) => Hit(world.Party.AllMembers().ToList(), key);

    // A hit the plan's table doesn't hold, because a scenario's own events land it.
    public void HitParty(MitHitDef hit)
    {
        if (!IsActive || !hpMode || hit.Raw == null) return;
        var now = clock();
        foreach (var victim in world.Party.AllMembers().ToList()) Land(victim, hit, now, 0f);
    }

    public void Hit(IEnumerable<SimCharacter> victims, string key)
    {
        if (!IsActive || !hpMode || data!.Hits.FirstOrDefault(h => h.Key == key) is not { Raw: not null } hit) return;
        var now = clock();
        foreach (var victim in victims) Land(victim, hit, now, 0f);
    }

    // An EnemyAction's resolve, before its ActionEffect goes out: the plan's hit for that action lands
    // on everyone it caught, and what each lost replaces the flytext's stock number. The bars follow
    // damageDelay later, as the numbers do.
    internal IReadOnlyDictionary<SimCharacter, uint> Resolved(uint actionId, IReadOnlyList<SimCharacter> hits, float damageDelay, SimCharacter? target)
    {
        if (!IsActive || !hpMode || data!.Hits.FirstOrDefault(h => h.ActionIds?.Contains(actionId) == true) is not { Raw: not null } hit)
            return new Dictionary<SimCharacter, uint>();
        var now = clock();
        if (hit.SoloRaw is { } solo && hits.Count == 1) hit = hit with { Raw = solo, TankRaw = null, TankBuster = true };
        var shown = new Dictionary<SimCharacter, uint>();
        foreach (var victim in hits)
            if ((!hit.TargetOnly || ReferenceEquals(victim, target)) && Land(victim, hit, now, damageDelay) is >= 1f and var loss) shown[victim] = (uint)MathF.Round(loss);
        return shown;
    }

    // The HP the hit took, or null when it landed on no pool.
    private float? Land(SimCharacter victim, MitHitDef hit, float now, float damageDelay)
    {
        if (victim is not ISimPartyMember member || !victim.IsAlive() || !pools.TryGetValue(victim, out var pool)) return null;
        if (hit.ToOneHp)
        {
            pool.SetTo(1f);
            world.Healing.NoteHit(victim);
            writeDue[victim] = now + damageDelay + world.PartyHp.RollTickDelay();
            return null;
        }
        var ledger = Ledger(victim);
        ledger.Sync(victim, pool.Max);
        var source = hit.Source is { } s ? Layers(Source(s)) : [];
        var result = MitDamage.Apply(hit, MitProfile.ClassOf((JobId)member.ClassJob), data!.Profile, Layers(victim), source,
            ledger.Shields, assumeTankCooldowns: world.Party.IsBotDriven(victim));
        ledger.Commit(victim);
        writeDue[victim] = now + damageDelay + world.PartyHp.RollTickDelay();
        // The mitigation was judged now; the damage lands, for the healers and the HP model, damageDelay later.
        if (damageDelay > 0f)
        {
            landings.Add((now + damageDelay, victim, hit, result));
            return result.HpLoss;
        }
        Damage(member, victim, pool, hit, result, now);
        return result.HpLoss;
    }

    private void Damage(ISimPartyMember member, SimCharacter victim, MitHpPool pool, MitHitDef hit, MitHitResult result, float now)
    {
        var hp = pool.Land(result.HpLoss, hit.Fresh);
        world.Healing.NoteHit(victim);
        if (hp > 0f) return;
        if (warnedAt.TryGetValue(victim, out var warned) && now - warned <= data!.Profile.TopUpGap) return;
        warnedAt[victim] = now;
        world.Announce($"{hit.Name} would have killed {Who(member)}: {MitDamage.Explain(result, Missing(victim, now))}");
    }

    // Planned presses meant for a hit around now that aren't in effect on the victim.
    private IReadOnlyList<string> Missing(SimCharacter victim, float now)
    {
        var missing = new List<string>();
        foreach (var p in Wanted())
        {
            if (!p.Covers.Any(c => MathF.Abs(c - now) <= MissingWithin) || InEffect(p, victim, now)) continue;
            missing.Add($"{Owner(p.Slot)} {p.Action.Name}");
        }
        return missing.Distinct().ToList();
    }

    private bool InEffect(ResolvedPress p, SimCharacter victim, float now)
    {
        var granted = JobActions.GrantsOf(p.Action.ActionId).Select(g => g.StatusId).Where(Mitigation.ByStatusId.ContainsKey).ToList();
        return p.Action.Kind switch
        {
            MitKind.None => true,
            MitKind.Debuff => DebuffTargets(p).Any(boss => granted.Any(boss.HasStatus)),
            MitKind.Shield => presses.Any(x => x.Slot == p.Slot && MitCatalogue.Find(x.ActionId)?.ActionId == p.Action.ActionId
                                               && x.At <= now && now - x.At <= p.Action.Duration),
            _ => granted.Any(victim.HasStatus),
        };
    }

    // When the run ends, the local player's grading in chat.
    internal void Finish(MitRunEnd end)
    {
        if (!IsActive) return;
        finished = true;
        DiagnosticLog.Info($"[MitPractice] Finish ({end}) at {clock():F2}s: {presses.Count} presses recorded.");
        if (world.Party.Player is not { } player) return;
        foreach (var line in Summary(player, clock()))
            Plugin.ChatGui.Print(new XivChatEntry { Type = XivChatType.SystemMessage, Message = $"{PluginIdentity.ChatPrefix} {line}" });
    }

    private IReadOnlyList<string> Summary(SimPlayer player, float now)
    {
        if (!grading) return ["Mitigation: your presses weren't graded. Turn on \"Resolve your own actions\" to grade them."];
        var wanted = Wanted().ToList();
        if (!wanted.Any(p => p.Slot == player.Role && !p.IsPreStart))
            return [$"Mitigation: no planned presses for your {JobName((JobId)player.ClassJob)} in {player.Role.ShortLabel()}."];
        return MitGrader.Summary(MitGrader.Grade(wanted, player.Role, presses, now), now);
    }

    internal void Clear()
    {
        data = null;
        plan = [];
        sources = new Dictionary<MitSource, Func<SimCharacter?>>();
        clock = () => world.Events.Elapsed;
        pending.Clear();
        lastPress.Clear();
        presses.Clear();
        pools.Clear();
        ledgers.Clear();
        warnedAt.Clear();
        landings.Clear();
        writeDue.Clear();
        hpMode = false;
        grading = false;
        finished = false;
    }

    private SimCharacter? Source(MitSource source) => sources.TryGetValue(source, out var actor) ? actor() : null;

    // The boss a debuff goes on: the row's, else the plan's, else every boss, as one pressed in
    // range of both.
    private IReadOnlyList<SimCharacter> DebuffTargets(ResolvedPress p)
        => ((data!.Entries[p.Entry].On ?? data.DebuffTarget) is { } target ? [Source(target)] : sources.Values.Select(actor => actor()))
            .OfType<SimCharacter>().ToList();

    private ShieldLedger Ledger(SimCharacter member)
    {
        if (!ledgers.TryGetValue(member, out var ledger)) ledgers[member] = ledger = new ShieldLedger();
        return ledger;
    }

    private static IReadOnlyList<MitLayer> Layers(SimCharacter? character)
        => character?.ActiveStatuses
                    .Where(s => Mitigation.ByStatusId.ContainsKey(s.StatusId))
                    .Select(s => new MitLayer(s.StatusId, Natives.Data.StatusName(s.StatusId), Mitigation.ByStatusId[s.StatusId]))
                    .ToList()
           ?? [];

    private static void Write(SimCharacter member, MitHpPool pool) => member.WriteHp(pool.Hp / pool.Max);

    private string Owner(PartyRole slot) => world.Party.Player is { } player && player.Role == slot ? "your" : $"{slot.ShortLabel()}'s";

    private static string Who(ISimPartyMember member) => member is SimPlayer ? $"you ({member.Role.ShortLabel()})" : member.Role.ShortLabel();

    private static string JobName(JobId job) => Regex.Replace(job.ToString(), "(?<!^)([A-Z])", " $1");
}

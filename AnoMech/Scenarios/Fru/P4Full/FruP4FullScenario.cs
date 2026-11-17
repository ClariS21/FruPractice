using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using AnoMech.Scenarios.Fru.P4DarklitDragonsong;
using Actions = AnoMech.Scenarios.Fru.FruActions;

namespace AnoMech.Scenarios.Fru.P4Full;

// P4 from the bosses' arrival to Memory's End and Absolute Zero: the two drills back to back on one
// clock, with one Usurper, Oracle and Fragment of Fate, and the casts between them.
public sealed class FruP4FullScenario : IScenario, IGuidanceHazardSource
{
    // Each slice's hazards, on the run's clock, once it has begun.
    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
    {
        foreach (var slice in started)
            if (slice is IGuidanceHazardSource source)
                foreach (var hazard in source.Upcoming(now, horizon)) yield return hazard;
    }

    public string Name => "Full phase";
    public IPhase Phase => FruZone.P4;
    public bool SupportsMitigationPractice => true;
    public IReadOnlyList<IScenarioAi> AiStrats { get; }
    public object SettingsOverrides => settingsWindow.Overrides;

    // Uptime between the drills, on top of their own: the staging in the middle before Darklit Dragonsong, and the stack
    // after the first Morn Afah until Crystallize Time's debuffs. Nothing positional goes off then.
    internal static readonly (float From, float To, float MaxDistance)[] UptimeWindows = [(6f, 12.8f, 2.5f), (81.5f, 94f, 2.5f)];

    // Each drill's own casts land on the timeline's times this far into the run.
    internal const float DarklitDragonsongAt = 13.85f;
    internal const float CrystallizeTimeAt = 85.3f;

    private readonly FruP4DarklitDragonsongScenario darklitDragonsong = new();
    private readonly FruP4CrystallizeTimeScenario crystallizeTime = new();
    private readonly FruP4FullSettingsWindow settingsWindow;
    // Only the slice playing hears the sim's events: each drill's checks are about its own mechanics.
    private IScenario? current;
    private readonly List<IScenario> started = [];

    public FruP4FullScenario()
    {
        settingsWindow = new FruP4FullSettingsWindow(darklitDragonsong, crystallizeTime);
        AiStrats =
        [
            .. Enumerable.Range(0, darklitDragonsong.AiStrats.Count).Select(i => new FruP4FullStrat(
                i, 0, darklitDragonsong.AiStrats[i].Name, crystallizeTime.AiStrats[0].Name, crystallizeTime.AiStrats[0].Group)),
        ];
    }

    public void DrawSettings() => settingsWindow.Draw();

    public IReadOnlyList<BurstAnchor> BurstAnchors =>
    [
        .. BurstAnchor.Shifted(darklitDragonsong.BurstAnchors, DarklitDragonsongAt),
        .. BurstAnchor.Shifted(crystallizeTime.BurstAnchors, CrystallizeTimeAt),
    ];

    public void Run(SimWorld world, int? selectedAi)
    {
        current = null;
        started.Clear();
        foreach (var (from, to, maxDistance) in UptimeWindows) world.Uptime.Allow(world.Events.Elapsed + from, world.Events.Elapsed + to, maxDistance);
        var strat = selectedAi is { } idx && idx < AiStrats.Count ? (FruP4FullStrat)AiStrats[idx] : null;
        var state = new FruP4FullState(darklitDragonsong.Roll(world), crystallizeTime.Roll(world));
        var cast = new FruP4Cast { Chained = true };
        SimEnemy? edgeHelper = null;
        if (strat is not null) new FruP4FullAi().Run(world);
        world.MitPractice.Begin(MitigationPlan(() => darklitDragonsong.SomberTank), new Dictionary<MitSource, Func<SimCharacter?>>
        {
            [FruMitigation.Usurper] = () => cast.Usurper,
            [FruMitigation.Oracle] = () => cast.Oracle,
        });

        world.Events.Add(0f, () =>
        {
            cast.Usurper = FruP4Cast.SpawnUsurper(world, targetable: false);
            cast.Oracle = FruP4Cast.SpawnOracle(world);
            cast.Fragment = FruP4Cast.SpawnFragment(world);
            edgeHelper = FruP4Cast.SpawnEdgeHelper(world);
        });
        world.Events.Add(0.8f, () => cast.Usurper?.SetTargetable(true));
        world.Events.Add(6.2f, () => cast.Usurper?.Cast(Actions.Materialization));
        world.Events.Add(DarklitDragonsongAt, () => Start(darklitDragonsong,
            () => darklitDragonsong.Play(world, strat?.DarklitDragonsong, state.DarklitDragonsong, cast)));
        world.Events.Add(74.8f, () => cast.Oracle?.Cast(Actions.MornAfah, world.Party.Get(PartyRole.MainTank)));
        world.Events.Add(CrystallizeTimeAt, () => Start(crystallizeTime,
            () => crystallizeTime.Play(world, strat?.CrystallizeTime, state.CrystallizeTime, cast)));
        world.Events.Add(156.3f, () => FruP4Cast.CastEdgeOfOblivion(cast.Fragment, edgeHelper));
        world.Events.Add(157.5f, () => cast.Oracle?.Cast(Actions.MornAfah, world.Party.Get(PartyRole.MainTank)));
        world.Events.Add(166.5f, () => cast.Oracle?.Cast(Actions.MemorysEndOracle));
        world.Events.Add(166.5f, () => cast.Usurper?.Cast(Actions.AbsoluteZero));
        world.Events.Add(176.5f, () => cast.Usurper?.SetTargetable(false));
        world.Events.Add(176.5f, () => cast.Oracle?.SetTargetable(false));
    }

    internal static MitPlanData MitigationPlan(Func<PartyRole?>? somberTank = null) => MitPlanChain.Of(
        (FruP4DarklitDragonsongMitigation.Plan(somberTank, FruP4FullMitigation.MornAfah1At - DarklitDragonsongAt), DarklitDragonsongAt),
        (FruP4FullMitigation.Plan(), 0f),
        (FruP4CrystallizeTimeMitigation.Plan(FruP4FullMitigation.MornAfah2At - CrystallizeTimeAt), CrystallizeTimeAt));

    private void Start(IScenario slice, Action play)
    {
        current = slice;
        started.Add(slice);
        play();
    }

    public void Tick(float delta, float elapsed)
    {
        if (current is null) return;
        current.Tick(delta, elapsed - (current == darklitDragonsong ? DarklitDragonsongAt : CrystallizeTimeAt));
    }

    public void OnActionResolved(ActionResolved resolved) => current?.OnActionResolved(resolved);

    public void OnStatusAdded(StatusAdded added) => current?.OnStatusAdded(added);

    public void OnStatusLost(StatusLost lost) => current?.OnStatusLost(lost);

    public void OnTetherEnded(TetherEnded ended) => current?.OnTetherEnded(ended);
}

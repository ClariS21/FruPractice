using System;
using System.Collections.Generic;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;
using static AnoMech.Scenarios.Fru.FruConstants;
using Actions = AnoMech.Scenarios.Fru.FruActions;

namespace AnoMech.Scenarios.Fru.P3Full;

// P3 from the Oracle's arrival to Memory's End: the two drills back to back on one clock, with one
// Oracle, and the casts between them.
public sealed class FruP3FullScenario : IScenario, IGuidanceHazardSource
{
    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
    {
        foreach (var (slice, _) in started)
            if (slice is IGuidanceHazardSource source)
                foreach (var hazard in source.Upcoming(now, horizon)) yield return hazard;
    }
    public string Name => "Full phase";
    public IPhase Phase => FruZone.P3;
    public bool SupportsMitigationPractice => true;
    public IReadOnlyList<IScenarioAi> AiStrats { get; }
    public object SettingsOverrides => settingsWindow.Overrides;

    // Each drill's own casts land on the timeline's times this far into the run.
    internal const float UltimateRelativityAt = 11.4f;
    internal const float ApocalypseAt = 92f;

    private readonly FruP3UltimateRelativityScenario ultimateRelativity = new();
    private readonly FruP3ApocalypseScenario apocalypse = new();
    private readonly FruP3FullSettingsWindow settingsWindow;
    // Each slice begun so far, with where it started on the run's clock.
    private readonly List<(IScenario Slice, float At)> started = [];

    public FruP3FullScenario()
    {
        settingsWindow = new FruP3FullSettingsWindow(ultimateRelativity, apocalypse);
        AiStrats =
        [
            new FruP3FullStrat(0, 0, ultimateRelativity.AiStrats[0].Name, apocalypse.AiStrats[0].Name, apocalypse.AiStrats[0].Group),
        ];
    }

    public void DrawSettings() => settingsWindow.Draw();

    public IReadOnlyList<BurstAnchor> BurstAnchors =>
    [
        new(FruConstants.ActionId.HellsJudgment, 4.3f),
        .. BurstAnchor.Shifted(ultimateRelativity.BurstAnchors, UltimateRelativityAt),
        .. BurstAnchor.Shifted(apocalypse.BurstAnchors, ApocalypseAt),
    ];

    public void Run(SimWorld world, int? selectedAi)
    {
        started.Clear();
        var strat = selectedAi is { } idx && idx < AiStrats.Count ? (FruP3FullStrat)AiStrats[idx] : null;
        var state = new FruP3FullState(ultimateRelativity.Roll(world), apocalypse.Roll(world));
        var cast = new FruP3Cast { Chained = true };
        if (strat is not null) new FruP3FullAi().Run(world);
        world.MitPractice.Begin(MitigationPlan(), new Dictionary<MitSource, Func<SimCharacter?>> { [FruMitigation.Oracle] = () => cast.Oracle });

        world.Events.Add(0f, () => cast.Oracle = FruP3Cast.SpawnOracle(world, targetable: false));
        world.Events.Add(4.3f, () => cast.Oracle?.SetTargetable(true));
        world.Events.Add(4.3f, () => cast.Oracle?.Cast(Actions.HellsJudgment));
        world.Events.Add(UltimateRelativityAt, () => Start(ultimateRelativity, UltimateRelativityAt,
            () => ultimateRelativity.Play(world, strat?.UltimateRelativity, state.UltimateRelativity, cast)));
        world.Events.Add(73.4f, () => cast.Oracle?.VoiceLine(VoiceLineId.ShockwavePulsar));
        world.Events.Add(73.5f, () => cast.Oracle?.Cast(Actions.ShockwavePulsar));
        world.Events.Add(81.8f, () => cast.Oracle?.Cast(Actions.BlackHalo, world.Party.Get(PartyRole.MainTank)));
        world.Events.Add(ApocalypseAt, () => Start(apocalypse, ApocalypseAt,
            () => apocalypse.Play(world, strat?.Apocalypse, state.Apocalypse, cast)));
        world.Events.Add(152f, () => cast.Oracle?.Cast(Actions.MemorysEnd));
        world.Events.Add(162f, () => cast.Oracle?.SetTargetable(false));
    }

    // Ultimate Relativity first: its rows from before the scenario are this run's Hell's Judgment ones.
    internal static MitPlanData MitigationPlan() => MitPlanChain.Of(
        (FruP3UltimateRelativityMitigation.Plan(), UltimateRelativityAt),
        (FruP3FullMitigation.Plan(), 0f),
        (FruP3ApocalypseMitigation.Plan(), ApocalypseAt));

    private void Start(IScenario slice, float at, Action play)
    {
        started.Add((slice, at));
        play();
    }

    public void Tick(float delta, float elapsed)
    {
        foreach (var (slice, at) in started) slice.Tick(delta, elapsed - at);
    }

    public void OnActionResolved(ActionResolved resolved)
    {
        foreach (var (slice, _) in started) slice.OnActionResolved(resolved);
    }

    public void OnStatusAdded(StatusAdded added)
    {
        foreach (var (slice, _) in started) slice.OnStatusAdded(added);
    }

    public void OnStatusLost(StatusLost lost)
    {
        foreach (var (slice, _) in started) slice.OnStatusLost(lost);
    }

    public void OnTetherEnded(TetherEnded ended)
    {
        foreach (var (slice, _) in started) slice.OnTetherEnded(ended);
    }
}

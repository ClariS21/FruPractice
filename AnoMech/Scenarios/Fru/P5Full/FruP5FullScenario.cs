using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P5FulgentBlade;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;
using Actions = AnoMech.Scenarios.Fru.FruActions;

namespace AnoMech.Scenarios.Fru.P5Full;

// P5 from Pandora's arrival to Paradise Lost: Fulgent Blade three times and Paradise Regained
// twice, each round its own slice with its own rolls, against one Pandora, with Pandora's Box
// between the rounds.
public sealed class FruP5FullScenario : IScenario
{
    public string Name => "Full phase";
    public IPhase Phase => FruZone.P5;
    public bool SupportsMitigationPractice => true;
    public IReadOnlyList<IScenarioAi> AiStrats { get; }
    public object SettingsOverrides => settingsWindow.Overrides;

    // Each round's own casts land on the timeline's times this far into the run. The second
    // Paradise Regained is pinned by its Polarizing Strikes: the real second round runs its towers
    // and wings shorter than the drill does, so the drill's Paradise Regained itself comes early.
    internal static readonly float[] FulgentBladeAt = [4.4f, 117.3f, 207.85f];
    internal static readonly float[] ParadiseRegainedAt = [41.6f, 153.3f];

    private readonly FruP5FulgentBladeScenario fulgentBlade = new();
    private readonly FruP5ParadiseRegainedScenario paradiseRegained = new();
    private readonly FruP5FullSettingsWindow settingsWindow;
    // Only the slice playing hears the sim's events.
    private IScenario? current;
    private float currentAt;

    public FruP5FullScenario()
    {
        settingsWindow = new FruP5FullSettingsWindow(fulgentBlade, paradiseRegained);
        AiStrats =
        [
            new FruP5FullStrat(0, 0, fulgentBlade.AiStrats[0].Name, paradiseRegained.AiStrats[0].Name, paradiseRegained.AiStrats[0].Group),
        ];
    }

    public void DrawSettings() => settingsWindow.Draw();

    public IReadOnlyList<BurstAnchor> BurstAnchors
        => [.. FulgentBladeAt.SelectMany(at => BurstAnchor.Shifted(fulgentBlade.BurstAnchors, at))];

    public void Run(SimWorld world, int? selectedAi)
    {
        current = null;
        var strat = selectedAi is { } idx && idx < AiStrats.Count ? (FruP5FullStrat)AiStrats[idx] : null;
        var state = new FruP5FullState(
            [.. Enumerable.Range(0, FulgentBladeAt.Length).Select(_ => fulgentBlade.Roll(world))],
            [.. Enumerable.Range(0, ParadiseRegainedAt.Length).Select(_ => paradiseRegained.Roll(world))]);
        var cast = new FruP5Cast { Chained = true };
        if (strat is not null) new FruP5FullAi().Run(world);
        world.MitPractice.Begin(MitigationPlan(), new Dictionary<MitSource, Func<SimCharacter?>> { [FruMitigation.Pandora] = () => cast.Pandora });
        world.Events.Add(FruP5FullMitigation.PandorasBoxAt, () => world.MitPractice.HitParty(FruMitigation.P5.PandorasBox));

        world.Events.Add(0f, () => cast.Pandora = FruP5Cast.SpawnPandora(world, targetable: false));
        world.Events.Add(0.6f, () => cast.Pandora?.SetTargetable(true));
        for (var round = 0; round < FulgentBladeAt.Length; round++)
        {
            var r = round;
            world.Events.Add(FulgentBladeAt[r], () => Start(fulgentBlade, FulgentBladeAt[r],
                () => fulgentBlade.Play(world, strat?.FulgentBlade, state.FulgentBlades[r], cast)));
        }
        for (var round = 0; round < ParadiseRegainedAt.Length; round++)
        {
            var r = round;
            world.Events.Add(ParadiseRegainedAt[r], () =>
            {
                cast.Pandora?.KeepFacing(null);
                Start(paradiseRegained, ParadiseRegainedAt[r],
                    () => paradiseRegained.Play(world, strat?.ParadiseRegained, state.ParadiseRegaineds[r], cast));
            });
        }
        world.Events.Add(99f, () => world.Party.LimitBreak.Set(3f));
        world.Events.Add(100.7f, () => cast.Pandora?.Cast(Actions.PandorasBox));
        world.Events.Add(250.8f, () => cast.Pandora?.Cast(Actions.ParadiseLost));
        world.Events.Add(272.3f, () => cast.Pandora?.SetTargetable(false));
    }

    // In the order the rounds play, Pandora's Box between the first and second.
    internal static MitPlanData MitigationPlan() => MitPlanChain.Of(
        (FruP5FulgentBladeMitigation.Plan(0), FulgentBladeAt[0]),
        (FruP5ParadiseRegainedMitigation.Plan(0), ParadiseRegainedAt[0]),
        (FruP5FullMitigation.Plan(), 0f),
        (FruP5FulgentBladeMitigation.Plan(1), FulgentBladeAt[1]),
        (FruP5ParadiseRegainedMitigation.Plan(1), ParadiseRegainedAt[1]),
        (FruP5FulgentBladeMitigation.Plan(2), FulgentBladeAt[2]));

    private void Start(IScenario slice, float at, Action play)
    {
        current = slice;
        currentAt = at;
        play();
    }

    public void Tick(float delta, float elapsed) => current?.Tick(delta, elapsed - currentAt);

    public void OnActionResolved(ActionResolved resolved) => current?.OnActionResolved(resolved);

    public void OnStatusAdded(StatusAdded added) => current?.OnStatusAdded(added);

    public void OnStatusLost(StatusLost lost) => current?.OnStatusLost(lost);

    public void OnTetherEnded(TetherEnded ended) => current?.OnTetherEnded(ended);
}

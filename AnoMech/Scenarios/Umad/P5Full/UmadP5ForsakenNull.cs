using System;
using System.Collections.Generic;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P5Full;

// The "Forsaken Null" drill: the full phase's Forsaken, run alone from a second before Kefka's
// raidwide. The four marker sets and the voids they leave are the same code, then Forsaken Null's
// bar; Kefka dies at the kill time, as in the full run.
public sealed class UmadP5ForsakenNull : IScenario, IUmadHazardSource
{
    // The puddles are real sim casts here, so the guide reads them like the full phase's.
    public UmadHazards GuidanceHazards { get; } = new();

    public string Name => "Forsaken Null";
    public IPhase Phase => UmadZone.P5;
    public bool SupportsMitigationPractice => true;
    // Flood's track position at the phase's zero, moved on to where Forsaken starts.
    public float BgmSecondsAtStart => 38.83f - UmadP5FullScenario.FloodAt + Origin;
    public IReadOnlyList<IScenarioAi> AiStrats { get; } = [new ForsakenStrat()];

    // The full phase's clock time this drill's zero stands for: the raidwide begins 1 s in.
    internal const float Origin = UmadP5Forsaken.RaidwideAt - 1f;

    private readonly UmadP5Forsaken forsaken = new();

    public void Run(SimWorld world, int? selectedAi)
    {
        GuidanceHazards.Clear();
        // Only the Forsaken rolls and the kill time are used; the other slices' states stay empty.
        var state = new UmadP5FullState(null!, null!, null!, world.Rng, new UmadP5FullStateOverrides(new object(), new object(), new object()));
        var cast = new UmadP5Cast();
        if (selectedAi is not null) new UmadP5FullAi(state, new UmadP5Enmity(world, cast)).RunForsaken(world, Origin);
        world.MitPractice.Begin(MitigationPlan(UmadP5TankPair.FirstInvuln(world.Party)),
            new Dictionary<MitSource, Func<SimCharacter?>> { [UmadMitigation.Kefka] = () => cast.Kefka });
        world.Events.Add(0f, () => cast.Kefka = UmadP5Cast.SpawnKefka(world));
        forsaken.Schedule(world, cast, state.Forsaken, Origin);
        world.Events.Add(state.KillAt - Origin, () =>
        {
            cast.Kefka?.Defeat();
            cast.Kefka?.SetTargetable(false);
        });
    }

    // The full phase's Forsaken rows, on this drill's clock.
    internal static MitPlanData MitigationPlan(PartyRole firstInvuln) => MitPlanChain.Of((UmadP5FullMitigation.Forsaken(firstInvuln), -Origin));

    public void Tick(float delta, float elapsed) => forsaken.Tick();

    public void OnActionResolved(ActionResolved resolved) => forsaken.OnActionResolved(resolved);

    private sealed record ForsakenStrat : IScenarioAi
    {
        public string Name => "Waymarks clockwise past the orange";
    }
}

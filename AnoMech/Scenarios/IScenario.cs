using System.Collections.Generic;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios;

// The mechanic timeline for one encounter fragment. Shared identity lives on the owning
// IZone (via Phase.Zone) and IPhase; a scenario declares only what is its own.
public interface IScenario : IWorldEventListener
{
    string Name { get; }

    // Its phase — usually `=> TopZone.P5`.
    IPhase Phase { get; }

    bool SupportsSolo => false;

    // Has a mitigation plan and calls world.MitPractice.Begin from Run.
    bool SupportsMitigationPractice => false;

    // Selectable strats. Run's selectedAi indexes this (null = solo); region buttons derive
    // from each strat's IScenarioAi.Group.
    IReadOnlyList<IScenarioAi> AiStrats { get; }

    // The track playing when this scenario starts, when it isn't the phase's (0 = the phase's).
    ushort Bgm => 0;

    // How far into that track the real fight is when this scenario starts.
    float BgmSecondsAtStart => 0f;

    // Boss casts the party's burst windows are anchored to, with when each starts on this scenario's
    // clock. A scenario without any leaves the bots' burst actions unused.
    IReadOnlyList<BurstAnchor> BurstAnchors => [];

    void Run(SimWorld world, int? selectedAi);
    void Tick(float delta, float elapsed) { }

    // The fight-wide rolls only: one setting the whole sim shares.
    void DrawSettings() { }

    // Mechanic roles a single player carries -- a Limit Cut number, an Accretion, a tether. Not
    // part of DrawSettings: these are per seat, and mixing them into the fight-wide panel is
    // what made them hard to read. Drawn in their own dialog instead.
    bool HasPerPlayerSettings => false;
    void DrawPerPlayerSettings() { }

    // The overrides object DrawSettings edits; null when there is nothing to configure.
    object? SettingsOverrides => null;

    // Per-player settings the fight can't produce together (see SettingsConflicts). A start is
    // refused while this is non-empty, rather than running something the player didn't ask for.
    IReadOnlyList<string> SettingsConflicts => [];

    // Instance-progress events scheduled when the scenario starts.
    void RunInstanceEvents(SimWorld world) { }

    // How far the run has got, told to the player with their death; null when the scenario
    // doesn't count it.
    string? RunProgress => null;
}

public static class ScenarioExtensions
{
    // Menu label, e.g. "P5 Delta".
    public static string DisplayName(this IScenario scenario)
    {
        var phase = scenario.Phase;
        return string.IsNullOrEmpty(phase.Name) ? scenario.Name : $"{phase.Name} {scenario.Name}";
    }

    public static string FullName(this IScenario scenario)
        => $"{scenario.Phase.Zone.Name} — {scenario.DisplayName()}";
}

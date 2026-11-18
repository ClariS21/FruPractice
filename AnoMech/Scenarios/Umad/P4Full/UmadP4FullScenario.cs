using System.Collections.Generic;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Umad.P4Full;

// The whole of P4 "Kefka Says" on the real phase clock: Kefka is on the field from 0, the upstream drill plays 3.75 s
// in (its clock is the real one minus that), and the tail the drill lacks is its HP-check branch: on a pass Kefka's
// second Poof (125.8 s) and Ultima Upsurge (cast 127.2 s, resolves 132.2 s) and Kefka untargetable at 132.2 s, then the
// phase ends at 163 s. The sim has no boss HP, so the HP check is the HpCheckMode setting, Pass by default here.
public sealed class UmadP4FullScenario : IScenario
{
    public string Name => "Full phase";
    public IPhase Phase => UmadZone.P4;

    // The drill's own clock starts this far into the real phase (the real Kefka Says begins 1.45 s after it).
    public const float P4ClockOffset = 3.75f;
    public const float PhaseEnd = 163f;

    private readonly UmadP4KefkaSaysScenario drill = new();
    private readonly UmadP4FullSettingsWindow settingsWindow;
    private bool playing;

    // Kefka on this run's field, for what watches it (the HP check's untargetable moment).
    public SimEnemy? Kefka { get; private set; }

    public UmadP4FullScenario()
    {
        settingsWindow = new UmadP4FullSettingsWindow(drill);
    }

    public IReadOnlyList<IScenarioAi> AiStrats => drill.AiStrats;
    public object SettingsOverrides => drill.SettingsOverrides;
    public void DrawSettings() => settingsWindow.Draw();

    // The current run's rolls, for MultiplayerManager (as the drill's).
    public UmadP4KefkaSaysState? LastState => drill.LastState;

    public void RunInstanceEvents(SimWorld instanceWorld) => drill.RunInstanceEvents(instanceWorld);

    public void Run(SimWorld world, int? selectedAi)
    {
        playing = false;
        var state = drill.Roll(world);
        Kefka = null;
        world.Events.Add(0f, () => Kefka = UmadP4KefkaSaysScenario.SpawnKefka(world));
        world.Events.Add(P4ClockOffset, () =>
        {
            playing = true;
            drill.Play(world, selectedAi, state, Kefka);
        });
        // Nothing happens on the log between Kefka going untargetable and the phase end; the run ends there.
        world.Events.Add(PhaseEnd, () => { });
    }

    public void Tick(float delta, float elapsed)
    {
        if (playing) drill.Tick(delta, elapsed - P4ClockOffset);
    }

    public void OnActionResolved(ActionResolved resolved) { if (playing) ((IScenario)drill).OnActionResolved(resolved); }
    public void OnStatusAdded(StatusAdded added) { if (playing) ((IScenario)drill).OnStatusAdded(added); }
    public void OnStatusLost(StatusLost lost) { if (playing) ((IScenario)drill).OnStatusLost(lost); }
    public void OnTetherEnded(TetherEnded ended) { if (playing) ((IScenario)drill).OnTetherEnded(ended); }
}

// The drill's settings, plus what the HP check does.
public sealed class UmadP4FullSettingsWindow
{
    private readonly UmadP4KefkaSaysScenario drill;
    private readonly UmadP4KefkaSaysStateOverrides overrides;

    public UmadP4FullSettingsWindow(UmadP4KefkaSaysScenario drill)
    {
        this.drill = drill;
        overrides = (UmadP4KefkaSaysStateOverrides)drill.SettingsOverrides;
        overrides.HpCheck = HpCheckMode.Pass;
    }

    public void Draw()
    {
        if (SettingsGrid.Begin("##umadp4fullhp"))
        {
            SettingsGrid.Row("HP check:");
            if (ImGui.RadioButton("Pass##hpcheck", overrides.HpCheck != HpCheckMode.Fail)) overrides.HpCheck = HpCheckMode.Pass;
            ImGui.SameLine();
            if (ImGui.RadioButton("Fail (enrage)##hpcheck", overrides.HpCheck == HpCheckMode.Fail)) overrides.HpCheck = HpCheckMode.Fail;
            SettingsGrid.End();
        }
        drill.DrawSettings();
    }
}

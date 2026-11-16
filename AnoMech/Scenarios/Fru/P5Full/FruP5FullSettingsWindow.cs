using System;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Fru.P5Full;

public sealed class FruP5FullSettingsWindow(IScenario fulgentBlade, IScenario paradiseRegained)
{
    public FruP5FullStateOverrides Overrides { get; } = new(fulgentBlade.SettingsOverrides!, paradiseRegained.SettingsOverrides!);

    public void Draw()
    {
        DrawSlice("Fulgent Blade (each round)", fulgentBlade.DrawSettings);
        DrawSlice("Paradise Regained (each round)", paradiseRegained.DrawSettings);
    }

    private static void DrawSlice(string label, Action draw)
    {
        if (ImGui.CollapsingHeader(label)) draw();
    }
}

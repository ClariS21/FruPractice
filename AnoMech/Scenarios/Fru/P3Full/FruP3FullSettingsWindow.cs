using System;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Fru.P3Full;

public sealed class FruP3FullSettingsWindow(IScenario ultimateRelativity, IScenario apocalypse)
{
    public FruP3FullStateOverrides Overrides { get; } = new(ultimateRelativity.SettingsOverrides!, apocalypse.SettingsOverrides!);

    public void Draw()
    {
        DrawSlice("Ultimate Relativity", ultimateRelativity.DrawSettings);
        DrawSlice("Apocalypse", apocalypse.DrawSettings);
    }

    private static void DrawSlice(string label, Action draw)
    {
        if (ImGui.CollapsingHeader(label)) draw();
    }
}

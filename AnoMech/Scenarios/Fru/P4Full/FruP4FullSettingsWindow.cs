using System;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Fru.P4Full;

public sealed class FruP4FullSettingsWindow(IScenario darklitDragonsong, IScenario crystallizeTime)
{
    public FruP4FullStateOverrides Overrides { get; } = new(darklitDragonsong.SettingsOverrides!, crystallizeTime.SettingsOverrides!);

    public void Draw()
    {
        DrawSlice("Darklit Dragonsong", darklitDragonsong.DrawSettings);
        DrawSlice("Crystallize Time", crystallizeTime.DrawSettings);
    }

    private static void DrawSlice(string label, Action draw)
    {
        if (ImGui.CollapsingHeader(label)) draw();
    }
}

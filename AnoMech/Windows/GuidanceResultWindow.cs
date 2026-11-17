using AnoMech.Core.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace AnoMech.Windows;

public sealed class GuidanceResultWindow : Window
{
    private readonly Game game;

    public GuidanceResultWindow(Game game) : base($"Path debrief###{PluginIdentity.InternalName}PathDebrief")
    {
        this.game = game;
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing;
        ShowCloseButton = false;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    public override bool DrawConditions() => Plugin.Config.PathGuidance != PathGuidance.Off && game.Guidance.Debrief is not null;

    public override void Draw()
    {
        if (game.Guidance.Debrief is not { } result) return;
        ImGui.TextUnformatted("Your path: green = safe, orange = near, red = clipped. Strat: white.");
        ImGui.BeginChild("findings", new System.Numerics.Vector2(480, 160) * ImGuiHelpers.GlobalScale);
        ImGui.PushTextWrapPos();
        if (result.Findings.Count == 0) ImGui.TextUnformatted("No path findings in the recorded portion of this run.");
        foreach (var finding in result.Findings) ImGui.TextUnformatted(finding.Text);
        ImGui.PopTextWrapPos();
        ImGui.EndChild();
        if (ImGui.Button("Close path debrief")) game.Guidance.Close();
    }
}

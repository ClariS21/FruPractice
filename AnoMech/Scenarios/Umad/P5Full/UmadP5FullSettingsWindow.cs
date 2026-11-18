using System;
using AnoMech.Core.Game.Party;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Scenarios.Umad.P5Full;

public sealed class UmadP5FullSettingsWindow(IScenario flood, IScenario celestriad, IScenario exaflares)
{
    public UmadP5FullStateOverrides Overrides { get; } = new(flood.SettingsOverrides!, celestriad.SettingsOverrides!, exaflares.SettingsOverrides!);

    // Index-aligned with MarkerRoles (0 = a fresh roll every set).
    private static readonly string[] MarkerLabels =
        ["Random each set", "Regen healer", "Shield healer", "Melee A", "Melee B", "Phys ranged", "Caster"];
    private static readonly PartyRole[] MarkerRoles =
    [
        PartyRole.RegenHealer, PartyRole.ShieldHealer,
        PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps,
    ];

    public void Draw()
    {
        if (ImGui.Button("Auto"))
        {
            Overrides.KillAt = UmadP5FullStateOverrides.KillAtDefault;
            Overrides.BossSurvives = false;
            Overrides.ForsakenMarker = null;
        }

        if (SettingsGrid.Begin("##umadp5full"))
        {
            SettingsGrid.FightOnlyNote();
            SettingsGrid.Row("Kefka dies at:");
            var killAt = Overrides.KillAt;
            SettingsGrid.ItemWidth(200);
            if (ImGui.SliderFloat("##killat", ref killAt, UmadP5FullStateOverrides.KillAtMin, UmadP5FullStateOverrides.KillAtMax, "%.1f s"))
                Overrides.KillAt = killAt;

            SettingsGrid.Row("Boss survives:");
            var survives = Overrides.BossSurvives;
            if (ImGui.Checkbox("Forsaken Null lands##bosssurvives", ref survives)) Overrides.BossSurvives = survives;

            SettingsGrid.Row("Forsaken marker:");
            var marker = Overrides.ForsakenMarker is { } role ? Array.IndexOf(MarkerRoles, role) + 1 : 0;
            SettingsGrid.ItemWidth(160);
            if (ImGui.Combo("##forsakenmarker", ref marker, MarkerLabels, MarkerLabels.Length))
                Overrides.ForsakenMarker = marker == 0 ? null : MarkerRoles[marker - 1];
            SettingsGrid.End();
        }

        DrawSlice("Flood", flood.DrawSettings);
        DrawSlice("Celestriad (each Catastrophic Choice is its own roll)", celestriad.DrawSettings);
        DrawSlice("Stray Apocalypse", exaflares.DrawSettings);
    }

    private static void DrawSlice(string label, Action draw)
    {
        if (ImGui.CollapsingHeader(label)) draw();
    }
}

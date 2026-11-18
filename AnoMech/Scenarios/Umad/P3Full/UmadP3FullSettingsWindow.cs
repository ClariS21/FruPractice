using System;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3LimitCut;
using Dalamud.Bindings.ImGui;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P3Full;

public sealed class UmadP3FullSettingsWindow(UmadP3LimitCutScenario limitCut, UmadP3BlackHoleScenario blackHole)
{
    public UmadP3FullStateOverrides Overrides { get; } =
        new((UmadP3LimitCutStateOverrides)limitCut.SettingsOverrides, (UmadP3BlackHoleStateOverrides)blackHole.SettingsOverrides);

    private const PartyRole EditingSeat = PartyRole.MainTank;

    private static readonly string[] ElementLabels = ["Random", "Fire", "Water"];
    private static readonly string[] SideLabels = ["Random", "Left", "Right"];
    private static readonly string[] ImplosionLabels = ["Random", "Longitudinal", "Latitudinal"];
    private static readonly string[] PlanLabels = ["Chaos tank invulns", "Exdeath tank invulns", "Share, Chaos tank first", "Share, Exdeath tank first"];
    private static readonly string[] DebuffLabels = ["Random", "Entropy (fire)", "Dynamic Fluid (water)"];

    public void Draw()
    {
        var opening = Overrides.OpeningRolls;
        if (ImGui.Button("Auto"))
        {
            opening.ShortElement = null;
            opening.FireOnRight = null;
            opening.Implosion = null;
        }
        if (SettingsGrid.Begin("##umadp3full"))
        {
            SettingsGrid.Row("First element:");
            var element = opening.ShortElement switch { UmadP3Element.Fire => 1, UmadP3Element.Water => 2, _ => 0 };
            SettingsGrid.ItemWidth(140);
            if (ImGui.Combo("##p3element", ref element, ElementLabels, ElementLabels.Length))
                opening.ShortElement = element switch { 1 => UmadP3Element.Fire, 2 => UmadP3Element.Water, _ => null };

            SettingsGrid.Row("Fire crystal:");
            var side = opening.FireOnRight switch { false => 1, true => 2, null => 0 };
            SettingsGrid.ItemWidth(140);
            if (ImGui.Combo("##p3fireside", ref side, SideLabels, SideLabels.Length))
                opening.FireOnRight = side switch { 1 => false, 2 => true, _ => null };

            SettingsGrid.Row("First Implosion:");
            var implosion = opening.Implosion switch { ActionId.LongitudinalImplosion => 1, ActionId.LatitudinalImplosion => 2, _ => 0 };
            SettingsGrid.ItemWidth(160);
            if (ImGui.Combo("##p3implosion", ref implosion, ImplosionLabels, ImplosionLabels.Length))
                opening.Implosion = implosion switch { 1 => ActionId.LongitudinalImplosion, 2 => ActionId.LatitudinalImplosion, _ => null };

            DrawPlan("Thunder III 1:", "##p3tb1", Overrides.Thunder1, v => Overrides.Thunder1 = v);
            DrawPlan("Thunder III 2:", "##p3tb2", Overrides.Thunder2, v => Overrides.Thunder2 = v);
            DrawPlan("Thunder III 3:", "##p3tb3", Overrides.Thunder3, v => Overrides.Thunder3 = v);
            DrawPlan("Thunder III 4:", "##p3tb4", Overrides.Thunder4, v => Overrides.Thunder4 = v);
            DrawPlan("Thunder III 5:", "##p3tb5", Overrides.Thunder5, v => Overrides.Thunder5 = v);
            SettingsGrid.End();
        }
        if (ImGui.CollapsingHeader("Limit Cut")) limitCut.DrawRolls();
        if (ImGui.CollapsingHeader("Black Hole")) blackHole.DrawRolls();
    }

    private static void DrawPlan(string label, string id, UmadP3ThunderPlan current, Action<UmadP3ThunderPlan> set)
    {
        SettingsGrid.Row(label);
        var plan = (int)current;
        SettingsGrid.ItemWidth(220);
        if (ImGui.Combo(id, ref plan, PlanLabels, PlanLabels.Length)) set((UmadP3ThunderPlan)plan);
    }

    public void DrawPerPlayer()
    {
        var opening = Overrides.OpeningRolls;
        if (ImGui.Button("Auto")) opening.Debuff.Mine = null;
        if (SettingsGrid.Begin("##umadp3fullplayers"))
        {
            var debuff = opening.Debuff.Effective(EditingSeat) switch { UmadP3Element.Fire => 1, UmadP3Element.Water => 2, _ => 0 };
            SettingsGrid.PlayerRow("element:");
            SettingsGrid.ItemWidth(160);
            if (ImGui.Combo("##p3debuff", ref debuff, DebuffLabels, DebuffLabels.Length))
                opening.Debuff.Set(EditingSeat, debuff switch { 1 => UmadP3Element.Fire, 2 => UmadP3Element.Water, _ => null });
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(opening.Validate());
        if (ImGui.CollapsingHeader("Limit Cut##players")) limitCut.DrawPerPlayerSettings();
        if (ImGui.CollapsingHeader("Black Hole##players")) blackHole.DrawPerPlayerSettings();
    }
}

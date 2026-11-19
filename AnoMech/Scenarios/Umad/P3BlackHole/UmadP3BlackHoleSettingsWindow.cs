using System;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using Dalamud.Bindings.ImGui;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P3BlackHole;

// ImGui panel rendered in the main window's "Scenario config" pane when this scenario is
// active. Owns the StateOverrides instance and writes user choices into it.
public sealed class UmadP3BlackHoleSettingsWindow
{
    public UmadP3BlackHoleStateOverrides Overrides { get; } = new();

    private const PartyRole EditingSeat = PartyRole.MainTank;

    public void Draw()
    {
#if DEBUG
        if (ImGui.Button("Auto")) Overrides.SlapAttacks[0] = null;
#endif
        if (SettingsGrid.Begin("##umadp3blackhole"))
        {
            DrawWhiteHole();
#if DEBUG
            DrawFirstSlap();
#endif
            SettingsGrid.End();
        }
    }

    // As P4's HP check: the sim reads nobody's HP, so whether the party was full is chosen here.
    private void DrawWhiteHole()
    {
        SettingsGrid.Row("White Hole:");
        if (ImGui.RadioButton("Pass##whitehole", Overrides.HpCheck != HpCheckMode.Fail)) Overrides.HpCheck = HpCheckMode.Pass;
        ImGui.SameLine();
        if (ImGui.RadioButton("Fail (petrified)##whitehole", Overrides.HpCheck == HpCheckMode.Fail)) Overrides.HpCheck = HpCheckMode.Fail;
    }

    public void DrawPerPlayer()
    {
        if (ImGui.Button("Auto")) ResetMine();
        if (SettingsGrid.Begin("##umadp3blackholeplayers"))
        {
            DrawLineNumber();
            DrawAccretion();
#if DEBUG
            DrawFirstSlapTarget();
#endif
            SettingsGrid.End();
        }
        SettingsGrid.ConflictRows(Overrides.Validate());
    }

    // Forces the seat into the slot carrying that line number (Auto = the fight's own roll).
    private void DrawLineNumber()
    {
        var v = Overrides.LineNumber.Effective(EditingSeat);
        SettingsGrid.PlayerRow("line:");
        if (ImGui.RadioButton("Auto##line",   v == null)) Overrides.LineNumber.Set(EditingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("First##line",  v == 1))    Overrides.LineNumber.Set(EditingSeat, 1);
        ImGui.SameLine();
        if (ImGui.RadioButton("Second##line", v == 2))    Overrides.LineNumber.Set(EditingSeat, 2);
        ImGui.SameLine();
        if (ImGui.RadioButton("Third##line",  v == 3))    Overrides.LineNumber.Set(EditingSeat, 3);
    }

    // Yes is dropped for tanks and third-in-line, which never carry Accretion in the fight.
    private void DrawAccretion()
    {
        var v = Overrides.Accretion.Effective(EditingSeat);
        SettingsGrid.PlayerRow("Accretion:");
        if (ImGui.RadioButton("Auto##accretion", v == null))  Overrides.Accretion.Set(EditingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##accretion",  v == true))  Overrides.Accretion.Set(EditingSeat, true);
        ImGui.SameLine();
        if (ImGui.RadioButton("No##accretion",   v == false)) Overrides.Accretion.Set(EditingSeat, false);
    }

#if DEBUG
    private void DrawFirstSlap()
    {
        var v = Overrides.SlapAttacks[0];
        SettingsGrid.Row("1st Slap:");
        if (ImGui.RadioButton("Auto##firstslap",  v == null))                     Overrides.SlapAttacks[0] = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Left##firstslap",  v == ActionId.SlapHappy_Left))  Overrides.SlapAttacks[0] = ActionId.SlapHappy_Left;
        ImGui.SameLine();
        if (ImGui.RadioButton("Right##firstslap", v == ActionId.SlapHappy_Right)) Overrides.SlapAttacks[0] = ActionId.SlapHappy_Right;
    }

    private void DrawFirstSlapTarget()
    {
        var v = Overrides.FirstSlapAllOnMe.Effective(EditingSeat);
        SettingsGrid.Row(PerRole.ForceSeats ? "1st Slap at:" : "1st Slap Target:");
        if (ImGui.RadioButton("Auto##firstslaptarget", v != true)) Overrides.FirstSlapAllOnMe.Set(EditingSeat, null);
        ImGui.SameLine();
        if (ImGui.RadioButton($"{(PerRole.ForceSeats ? "This seat" : "Player")}##firstslaptarget", v == true))
            Overrides.FirstSlapAllOnMe.Set(EditingSeat, true);
    }
#endif

    // Only matters when a tank slot is bot-driven. Two rows: the sim casts Thunder III twice.
    public void DrawThunderIIIPlan()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Thunder III plan (planning tank bots will follow):");
        DrawThunderIIIRow("##thunder1", "Set 1 (~42.6s):", Overrides.ThunderSet1, Overrides.ThunderSet2, v => Overrides.ThunderSet1 = v);
        DrawThunderIIIRow("##thunder2", "Set 2 (~83.9s):", Overrides.ThunderSet2, Overrides.ThunderSet1, v => Overrides.ThunderSet2 = v);
    }

    private static void DrawThunderIIIRow(string idSuffix, string label, ThunderIIIAssignment current, ThunderIIIAssignment otherSet, Action<ThunderIIIAssignment> set)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        DrawThunderIIIOption("MT invulns both" + idSuffix, ThunderIIIAssignment.MtInvulnsBoth, current, otherSet, set);
        ImGui.SameLine();
        DrawThunderIIIOption("OT invulns both" + idSuffix, ThunderIIIAssignment.OtInvulnsBoth, current, otherSet, set);
        ImGui.SameLine();
        DrawThunderIIIOption("Share, MT first" + idSuffix, ThunderIIIAssignment.ShareMtFirst, current, otherSet, set);
        ImGui.SameLine();
        DrawThunderIIIOption("Share, OT first" + idSuffix, ThunderIIIAssignment.ShareOtFirst, current, otherSet, set);
    }

    // Invuln and Share are separate pools: neither an invuln nor the big self-mit cooldowns
    // recover in the ~41s between sets.
    private static void DrawThunderIIIOption(string label, ThunderIIIAssignment option, ThunderIIIAssignment current, ThunderIIIAssignment otherSet, Action<ThunderIIIAssignment> set)
    {
        var isShare = option is ThunderIIIAssignment.ShareMtFirst or ThunderIIIAssignment.ShareOtFirst;
        var otherIsShare = otherSet is ThunderIIIAssignment.ShareMtFirst or ThunderIIIAssignment.ShareOtFirst;
        var blocked = isShare ? otherIsShare : option == otherSet;
        if (blocked) ImGui.BeginDisabled();
        if (ImGui.RadioButton(label, current == option) && !blocked) set(option);
        if (blocked)
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(isShare
                    ? "Both tanks already shared the other set -- their own mitigation kit's big cooldowns (120s/90s) can't recover in ~41s, so neither can reach the required mitigation again."
                    : "That tank already invulns the other set -- no tank invuln recovers in ~41s, so they can't do both.");
        }
    }

    private void ResetMine()
    {
        Overrides.LineNumber.Mine = null;
        Overrides.Accretion.Mine = null;
        Overrides.FirstSlapAllOnMe.Mine = null;
    }
}

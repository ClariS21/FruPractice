using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3LimitCut;

namespace AnoMech.Scenarios.Umad.P3Full;

// The opening's rolls, the five Thunder III plans by boss, and each drill's own rolls, which the
// drill reads as it starts. A drill's own Thunder III plan is not used: the phase sets all five.
public sealed class UmadP3FullStateOverrides(UmadP3LimitCutStateOverrides limitCut, UmadP3BlackHoleStateOverrides blackHole)
{
    public UmadP3SliceSettings Opening { get; } = new(new UmadP3OpeningStateOverrides());
    public UmadP3SliceSettings LimitCut { get; } = new(limitCut);
    public UmadP3SliceSettings BlackHole { get; } = new(blackHole);

    // The group's: a swap, the Chaos tank's invuln, a swap, the Exdeath tank's invuln, a swap.
    public UmadP3ThunderPlan Thunder1 { get; set; } = UmadP3ThunderPlan.ShareChaosTankFirst;
    public UmadP3ThunderPlan Thunder2 { get; set; } = UmadP3ThunderPlan.ChaosTankInvulns;
    public UmadP3ThunderPlan Thunder3 { get; set; } = UmadP3ThunderPlan.ShareExdeathTankFirst;
    public UmadP3ThunderPlan Thunder4 { get; set; } = UmadP3ThunderPlan.ExdeathTankInvulns;
    public UmadP3ThunderPlan Thunder5 { get; set; } = UmadP3ThunderPlan.ShareExdeathTankFirst;

    internal UmadP3OpeningStateOverrides OpeningRolls => (UmadP3OpeningStateOverrides)Opening.Overrides;
    internal UmadP3LimitCutStateOverrides LimitCutRolls => (UmadP3LimitCutStateOverrides)LimitCut.Overrides;
    internal UmadP3BlackHoleStateOverrides BlackHoleRolls => (UmadP3BlackHoleStateOverrides)BlackHole.Overrides;
}

// The settings summary shows a slice's forced rolls under its name.
public sealed record UmadP3SliceSettings(object Overrides)
{
    public override string ToString()
        => ScenarioSettingsSummary.Describe(Overrides) is { Count: > 0 } lines ? string.Join("; ", lines) : "Auto";
}

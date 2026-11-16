namespace AnoMech.Scenarios.Fru.P3Full;

// Each slice reads its own settings as it starts.
public sealed class FruP3FullStateOverrides(object ultimateRelativity, object apocalypse)
{
    public FruP3SliceSettings UltimateRelativity { get; } = new(ultimateRelativity);
    public FruP3SliceSettings Apocalypse { get; } = new(apocalypse);
}

// The settings summary shows a slice's forced rolls under its name.
public sealed record FruP3SliceSettings(object Overrides)
{
    public override string ToString()
        => ScenarioSettingsSummary.Describe(Overrides) is { Count: > 0 } lines ? string.Join("; ", lines) : "Auto";
}

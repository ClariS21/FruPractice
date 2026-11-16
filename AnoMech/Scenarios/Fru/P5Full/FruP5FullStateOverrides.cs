namespace AnoMech.Scenarios.Fru.P5Full;

// Each drill's settings apply to every round of it.
public sealed class FruP5FullStateOverrides(object fulgentBlade, object paradiseRegained)
{
    public FruP5SliceSettings FulgentBlade { get; } = new(fulgentBlade);
    public FruP5SliceSettings ParadiseRegained { get; } = new(paradiseRegained);
}

// The settings summary shows a drill's forced rolls under its name.
public sealed record FruP5SliceSettings(object Overrides)
{
    public override string ToString()
        => ScenarioSettingsSummary.Describe(Overrides) is { Count: > 0 } lines ? string.Join("; ", lines) : "Auto";
}

namespace AnoMech.Scenarios.Fru.P4Full;

// Each slice reads its own settings as it starts.
public sealed class FruP4FullStateOverrides(object darklitDragonsong, object crystallizeTime)
{
    public FruP4SliceSettings DarklitDragonsong { get; } = new(darklitDragonsong);
    public FruP4SliceSettings CrystallizeTime { get; } = new(crystallizeTime);
}

// The settings summary shows a slice's forced rolls under its name.
public sealed record FruP4SliceSettings(object Overrides)
{
    public override string ToString()
        => ScenarioSettingsSummary.Describe(Overrides) is { Count: > 0 } lines ? string.Join("; ", lines) : "Auto";
}

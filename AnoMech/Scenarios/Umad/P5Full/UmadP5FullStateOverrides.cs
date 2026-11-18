using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Umad.P5Full;

// Each drill's settings apply to every round of it. The sim has no boss HP: Kefka dies at KillAt,
// unless BossSurvives lets Forsaken Null land.
public sealed class UmadP5FullStateOverrides(object flood, object celestriad, object exaflares)
{
    // Clears kill him from about 208 s to 224 s: Forsaken Null's bar ends at 220 s, and its blast
    // lands 6.4 s later.
    public const float KillAtDefault = 214f;
    public const float KillAtMin = 200f;
    public const float KillAtMax = 226f;

    public UmadP5SliceSettings Flood { get; } = new(flood);
    public UmadP5SliceSettings Celestriad { get; } = new(celestriad);
    public UmadP5SliceSettings StrayApocalypse { get; } = new(exaflares);

    public float KillAt { get; set; } = KillAtDefault;
    public bool BossSurvives { get; set; }

    // Null leaves it to each set's roll.
    public PartyRole? ForsakenMarker { get; set; }

    // Pins for tests: every Fell Forces round's healer and DPS stack, the first notes' three Holy
    // targets in both Orchestras, and each Forsaken set's black-hole pattern.
    internal PartyRole? FellForcesHealer { get; set; }
    internal PartyRole? FellForcesDps { get; set; }
    internal PartyRole[]? OrchestraHoly { get; set; }
    internal int? ForsakenSide { get; set; }
    internal int? ForsakenPick { get; set; }
}

// The settings summary shows a drill's forced rolls under its name.
public sealed record UmadP5SliceSettings(object Overrides)
{
    public override string ToString()
        => ScenarioSettingsSummary.Describe(Overrides) is { Count: > 0 } lines ? string.Join("; ", lines) : "Auto";
}

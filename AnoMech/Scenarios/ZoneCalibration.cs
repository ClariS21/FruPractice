namespace AnoMech.Scenarios;

// The layers tuned against a fight's real data. A zone turns a layer off until it is tuned there;
// a world in no zone, as in a test without one, runs them all.
public sealed record ZoneCalibration
{
    public static readonly ZoneCalibration All = new();

    // Off: every move is timed as Last moment, leaving at the latest safe moment.
    public bool HumanLikeMovement { get; init; } = true;

    // Off: a move that declares its timing moves as one that doesn't.
    public bool Habits { get; init; } = true;

    // Off: every member's HP lands with the hit instead of on its own server tick.
    public bool HpTickJitter { get; init; } = true;

    public bool PathGuidance { get; init; } = true;

    public bool ActEmission { get; init; } = true;
}

using AnoMech.Core.Game.Ai.Habits;

namespace AnoMech.Tests;

// The habits.json the build ships beside the DLL, parsed once per test run: scenario runs move their
// bots as the released plugin does.
internal static class ShippedHabits
{
    private static readonly Lazy<MovementHabits> Loaded = new(() =>
        MovementHabits.Load(MovementHabits.PathIn(TestContext.CurrentContext.TestDirectory))
        ?? throw new InvalidOperationException("The shipped movement/habits.json is missing from the test output or invalid."));

    public static MovementHabits Value => Loaded.Value;
}

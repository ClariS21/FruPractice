using AnoMech.Core.Game.Ai;

namespace AnoMech.Scenarios.Umad.P3Full;

// A strat of each drill, in the order they are played; the opening and the bridges have one.
public sealed record UmadP3FullStrat(int LimitCut, int BlackHole, string LimitCutName, string BlackHoleName) : IScenarioAi
{
    public string Name => $"{LimitCutName} · {BlackHoleName}";
}

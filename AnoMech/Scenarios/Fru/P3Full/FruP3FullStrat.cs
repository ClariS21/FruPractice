using AnoMech.Core.Game.Ai;

namespace AnoMech.Scenarios.Fru.P3Full;

// A strat of each drill, in the order they are played.
public sealed record FruP3FullStrat(int UltimateRelativity, int Apocalypse, string UltimateRelativityName, string ApocalypseName, string? ApocalypseGroup) : IScenarioAi
{
    public string Name => $"{UltimateRelativityName} · {ApocalypseName}";
    public string? Group => ApocalypseGroup;
}

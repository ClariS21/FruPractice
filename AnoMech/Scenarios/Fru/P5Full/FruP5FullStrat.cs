using AnoMech.Core.Game.Ai;

namespace AnoMech.Scenarios.Fru.P5Full;

// A strat of each drill, used in every round of it.
public sealed record FruP5FullStrat(int FulgentBlade, int ParadiseRegained, string FulgentBladeName, string ParadiseRegainedName, string? ParadiseRegainedGroup) : IScenarioAi
{
    public string Name => $"{FulgentBladeName} · {ParadiseRegainedName}";
    public string? Group => ParadiseRegainedGroup;
}

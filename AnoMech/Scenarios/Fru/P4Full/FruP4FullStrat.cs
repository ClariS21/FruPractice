using AnoMech.Core.Game.Ai;

namespace AnoMech.Scenarios.Fru.P4Full;

// A strat of each drill, in the order they are played.
public sealed record FruP4FullStrat(int DarklitDragonsong, int CrystallizeTime, string DarklitDragonsongName, string CrystallizeTimeName, string? CrystallizeTimeGroup) : IScenarioAi
{
    public string Name => $"{DarklitDragonsongName} · {CrystallizeTimeName}";
    public string? Group => CrystallizeTimeGroup;
}

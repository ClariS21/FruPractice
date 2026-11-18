using AnoMech.Core.Game.Ai;

namespace AnoMech.Scenarios.Umad.P5Full;

// The new slices' strat, then a strat of each drill, used in every round of it.
public sealed record UmadP5FullStrat(int Flood, int Celestriad, int Exaflares, string FloodName, string CelestriadName, string ExaflaresName) : IScenarioAi
{
    public string Name => $"Role stacks, Omnitank · {FloodName} · {CelestriadName} · {ExaflaresName}";
}

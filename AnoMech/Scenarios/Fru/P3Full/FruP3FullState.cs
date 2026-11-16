using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;

namespace AnoMech.Scenarios.Fru.P3Full;

// Each slice's rolls, made once at the run's start.
public sealed record FruP3FullState(FruP3UltimateRelativityState UltimateRelativity, FruP3ApocalypseState Apocalypse);

using AnoMech.Scenarios.Fru.P5FulgentBlade;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;

namespace AnoMech.Scenarios.Fru.P5Full;

// Each round's rolls, made once at the run's start.
public sealed record FruP5FullState(
    FruP5FulgentBladeState[] FulgentBlades, FruP5ParadiseRegainedState[] ParadiseRegaineds);

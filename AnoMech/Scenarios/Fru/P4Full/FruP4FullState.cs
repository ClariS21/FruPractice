using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using AnoMech.Scenarios.Fru.P4DarklitDragonsong;

namespace AnoMech.Scenarios.Fru.P4Full;

// Each slice's rolls, made once at the run's start.
public sealed record FruP4FullState(FruP4DarklitDragonsongState DarklitDragonsong, FruP4CrystallizeTimeState CrystallizeTime);

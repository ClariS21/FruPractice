using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3LimitCut;

namespace AnoMech.Scenarios.Umad.P3Full;

// Each slice's rolls, made once at the run's start, and the plan for the buster between the drills.
public sealed record UmadP3FullState(
    UmadP3Tanks Tanks,
    UmadP3OpeningState Opening,
    UmadP3LimitCutState LimitCut,
    ThunderIIIAssignment BridgeThunder,
    UmadP3BlackHoleState BlackHole);

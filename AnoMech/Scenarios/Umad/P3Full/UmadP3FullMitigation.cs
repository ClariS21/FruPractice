using System.Collections.Generic;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3LimitCut;
using static AnoMech.Scenarios.Umad.UmadMitigation.P3;

namespace AnoMech.Scenarios.Umad.P3Full;

// What belongs to neither drill, on the full run's clock: the third Thunder III, which the Limit Cut
// casts and the Black Hole's start sees land, and the tank rows of all five sets, planned over the
// whole phase so each tank's cooldowns follow the sheet's order across the drills. The Reprisal the
// Exdeath tank presses at The Decisive Battle is still up for the third set.
internal static class UmadP3FullMitigation
{
    public static readonly float[] BridgeThunderAt = [126.81f, 129.85f];

    // `busters` is the five sets' plans, in order.
    public static MitPlanData Plan(IReadOnlyList<ThunderIIIAssignment> busters) => new(UmadMitigation.Profile,
        [ThunderIII],
        [new(BridgeThunderAt[0], [ThunderIII.Key]), new(BridgeThunderAt[1], [ThunderIII.Key])],
        UmadP3ThunderMitigation.Rows(
        [
            new(1, UmadP3OpeningMitigation.ThunderAt[0], UmadP3OpeningMitigation.ThunderAt[1], busters[0]),
            new(2, UmadP3FullScenario.LimitCutAt + UmadP3LimitCutMitigation.ThunderAt[0],
                UmadP3FullScenario.LimitCutAt + UmadP3LimitCutMitigation.ThunderAt[1], busters[1]),
            new(3, BridgeThunderAt[0], BridgeThunderAt[1], busters[2]),
            new(4, UmadP3FullScenario.BlackHoleAt + UmadP3BlackHoleMitigation.FirstThunderAt[0],
                UmadP3FullScenario.BlackHoleAt + UmadP3BlackHoleMitigation.FirstThunderAt[1], busters[3]),
            new(5, UmadP3FullScenario.BlackHoleAt + UmadP3BlackHoleMitigation.SecondThunderAt[0],
                UmadP3FullScenario.BlackHoleAt + UmadP3BlackHoleMitigation.SecondThunderAt[1], busters[4]),
        ]),
        UmadMitigation.Chaos);
}

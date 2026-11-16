using System;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.FruConstants;

namespace AnoMech.Scenarios.Fru;

// What the P3 slices of a full run share: the one Oracle, which each slice adopts instead of
// spawning its own.
public sealed class FruP3Cast
{
    public bool Chained { get; init; }
    public SimEnemy? Oracle { get; set; }

    public static SimEnemy? SpawnOracle(SimWorld world, bool targetable) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.OracleP3,
        NameId: BNpcNameId.Oracle,
        Level: Level,
        Targetable: targetable,
        EnemyList: EnemyListMode.Always,
        Placement: new Placement(Vector3.Zero, MathF.PI),
        LayoutId: LayoutId.OracleP3,
        Appearance: Appearance.Oracle));
}

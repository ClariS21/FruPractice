using System;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.FruConstants;

namespace AnoMech.Scenarios.Fru;

// What the P5 slices of a full run share: the one Pandora, which each slice adopts instead of
// spawning its own.
public sealed class FruP5Cast
{
    public bool Chained { get; init; }
    public SimEnemy? Pandora { get; set; }

    public static SimEnemy? SpawnPandora(SimWorld world, bool targetable) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.Pandora,
        NameId: BNpcNameId.Pandora,
        Level: Level,
        Targetable: targetable,
        EnemyList: EnemyListMode.Always,
        Placement: new Placement(Vector3.Zero, MathF.PI),
        LayoutId: LayoutId.PandoraP5));
}

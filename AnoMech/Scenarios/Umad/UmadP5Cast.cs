using System;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad;

// What the P5 slices of a full run share: the one Kefka, which each slice adopts instead of
// spawning its own.
public sealed class UmadP5Cast
{
    public bool Chained { get; init; }
    public SimEnemy? Kefka { get; set; }

    public static SimEnemy? SpawnKefka(SimWorld world) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.KefkaP5,
        NameId: BNpcNameId.Kefka,
        Level: 100,
        Targetable: true,
        EnemyList: EnemyListMode.Always,
        Visibility: SpawnVisibility.Visible,
        Placement: new Placement(Vector3.Zero, MathF.PI),
        LayoutId: BNpcLayoutId.KefkaP5));
}

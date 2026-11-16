using System;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.FruConstants;
using Actions = AnoMech.Scenarios.Fru.FruActions;

namespace AnoMech.Scenarios.Fru;

// What the P4 slices of a full run share: the one Usurper, Oracle and Fragment of Fate, which each
// slice adopts instead of spawning its own.
public sealed class FruP4Cast
{
    public static readonly Vector3 FragmentPosition = new(0, 0, -16);

    public bool Chained { get; init; }
    public SimEnemy? Usurper { get; set; }
    public SimEnemy? Oracle { get; set; }
    public SimEnemy? Fragment { get; set; }

    public static SimEnemy? SpawnUsurper(SimWorld world, bool targetable) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.UsurperP4,
        NameId: BNpcNameId.Usurper,
        Level: Level,
        Targetable: targetable,
        EnemyList: EnemyListMode.Always,
        Placement: new Placement(Vector3.Zero, MathF.PI),
        InitialModeAttributeFlags: 0xFC,
        LayoutId: LayoutId.UsurperP4));

    public static SimEnemy? SpawnOracle(SimWorld world) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.OracleP4,
        NameId: BNpcNameId.Oracle,
        Level: Level,
        EnemyList: EnemyListMode.OnlyWhenVisible,
        Visibility: SpawnVisibility.HiddenUntilShown,
        Placement: new Placement(new Vector3(3.54f, 0, 0), MathF.PI / 4),
        LayoutId: LayoutId.OracleP4,
        Appearance: Appearance.Oracle));

    public static SimEnemy? SpawnFragment(SimWorld world) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.FragmentOfFate,
        NameId: BNpcNameId.FragmentOfFate,
        Level: Level,
        Targetable: true,
        EnemyList: EnemyListMode.Never,
        Placement: new Placement(FragmentPosition, 0f),
        DisplayFlags: 0x40003,
        LayoutId: LayoutId.FragmentOfFate,
        IsHostile: false,
        Statuses: [new(StatusId.Incurable)]));

    public static SimEnemy? SpawnEdgeHelper(SimWorld world) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.Helper,
        NameId: BNpcNameId.FragmentOfFate,
        Level: 1,
        EnemyList: EnemyListMode.Never,
        Visibility: SpawnVisibility.InvisibleHelper,
        Placement: new Placement(Vector3.Zero, 0f)));

    public static void CastEdgeOfOblivion(SimEnemy? fragment, SimEnemy? helper)
    {
        fragment?.Cast(Actions.EdgeOfOblivion);
        helper?.SetPosition(new Placement(FragmentPosition, 0f));
        helper?.Cast(Actions.EdgeOfOblivionAnimation);
    }
}

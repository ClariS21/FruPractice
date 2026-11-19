using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad;

// What the P3 slices of a full run share: the one Chaos, Exdeath and Kefka, and the wind crystal,
// which each slice adopts instead of spawning its own.
public sealed class UmadP3Cast
{
    public bool Chained { get; init; }
    public SimEnemy? Chaos { get; set; }
    public SimEnemy? Exdeath { get; set; }
    public SimEnemy? Kefka { get; set; }
    public SimEventObject? WindCrystal { get; set; }

    public static SimEnemy? SpawnChaos(SimWorld world, Placement at) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.ChaosP3, NameId: BNpcNameId.Chaos, Level: 100,
        Targetable: true, EnemyList: EnemyListMode.Always, Visibility: SpawnVisibility.Visible,
        LayoutId: BNpcLayoutId.ChaosP3, Placement: at));

    public static SimEnemy? SpawnExdeath(SimWorld world, Placement at) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.Exdeath, NameId: BNpcNameId.Exdeath, Level: 100,
        Targetable: true, EnemyList: EnemyListMode.Always, Visibility: SpawnVisibility.Visible,
        LayoutId: BNpcLayoutId.Exdeath, Placement: at));

    // Exdeath's Thunder III buster cast, at whoever is closest to him: the helper's hits then land on
    // the closest player, and cactbot's tank swap call reads who the cast's ability line names.
    public static void CastThunderBuster(SimEnemy? exdeath, SimParty party)
    {
        if (exdeath is null) return;
        exdeath.Cast(ActionId.ThunderIII_Cast, party.Find.Closest(exdeath.Position), animationLock: 3.1f);
    }

    public static SimEnemy? SpawnKefka(SimWorld world, Placement at, SpawnVisibility visibility) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.KefkaP3, NameId: BNpcNameId.Kefka, Level: 100,
        Targetable: false, EnemyList: EnemyListMode.Always, Visibility: visibility,
        LayoutId: BNpcLayoutId.KefkaP3, Placement: at));

    // On the intercardinal Bowels of Agony puts it on (Limit Cut's boss spot: 1 = SE, 3 = NE, 5 = NW, 7 = SW).
    public static SimEventObject? SpawnWindCrystal(SimWorld world, int spot, Vector3 position, float rotation) => world.SpawnEventObject(new EventObjectSpawnConfig
    {
        EObjId = EObjId.WindCrystal,
        LayoutId = spot switch
        {
            1 => EObjId.WindCrystalSeLayoutId,
            3 => EObjId.WindCrystalNeLayoutId,
            5 => EObjId.WindCrystalNwLayoutId,
            _ => EObjId.WindCrystalSwLayoutId,
        },
        TargetableStatus = 5,
        Arg2 = EObjId.WindCrystalArg2,
        Placement = new Placement(position, rotation),
    });
}

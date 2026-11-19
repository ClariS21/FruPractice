using System.Linq;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Scenarios.Umad.P3BlackHole;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Scenarios.Umad.UmadMitigation.P3;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad.P3LimitCut;

// The P3 sheet's rows from Ultima Blaster to The Decisive Battle, on this scenario's clock. Its MT
// column is the Chaos tank's and its OT column the Exdeath tank's. Temperance and Neutral Sect go out
// for the second elements before the scenario starts and carry into it. The 15 s and 20 s buttons
// for Ultima Blaster go out late enough to last through Cyclone. The limit break is the Exdeath
// tank's, which its AI presses when both tanks are bots. Clones and helpers deal every hit but the
// buster, so elsewhere the debuffs on the bosses only soften their autos. The Decisive Battle's
// barriers are for the Earthquakes in Black Hole.
internal static class UmadP3LimitCutMitigation
{
    public static readonly float[] BlasterAt = [8.90f, 10.90f, 12.92f, 14.92f, 16.93f, 18.93f, 20.93f, 22.93f];
    public const float CycloneAt = 20.00f;
    public static readonly float[] ChargeAt = [30.97f, 31.18f, 31.40f, 31.63f, 31.85f, 32.08f, 32.30f, 32.52f];
    public static readonly float[] ThunderAt = [38.58f, 41.62f];

    // The real party's: just before the first clone appears.
    private const float BeforeTheBlasters = 7.4f;
    private const float LimitBreakAt = 14.1f;
    private const float VacuumWaveAt = 14.6f;
    private const float DecisiveBattleAt = 44.99f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    // `thunder` is the buster's plan; a full run plans its tank rows over the whole phase instead.
    public static MitPlanData Plan(UmadP3Tanks tanks, ThunderIIIAssignment? thunder)
    {
        var chaos = Column(tanks.Chaos);
        var exdeath = Column(tanks.Exdeath);
        var first = BlasterAt[0];
        var fifth = BlasterAt[4];
        float[] blasters = [first, CycloneAt];
        float[] afterTheWave = [fifth, BlasterAt[5], CycloneAt, BlasterAt[6]];
        MitPlanEntry[] rows =
        [
            new(WHM, Temperance, -1.7f, "Stray Flames/Tsunami 2 (before the scenario)", [first, fifth]),
            new(AST, NeutralSect, -1.7f, "Stray Flames/Tsunami 2 (before the scenario)", []),

            new(chaos, MitActionId.Reprisal, BeforeTheBlasters, "Ultima Blaster (Chaos's autos)", []),
            new(AST, SunSign, BeforeTheBlasters, "Ultima Blaster", blasters),
            new(SCH, SacredSoil, BeforeTheBlasters, "Ultima Blaster (through Cyclone)", blasters),
            new(SCH, FeyIllumination, BeforeTheBlasters, "Ultima Blaster (through Cyclone)", blasters),
            new(SCH, Seraphism, BeforeTheBlasters, "Ultima Blaster", []),
            new(SGE, Kerachole, BeforeTheBlasters, "Ultima Blaster (through Cyclone)", blasters),
            new(SGE, Panhaima, BeforeTheBlasters, "Ultima Blaster", [first, fifth, CycloneAt]),
            new(D2, Feint, BeforeTheBlasters, "Ultima Blaster (Chaos's autos)", []),
            new(D3, PartyMit, BeforeTheBlasters, "Ultima Blaster (through Cyclone)", blasters),

            new(exdeath, MitPlanEntry.TankLb3, LimitBreakAt, "Vacuum Wave (limit break at the W)", afterTheWave),
            new(WHM, PlenaryIndulgence, VacuumWaveAt, "Vacuum Wave", [fifth, CycloneAt]),
            new(AST, CollectiveUnconscious, VacuumWaveAt, "Vacuum Wave", [fifth, CycloneAt]),

            new(WHM, DivineCaress, CycloneAt - Short, "Cyclone", [CycloneAt]),

            new(exdeath, MitActionId.Reprisal, DecisiveBattleAt, "The Decisive Battle (Exdeath's autos, and the buster after)", [], On: UmadMitigation.Exdeath),
            new(SGE, Zoe, DecisiveBattleAt - 0.3f, "The Decisive Battle (Zoe shields for the Earthquakes)", []),
            new(SCH, DeploymentTactics, DecisiveBattleAt, "The Decisive Battle (Spreadlo for the Earthquakes)", []),
            new(SGE, EukrasianPrognosisII, DecisiveBattleAt, "The Decisive Battle (Zoe shields for the Earthquakes)", []),
        ];
        var busters = thunder is { } plan ? UmadP3ThunderMitigation.Rows([new(2, ThunderAt[0], ThunderAt[1], plan)]) : [];
        return new(UmadMitigation.Profile,
            [UltimaBlaster, Cyclone, UltimaBlasterCharge, ThunderIII],
            [
                .. BlasterAt.Select(at => new MitScheduledHit(at, [UltimaBlaster.Key])),
                new(CycloneAt, [Cyclone.Key]),
                .. ChargeAt.Select(at => new MitScheduledHit(at, [UltimaBlasterCharge.Key])),
                new(ThunderAt[0], [ThunderIII.Key]), new(ThunderAt[1], [ThunderIII.Key]),
            ],
            [.. rows, .. busters],
            UmadMitigation.Chaos);
    }
}

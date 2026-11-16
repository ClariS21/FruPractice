using System.Collections.Generic;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Scenarios.Fru.FruMitigation.P3;
using static AnoMech.Scenarios.Fru.FruMitigationRows;

namespace AnoMech.Scenarios.Fru.P3UltimateRelativity;

// The mitigation sheet's P3 rows from Hell's Judgment to Shell Crusher on this scenario's clock.
// The 15s mitigations the sheet puts slightly after Hell's Judgment are pressed before the
// scenario starts: back in time for Shell Crusher. A Soil or Kerachole the sheet lists too soon
// after the last goes out the moment it's back, so the first hits of a set go without it.
internal static class FruP3UltimateRelativityMitigation
{
    public const float UrAt = 11.02f;
    public const float Fire1 = 22.82f;
    public const float Meltdown1 = 27.97f;
    public const float Fire2 = 32.82f;
    public const float Meltdown2 = 38.02f;
    public const float Fire3 = 42.82f;
    public const float Meltdown3 = 49.02f;
    public const float Final = 54.82f;
    public const float ShellCrusherAt = 59.22f;

    private const float AfterHellsJudgment = -3f;

    private const uint PartyMit = MitPlanEntry.PartyMit;

    public static MitPlanData Plan() => new(FruMitigation.Profile,
        [UltimateRelativity, DarkFire, UnholyDarkness, SinboundMeltdown, DarkEruption, DarkWater, ShellCrusher],
        [
            new(UrAt, [UltimateRelativity.Key]),
            new(Fire1, [DarkFire.Key, UnholyDarkness.Key]), new(Meltdown1, [SinboundMeltdown.Key]),
            new(Fire2, [DarkFire.Key, UnholyDarkness.Key]), new(Meltdown2, [SinboundMeltdown.Key]),
            new(Fire3, [DarkFire.Key, UnholyDarkness.Key]), new(Meltdown3, [SinboundMeltdown.Key]),
            new(Final, [DarkEruption.Key, DarkWater.Key]),
            new(ShellCrusherAt, [ShellCrusher.Key]),
        ],
        Rows, FruMitigation.Oracle);

    private static readonly MitPlanEntry[] Rows =
    [
        new(MT, MitActionId.Reprisal, AfterHellsJudgment, "Ultimate Relativity (after Hell's Judgment)", [UrAt]),
        new(MT, PartyMit, UrAt - Short, "Ultimate Relativity", [UrAt]),
        new(SCH, SacredSoil, AfterHellsJudgment, "Ultimate Relativity (after Hell's Judgment)", [UrAt]),
        new(SCH, Concitation, UrAt - Short, "Ultimate Relativity", [UrAt]),
        new(SGE, Kerachole, AfterHellsJudgment, "Ultimate Relativity (after Hell's Judgment)", [UrAt]),
        new(SGE, EukrasianPrognosisII, UrAt - Short, "Ultimate Relativity", [UrAt]),
        new(D1, Feint, UrAt - Long, "Ultimate Relativity", [UrAt]),
        new(D4, Addle, UrAt - Long, "Ultimate Relativity", [UrAt]),

        new(SCH, DeploymentTactics, Fire1 - Short, "Fire/Dark set 1 and 2", [Fire1, Meltdown1, Fire2]),
        new(SCH, SummonSeraph, Fire1 - Short, "Fire/Dark set 1 and 2", [Fire1, Meltdown1, Fire2]),
        new(SCH, SacredSoil, 27f, "Fire/Dark set 1 and 2 (Soil is back at 27s)", [Meltdown1, Fire2]),
        new(SGE, EukrasianPrognosisII, Fire1 - Short, "Fire/Dark set 1 and 2", [Fire1, Meltdown1, Fire2]),
        new(SGE, Holos, Fire1 - Long, "Fire/Dark set 1 and 2", [Fire1, Meltdown1, Fire2]),
        new(SGE, Panhaima, Fire1 - Short, "Fire/Dark set 1 and 2", [Fire1, Meltdown1, Fire2]),
        new(SGE, Kerachole, 27f, "Fire/Dark set 1 and 2 (Kerachole is back at 27s)", [Meltdown1, Fire2]),
        new(WHM, Temperance, Fire1 - Long, "Fire/Dark set 1 and 2", [Fire1, Meltdown1, Fire2]),
        new(WHM, PlenaryIndulgence, Fire1 - Short, "Fire/Dark set 1 and 2", [Fire1, Meltdown1]),
        new(AST, NeutralSect, Fire1 - Long, "Fire/Dark set 1 and 2", []),
        new(D3, PartyMit, Fire1 - Long, "Fire/Dark set 1 and 2", [Fire1, Meltdown1, Fire2]),

        new(OT, MitActionId.Reprisal, Fire3 - Long, "Fire/Dark set 3 and the rewind", [Fire3, Meltdown3, Final]),
        new(SCH, Concitation, Fire3 - Short, "Fire/Dark set 3 and the rewind", [Fire3, Meltdown3, Final]),
        new(SCH, Seraphism, Fire3 - Long, "Fire/Dark set 3 and the rewind", []),
        new(SGE, EukrasianPrognosisII, Fire3 - Short, "Fire/Dark set 3 and the rewind", [Fire3, Meltdown3, Final]),
        new(SGE, Philosophia, Fire3 - Long, "Fire/Dark set 3 and the rewind", []),
        new(WHM, DivineCaress, Fire3 - Short, "Fire/Dark set 3 and the rewind", [Fire3, Meltdown3]),
        new(WHM, LiturgyOfTheBell, Fire3 - Long, "Fire/Dark set 3 and the rewind", []),
        new(AST, Macrocosmos, Fire3 - Long, "Fire/Dark set 3 and the rewind", []),
        new(AST, SunSign, Fire3 - Short, "Fire/Dark set 3 and the rewind", [Fire3, Meltdown3, Final]),
        .. Barrier(Final, "Fire/Dark set 3 and the rewind"),

        new(MT, MitActionId.Reprisal, ShellCrusherAt - Short, "Shell Crusher", [ShellCrusherAt]),
        new(OT, PartyMit, ShellCrusherAt - Short, "Shell Crusher", [ShellCrusherAt]),
        new(SCH, SacredSoil, ShellCrusherAt - Short, "Shell Crusher", [ShellCrusherAt]),
        new(SGE, Kerachole, ShellCrusherAt - Short, "Shell Crusher", [ShellCrusherAt]),
        new(AST, CollectiveUnconscious, ShellCrusherAt - Short, "Shell Crusher", [ShellCrusherAt]),
        new(D2, Feint, ShellCrusherAt - Long, "Shell Crusher", [ShellCrusherAt]),
    ];
}

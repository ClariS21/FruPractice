using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Core.UserActions.JobId;
using static AnoMech.Scenarios.Umad.UmadMitigation.P3;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad.P3BlackHole;

// The P3 sheet's rows from Earthquake to Stomp-a-Mole, on this scenario's clock. Its MT column is the
// Chaos tank's and its OT column the Exdeath tank's. The raidwide of every tether set is the
// Earthquake a spent crust sets off; the Spreadlo and Zoe shields from The Decisive Battle, before
// the scenario, take the first. A 15 s party mitigation the sheet carries from one row to the next
// goes out late enough to reach it. Seraph is still on recast for the second Shocking Impact in a
// full run, so there it goes out once it is back, for the third tether set. Helpers, Kefka and the
// black holes deal every hit but the busters and Knock Down, so elsewhere the debuffs on Chaos only
// soften his autos.
internal static class UmadP3BlackHoleMitigation
{
    public static readonly float[] QuakeAt = [13.42f, 17.33f, 64.38f, 69.48f, 74.58f, 98.67f, 103.77f, 108.88f, 132.18f, 139.24f];
    public static readonly float[] SlapAt = [24.82f, 55.27f, 123.26f];
    public static readonly float[] NothingnessAt = [32.28f, 39.33f, 62.83f, 67.93f, 73.03f, 97.12f, 102.22f, 107.33f, 130.61f, 137.68f];
    public static readonly float[] FirstThunderAt = [42.63f, 45.63f];
    public static readonly float[] SecondThunderAt = [83.95f, 86.95f];
    public static readonly float[] KnockDownAt = [152.14f, 157.67f];
    public static readonly float[] StompAt = [152.24f, 153.52f, 154.81f, 156.16f];

    // Where the party waits before splitting for Stomp-a-Mole: a Soil there reaches the stack and both towers.
    private const float StompSoilAt = 146.2f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    // `busters` is the two Thunder III sets' plans; a full run plans their tank rows over the whole phase.
    public static MitPlanData Plan(UmadP3Tanks tanks, (ThunderIIIAssignment First, ThunderIIIAssignment Second)? busters)
    {
        var chaos = Column(tanks.Chaos);
        var exdeath = Column(tanks.Exdeath);
        var (quake1, quake2, quake3, quake4, quake5, quake6, quake7, quake8) =
            (QuakeAt[0], QuakeAt[1], QuakeAt[2], QuakeAt[3], QuakeAt[4], QuakeAt[5], QuakeAt[6], QuakeAt[7]);
        var (slap1, slap2, slap3) = (SlapAt[0], SlapAt[1], SlapAt[2]);
        var thunder = SecondThunderAt[0];
        var knockDown = KnockDownAt[0];
        MitPlanEntry[] rows =
        [
            new(SGE, Zoe, -10.03f, "The Decisive Battle (before the scenario: Zoe shields for the Earthquakes)", []),
            new(SCH, DeploymentTactics, -9.73f, "The Decisive Battle (before the scenario: Spreadlo for the Earthquakes)", [quake1]),
            new(SGE, EukrasianPrognosisII, -9.73f, "The Decisive Battle (before the scenario: Zoe shields for the Earthquakes)", [quake1]),

            new(chaos, PartyMit, 10.58f, "Earthquake (through Shocking Impact 1)", [quake1, quake2, slap1], [Gunbreaker, DarkKnight]),
            new(AST, Macrocosmos, 10.58f, "Earthquake", []),
            new(SCH, SacredSoil, 10.58f, "Earthquake (through Shocking Impact 1)", [quake1, quake2, slap1]),
            new(SGE, Kerachole, 10.58f, "Earthquake (through Shocking Impact 1)", [quake1, quake2, slap1]),
            new(D1, Feint, 10.58f, "Earthquake (Chaos's autos)", []),
            new(SGE, Philosophia, 17.6f, "Earthquake (once both Accretions have gone off)", []),

            new(chaos, MitActionId.Reprisal, slap1 - Long, "Shocking Impact 1 (Chaos's autos)", []),
            new(chaos, PartyMit, slap1 - Long, "Shocking Impact 1", [slap1], [Warrior, Paladin]),
            new(WHM, PlenaryIndulgence, slap1 - Short, "Shocking Impact 1", [slap1]),
            new(AST, CollectiveUnconscious, slap1 - Short, "Shocking Impact 1", [slap1]),

            new(exdeath, PartyMit, 50.3f, "Shocking Impact 2 (through the third tether set)", [slap2, quake3], [Gunbreaker, DarkKnight]),
            new(WHM, LiturgyOfTheBell, slap2 - Long, "Shocking Impact 2", []),
            new(SCH, SacredSoil, slap2 - 3f, "Shocking Impact 2 (early, so it is back for Thunder III)", [slap2, quake3]),
            new(SGE, Kerachole, slap2 - 3f, "Shocking Impact 2 (early, so it is back for Thunder III)", [slap2, quake3]),
            new(SCH, Expedient, slap2 - Short, "Shocking Impact 2 (through the fourth tether set)", [slap2, quake3, quake4]),
            new(SGE, Holos, slap2 - Short, "Shocking Impact 2 (through the fourth tether set)", [slap2, quake3, quake4]),
            new(SCH, SummonSeraph, slap2 - Short, "Shocking Impact 2 (and the third tether set)", [slap2, quake3]),
            .. Extra(slap2 - Short, "Shocking Impact 2", [slap2]).Select(e => e.ActionId == Dismantle ? e with { Label = "Shocking Impact 2 (Chaos's autos)", Covers = [] } : e),

            new(exdeath, PartyMit, quake3 - Long, "Black Holes II (third tether set)", [quake3, quake4], [Warrior, Paladin]),
            new(SCH, Consolation, quake3 - Short, "Black Holes II (third tether set)", [quake3]),
            new(SCH, FeyIllumination, 72.9f, "Black Holes II (fifth tether set, through Thunder III)", [quake5, thunder, SecondThunderAt[1]]),

            new(chaos, MitActionId.Reprisal, thunder - 3f, "Thunder III 5 (Chaos's autos)", []),
            new(WHM, Temperance, thunder - Long, "Thunder III 5 (through the sixth tether set)", [thunder, SecondThunderAt[1], quake6]),
            new(AST, NeutralSect, thunder - Long, "Thunder III 5", []),
            new(SCH, DeploymentTactics, thunder - 3f, "Thunder III 5 (Spreadlo)", [thunder]),
            new(SCH, SacredSoil, thunder - 1.55f, "Thunder III 5 (Soil is back)", [thunder, SecondThunderAt[1]]),
            new(SGE, Zoe, thunder - 3.3f, "Thunder III 5 (Zoe shields)", []),
            new(SGE, EukrasianPrognosisII, thunder - 3f, "Thunder III 5 (Zoe shields)", [thunder]),
            new(SGE, Panhaima, thunder - Long, "Thunder III 5 (through the sixth tether set)", [thunder, SecondThunderAt[1], quake6]),
            new(SGE, Kerachole, thunder - 1.55f, "Thunder III 5 (Kerachole is back)", [thunder, SecondThunderAt[1]]),
            new(D2, Feint, thunder - Long, "Thunder III 5 (Chaos's autos)", []),
            new(D4, Addle, thunder - Long, "Thunder III 5", [thunder, SecondThunderAt[1]], On: UmadMitigation.Exdeath),

            new(WHM, DivineCaress, quake6 - Short, "Black Holes III (sixth tether set)", [quake6]),
            new(AST, SunSign, quake6 - Short, "Black Holes III (sixth to eighth tether sets)", [quake6, quake7, quake8]),
            new(D3, PartyMit, quake6 - 2.67f, "Black Holes III (sixth to eighth tether sets)", [quake6, quake7, quake8]),

            new(SCH, SacredSoil, slap3 - 7.26f, "Shocking Impact 3 (early, so it is back for Stomp-a-Mole)", [slap3]),
            new(SGE, Kerachole, slap3 - 7.26f, "Shocking Impact 3 (early, so it is back for Stomp-a-Mole)", [slap3]),
            new(WHM, PlenaryIndulgence, slap3 - Short, "Shocking Impact 3", [slap3]),
            new(AST, CollectiveUnconscious, slap3 - Short, "Shocking Impact 3", [slap3]),

            // The sheet's Reprisals here are for whoever holds Chaos; the Exdeath tank keeps its own for P4.
            new(chaos, MitActionId.Reprisal, knockDown - Long, "Stomp-a-Mole + Knock Down", [.. KnockDownAt]),
            new(chaos, PartyMit, knockDown - Long, "Stomp-a-Mole + Knock Down", [.. KnockDownAt]),
            new(SCH, Seraphism, knockDown - Long, "Stomp-a-Mole + Knock Down", []),
            new(SCH, SacredSoil, StompSoilAt, "Stomp-a-Mole + Knock Down (placed before the party splits for them)", [.. KnockDownAt]),
            new(SGE, Kerachole, StompSoilAt, "Stomp-a-Mole + Knock Down", [.. KnockDownAt]),
            new(D1, Feint, knockDown - Long, "Stomp-a-Mole + Knock Down", [.. KnockDownAt]),
        ];
        IEnumerable<MitPlanEntry> tankRows = busters is var (set1, set2)
            ? UmadP3ThunderMitigation.Rows([new(4, FirstThunderAt[0], FirstThunderAt[1], set1), new(5, SecondThunderAt[0], SecondThunderAt[1], set2)])
            : [];
        return new(UmadMitigation.Profile,
            [Earthquake, ShockingImpact, Shockwave, Nothingness, ThunderIII, KnockDown, StompAMole],
            [
                .. QuakeAt.Select(at => new MitScheduledHit(at, [Earthquake.Key])),
                .. SlapAt.Select(at => new MitScheduledHit(at, [ShockingImpact.Key, Shockwave.Key])),
                .. NothingnessAt.Select(at => new MitScheduledHit(at, [Nothingness.Key])),
                .. FirstThunderAt.Concat(SecondThunderAt).Select(at => new MitScheduledHit(at, [ThunderIII.Key])),
                .. KnockDownAt.Select(at => new MitScheduledHit(at, [KnockDown.Key])),
                .. StompAt.Select(at => new MitScheduledHit(at, [StompAMole.Key])),
            ],
            [.. rows, .. tankRows],
            UmadMitigation.Chaos);
    }
}

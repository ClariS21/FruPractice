using System.Collections.Generic;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Core.UserActions.JobId;
using static AnoMech.Scenarios.Umad.UmadMitigation.P4;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad.P4KefkaSays;

// The P4 sheet on this scenario's clock (the real phase's minus 3.75 s). Neo Exdeath and Chaos deal
// the raidwides, so the party's debuffs on Kefka count only for Ultima Upsurge, as the sheet's notes
// say. Flood of Naught deals no damage: its Sacred Soil and Kerachole go out early for the first
// Death Bolt/Wave so they are back for Ultima Upsurge. The second Ultima Upsurge comes only when the
// HP check passes. Kefka's autos on the main tank are left out, as FRU's plans leave autos out.
internal static class UmadP4KefkaSaysMitigation
{
    public static readonly float[] GrandCrossAt = [20.40f, 35.33f, 50.28f];
    public static readonly float[] InfernoTsunamiAt = [25.53f, 40.45f];
    public static readonly float[] DeathBoltWaveAt = [71.38f, 96.49f];
    public const float UpsurgeAt = 89.61f;
    public const float SecondUpsurgeAt = 128.50f;

    private const float FloodOfNaughtAt = 57.4f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    public static MitPlanData Plan(bool secondUpsurge)
    {
        var (gc1, gc2, gc3) = (GrandCrossAt[0], GrandCrossAt[1], GrandCrossAt[2]);
        var (it1, it2) = (InfernoTsunamiAt[0], InfernoTsunamiAt[1]);
        var (dbw1, dbw2) = (DeathBoltWaveAt[0], DeathBoltWaveAt[1]);
        List<MitPlanEntry> rows =
        [
            new(D2, Feint, 1.5f, "Kefka's autos (start of the phase)", []),

            new(WHM, PlenaryIndulgence, gc1 - Short, "Grand Cross 1", [gc1, it1]),
            new(AST, CollectiveUnconscious, gc1 - Short, "Grand Cross 1", [gc1, it1]),
            new(SCH, DeploymentTactics, gc1 - Short, "Grand Cross 1 (Spreadlo)", [gc1]),
            new(SCH, SacredSoil, gc1 - 0.9f, "Grand Cross 1", [gc1, it1, gc2]),
            new(SGE, Philosophia, gc1 - Short, "Grand Cross 1", []),
            new(SGE, Holos, gc1 - Short, "Grand Cross 1 (through Grand Cross 2)", [gc1, it1, gc2]),
            new(SGE, Kerachole, gc1 - 0.9f, "Grand Cross 1", [gc1, it1]),
            Dismantled(gc1 - Short, "Grand Cross 1 (Kefka's autos)", []),
            .. Barrier(gc1 - Short, "Grand Cross 1", [gc1]),

            new(D3, PartyMit, it1 - Short, "Inferno/Tsunami 1 (through Grand Cross 2)", [it1, gc2]),

            new(WHM, Temperance, gc2 - Short, "Grand Cross 2 (through Grand Cross 3)", [gc2, it2, gc3]),
            new(AST, NeutralSect, gc2 - Short, "Grand Cross 2", []),
            new(SCH, Expedient, gc2 - Short, "Grand Cross 2 (through Grand Cross 3)", [gc2, it2, gc3]),
            new(SCH, FeyIllumination, gc2 - Short, "Grand Cross 2 (through Grand Cross 3)", [gc2, it2, gc3]),
            new(SGE, Panhaima, gc2 - Short, "Grand Cross 2", [gc2, it2]),

            new(OT, PartyMit, it2 - Short, "Inferno/Tsunami 2 (through Grand Cross 3)", [it2, gc3], [Gunbreaker, DarkKnight]),
            new(AST, SunSign, it2 - Short, "Inferno/Tsunami 2 (through Grand Cross 3)", [it2, gc3]),
            new(SCH, SummonSeraph, it2 - Short, "Inferno/Tsunami 2", [it2, gc3]),

            new(OT, PartyMit, gc3 - Long, "Grand Cross 3", [gc3], [Warrior, Paladin]),
            new(WHM, DivineCaress, gc3 - Short, "Grand Cross 3", [gc3]),
            new(SCH, Consolation, gc3 - Short, "Grand Cross 3", [gc3]),
            new(SGE, Zoe, gc3 - Short - 0.3f, "Grand Cross 3 (Zoe shields)", []),
            new(SGE, EukrasianPrognosisII, gc3 - Short, "Grand Cross 3 (Zoe shields)", [gc3]),

            new(WHM, LiturgyOfTheBell, FloodOfNaughtAt, "Flood of Naught", []),
            new(AST, Macrocosmos, FloodOfNaughtAt, "Flood of Naught", []),
            new(SCH, SacredSoil, FloodOfNaughtAt, "Flood of Naught (as Neo Exdeath reappears: up for Death Bolt/Wave 1)", [dbw1]),
            new(SGE, Kerachole, FloodOfNaughtAt, "Flood of Naught (as Neo Exdeath reappears: up for Death Bolt/Wave 1)", [dbw1]),

            new(MT, PartyMit, dbw1 - Long, "Death Bolt/Wave 1", [dbw1]),

            new(MT, MitActionId.Reprisal, UpsurgeAt - Long, "Ultima Upsurge", [UpsurgeAt]),
            new(WHM, PlenaryIndulgence, UpsurgeAt - Short, "Ultima Upsurge (and Death Bolt/Wave 2)", [UpsurgeAt, dbw2]),
            new(AST, CollectiveUnconscious, UpsurgeAt - Short, "Ultima Upsurge (and Death Bolt/Wave 2)", [UpsurgeAt, dbw2]),
            new(SCH, SacredSoil, UpsurgeAt - 1.6f, "Ultima Upsurge (and Death Bolt/Wave 2)", [UpsurgeAt, dbw2]),
            new(SGE, Kerachole, UpsurgeAt - 1.6f, "Ultima Upsurge (and Death Bolt/Wave 2)", [UpsurgeAt, dbw2]),
            new(D1, Feint, UpsurgeAt - Long, "Ultima Upsurge", [UpsurgeAt]),
            new(D4, Addle, UpsurgeAt - Long, "Ultima Upsurge", [UpsurgeAt]),
        ];
        var hits = new List<MitHitDef> { GrandCross, Inferno, Tsunami, DeathBoltStack, DeathBoltSpread, DeathWaveStack, DeathWaveSpread, UltimaUpsurge };
        string[] deathElements = [DeathBoltStack.Key, DeathBoltSpread.Key, DeathWaveStack.Key, DeathWaveSpread.Key];
        var schedule = new List<MitScheduledHit>
        {
            new(gc1, [GrandCross.Key]), new(it1, [Inferno.Key, Tsunami.Key]), new(gc2, [GrandCross.Key]),
            new(it2, [Inferno.Key, Tsunami.Key]), new(gc3, [GrandCross.Key]),
            new(dbw1, deathElements), new(UpsurgeAt, [UltimaUpsurge.Key]), new(dbw2, deathElements),
        };
        if (secondUpsurge)
        {
            schedule.Add(new(SecondUpsurgeAt, [UltimaUpsurge.Key]));
            rows.Add(new(OT, MitActionId.Reprisal, SecondUpsurgeAt - Long, "Ultima Upsurge 2", [SecondUpsurgeAt]));
            rows.Add(new(SCH, SacredSoil, SecondUpsurgeAt - 3.5f, "Ultima Upsurge 2", [SecondUpsurgeAt]));
            rows.Add(new(SGE, Kerachole, SecondUpsurgeAt - 3.5f, "Ultima Upsurge 2", [SecondUpsurgeAt]));
        }
        return new(UmadMitigation.Profile, hits, schedule, rows, UmadMitigation.Kefka);
    }
}

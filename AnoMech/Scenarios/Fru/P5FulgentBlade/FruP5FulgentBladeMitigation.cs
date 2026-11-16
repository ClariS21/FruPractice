using System.Collections.Generic;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Scenarios.Fru.FruMitigation.P5;
using static AnoMech.Scenarios.Fru.FruMitigationRows;

namespace AnoMech.Scenarios.Fru.P5FulgentBlade;

// The mitigation sheet's P5 rows for a Fulgent Blade and the Akh Morn after it, on this scenario's
// clock. The sheet gives each of the three rounds its own rows; the drill is the first.
internal static class FruP5FulgentBladeMitigation
{
    public const float FulgentBladeAt = 7.52f;
    public const float AkhMornAt = 34.47f;

    // Immediately after dodging the first exaline.
    private const float AfterFirstExaline = 21.5f;
    // Soil and Kerachole go out for the Fulgent Blade 30s earlier; this is when they're back.
    private const float SoilBack = FulgentBladeAt - 5f + 30.1f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    public static MitPlanData Plan(int round = 0) => new(FruMitigation.Profile,
        [FulgentBlade, AkhMornLeft, AkhMornRight],
        [new(FulgentBladeAt, [FulgentBlade.Key]), new(AkhMornAt, [AkhMornLeft.Key, AkhMornRight.Key])],
        [.. FulgentBladeRows(round), .. AkhMornRows(round)], FruMitigation.Pandora);

    private static IEnumerable<MitPlanEntry> FulgentBladeRows(int round)
    {
        var fb = FulgentBladeAt;
        const string label = "Fulgent Blade";
        yield return new(round == 0 ? MT : OT, MitActionId.Reprisal, fb - Long, label, [fb]);
        yield return new(SCH, Concitation, fb - Short, label, [fb]);
        yield return new(SCH, SacredSoil, fb - Long, label, [fb]);
        yield return new(SGE, EukrasianPrognosisII, fb - Short, label, [fb]);
        yield return new(SGE, Kerachole, fb - Long, label, [fb]);
        if (round == 2) yield return new(WHM, Temperance, fb - Long, label, [fb]);
        yield return new(WHM, PlenaryIndulgence, fb - Short, label, [fb]);
        if (round == 2) yield return new(AST, NeutralSect, fb - Long, label, []);
        yield return new(AST, CollectiveUnconscious, fb - Short, label, [fb]);
        yield return new(D1, Feint, fb - Long, label, [fb]);
        if (round == 2) yield return Dismantled(fb, label);
    }

    private static IEnumerable<MitPlanEntry> AkhMornRows(int round)
    {
        var am = AkhMornAt;
        string label = $"Akh Morn {round + 1}";
        float[] covers = [am];
        if (round == 0)
        {
            yield return new(MT, PartyMit, am - Short, label, covers);
            yield return new(OT, MitActionId.Reprisal, am - Long, label, covers);
        }
        else
        {
            yield return new(MT, MitActionId.Reprisal, am - Long, label, covers);
            yield return new(MT, PartyMit, am - Short, label, covers);
        }
        yield return new(SCH, DeploymentTactics, am - Short, label, covers);
        yield return new(SCH, SacredSoil, SoilBack, label, covers);
        if (round == 2) yield return new(SGE, Zoe, am - Short - 1.5f, label, []);
        else yield return new(SGE, Holos, round == 0 ? AfterFirstExaline : am - Long, label, covers);
        yield return new(SGE, EukrasianPrognosisII, am - Short, label, covers);
        yield return new(SGE, Kerachole, SoilBack, label, covers);
        yield return new(D2, Feint, am - Long, label, covers);
        yield return new(D4, Addle, am - Long, label, covers);
        if (round != 0) yield break;
        yield return Dismantled(am, label);
        foreach (var barrier in Barrier(am, label)) yield return barrier;
    }
}

using System.Collections.Generic;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Core.UserActions.JobId;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad;

// The P5 sheet's Forsaken rows for its eight hits (the cast Forsaken, then Forsaken Bonds and the
// instant Forsaken in turn): every timed mitigation as late as possible, a second round for the 5th
// hit as the first wears off, and the 8th hit's Sacred Soil and Kerachole once they are back from
// the 1st. `mt` and `ot` are the sheet's tank columns.
internal static class UmadP5ForsakenMitigation
{
    // As late as possible: each round's 15 s buttons last to the 4th and the 8th hit.
    private const float FirstRound = 1.2f;
    private const float SecondRound = 0.8f;
    private const uint PartyMit = MitPlanEntry.PartyMit;

    public static IEnumerable<MitPlanEntry> Rows(float[] hit, SheetColumn mt, SheetColumn ot)
    {
        var first = hit[0] - FirstRound;
        float[] ToThe(int last) => [.. hit[..(last + 1)]];
        float[] FromThe(int from) => [.. hit[from..]];

        yield return new(mt, MitActionId.Reprisal, first, "Forsaken (1st hit, through the 4th)", ToThe(3));
        yield return new(mt, PartyMit, first, "Forsaken (1st hit)", ToThe(0), [Warrior, Paladin]);
        yield return new(mt, PartyMit, first, "Forsaken (1st hit, through the 4th)", ToThe(3), [Gunbreaker, DarkKnight]);
        yield return new(WHM, Temperance, first, "Forsaken (1st hit, through the 5th)", ToThe(4));
        yield return new(WHM, LiturgyOfTheBell, first, "Forsaken (1st hit)", []);
        yield return new(AST, NeutralSect, first, "Forsaken (1st hit)", []);
        yield return new(AST, CollectiveUnconscious, first, "Forsaken (1st hit)", ToThe(2));
        yield return new(SCH, DeploymentTactics, first, "Forsaken (1st hit, Spreadlo)", ToThe(0));
        yield return new(SCH, FeyIllumination, first, "Forsaken (1st hit, through the 4th)", ToThe(3));
        yield return new(SCH, SacredSoil, first, "Forsaken (1st hit)", ToThe(2));
        yield return new(SGE, Zoe, first - 0.3f, "Forsaken (1st hit, Zoe shields)", []);
        yield return new(SGE, EukrasianPrognosisII, first, "Forsaken (1st hit, Zoe shields)", ToThe(0));
        yield return new(SGE, Holos, first, "Forsaken (1st hit, through the 5th)", ToThe(4));
        yield return new(SGE, Kerachole, first, "Forsaken (1st hit)", ToThe(2));
        yield return new(D2, Feint, first, "Forsaken (1st hit, through the 4th)", ToThe(3));
        foreach (var extra in Extra(first, "Forsaken (1st hit)", ToThe(1))) yield return extra;

        yield return new(SCH, Seraphism, hit[1] - 1.5f, "Forsaken Bonds (2nd hit)", []);
        yield return new(SGE, Philosophia, hit[1] - 1.5f, "Forsaken Bonds (2nd hit)", []);

        yield return new(AST, Macrocosmos, hit[2] - 1.5f, "Forsaken (3rd hit)", []);
        yield return new(SCH, Expedient, hit[2] - 1.5f, "Forsaken (3rd hit, through the 7th)", [.. hit[2..7]]);
        yield return new(SGE, Panhaima, hit[2] - 1.5f, "Forsaken (3rd hit)", [.. hit[2..7]]);

        var fifth = hit[4] - SecondRound;
        yield return new(ot, MitActionId.Reprisal, fifth, "Forsaken (5th hit, as the first round wears off)", FromThe(4));
        yield return new(ot, PartyMit, fifth, "Forsaken (5th hit)", [hit[4]], [Warrior, Paladin]);
        yield return new(ot, PartyMit, fifth, "Forsaken (5th hit, through the 8th)", FromThe(4), [Gunbreaker, DarkKnight]);
        yield return new(WHM, DivineCaress, fifth, "Forsaken (5th hit)", [hit[4]]);
        yield return new(AST, SunSign, fifth, "Forsaken (5th hit, through the 8th)", FromThe(4));
        yield return new(D1, Feint, fifth, "Forsaken (5th hit)", FromThe(4));
        yield return new(D3, PartyMit, fifth, "Forsaken (5th hit, through the 8th)", FromThe(4));
        yield return new(D4, Addle, fifth, "Forsaken (5th hit)", FromThe(4));

        yield return new(WHM, PlenaryIndulgence, hit[5] - 0.8f, "Forsaken Bonds (6th hit, through the 8th)", FromThe(5));

        yield return new(SCH, SummonSeraph, hit[6] - 1.6f, "Forsaken (7th hit)", [hit[6], hit[7]]);

        yield return new(SCH, Consolation, hit[7] - 1.6f, "Forsaken Bonds (8th hit)", [hit[7]]);
        yield return new(SCH, SacredSoil, hit[7] - 0.6f, "Forsaken Bonds (8th hit, Soil is back)", [hit[7]]);
        yield return new(SGE, Kerachole, hit[7] - 0.6f, "Forsaken Bonds (8th hit, Kerachole is back)", [hit[7]]);
    }
}

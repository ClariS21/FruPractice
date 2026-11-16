using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Scenarios.Fru.FruMitigation.P5;
using static AnoMech.Scenarios.Fru.FruMitigationRows;

namespace AnoMech.Scenarios.Fru.P5Full;

// Pandora's Box on the full run's clock, and the sheet's rows for it: a tank's limit break, and 15s
// mitigations pressed late enough to still be up for the next Fulgent Blade, which matters more.
internal static class FruP5FullMitigation
{
    public const float PandorasBoxAt = 112.7f;

    private const float OnTheB = 2.4f;

    public static MitPlanData Plan() => new(FruMitigation.Profile,
        [PandorasBox],
        [new(PandorasBoxAt, [PandorasBox.Key])],
        [
            new(MT, MitPlanEntry.TankLb3, PandorasBoxAt - Short, "Pandora's Box", [PandorasBoxAt]),
            new(OT, MitActionId.Reprisal, PandorasBoxAt - OnTheB, "Pandora's Box (up for Fulgent Blade)", [PandorasBoxAt]),
            new(SCH, Concitation, PandorasBoxAt - Short, "Pandora's Box", [PandorasBoxAt]),
            new(SCH, SacredSoil, PandorasBoxAt - OnTheB, "Pandora's Box (up for Fulgent Blade)", [PandorasBoxAt]),
            new(SGE, EukrasianPrognosisII, PandorasBoxAt - Short, "Pandora's Box", [PandorasBoxAt]),
            new(SGE, Kerachole, PandorasBoxAt - OnTheB, "Pandora's Box (up for Fulgent Blade)", [PandorasBoxAt]),
            new(D1, Feint, PandorasBoxAt - OnTheB, "Pandora's Box (up for Fulgent Blade)", [PandorasBoxAt]),
        ],
        FruMitigation.Pandora);
}

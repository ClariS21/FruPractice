using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Scenarios.Umad.UmadMitigation.P5;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad.P5Exaflares;

// The P5 sheet's Stray Entropy row on this scenario's clock: the OT column's Reprisal two GCDs after
// the Stray Apocalypse cast bar, so it is back for Forsaken. Helpers deal Stray Entropy, so the
// Reprisal only softens Kefka's autos here.
internal static class UmadP5ExaflaresMitigation
{
    public const float StrayEntropyAt = 25.10f;

    private const float TwoGcdsAfterStrayApocalypse = 12.5f;

    // `firstInvuln` is the seat the sheet's MT column stands for; the OT column is the other.
    public static MitPlanData Plan(PartyRole firstInvuln) => new(UmadMitigation.Profile,
        [StrayEntropy],
        [new(StrayEntropyAt, [StrayEntropy.Key])],
        [
            new(Column(firstInvuln == PartyRole.MainTank ? PartyRole.OffTank : PartyRole.MainTank), MitActionId.Reprisal,
                TwoGcdsAfterStrayApocalypse, "Stray Entropy (two GCDs after Stray Apocalypse, so it is back for Forsaken)", []),
        ],
        UmadMitigation.Kefka);
}

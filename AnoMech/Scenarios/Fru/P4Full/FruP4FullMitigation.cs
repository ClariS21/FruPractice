using AnoMech.Core.Game.PartyMit;
using static AnoMech.Scenarios.Fru.FruMitigation.P4;

namespace AnoMech.Scenarios.Fru.P4Full;

// The P4 hits that belong to neither drill, on the full run's clock: the Morn Afahs after each Akh
// Morn and the last Edge of Oblivion. The mitigation the sheet gives for them is the Akh Morn
// Afah rows, which the drills press early enough to still be up.
internal static class FruP4FullMitigation
{
    // The Morn Afah bar is 5.7 s and its damage lands at 7.5 s after it begins: 74.8 and 157.5 s on the full run's clock.
    public const float MornAfah1At = 74.8f + 7.5f;
    public const float EdgeOfOblivion4At = 161.32f;
    public const float MornAfah2At = 157.5f + 7.5f;

    public static MitPlanData Plan() => new(FruMitigation.Profile,
        [MornAfah, EdgeOfOblivion],
        [new(MornAfah1At, [MornAfah.Key]), new(EdgeOfOblivion4At, [EdgeOfOblivion.Key]), new(MornAfah2At, [MornAfah.Key])],
        []);
}

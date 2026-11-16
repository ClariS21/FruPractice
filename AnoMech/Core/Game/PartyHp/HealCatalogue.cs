using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.UserActions;

namespace AnoMech.Core.Game.PartyHp;

public enum HealShape
{
    // Everyone of the party near the caster.
    Party,
    // One member.
    Single,
    // A circle on the ground at the caster; whoever stands in it at a tick is healed.
    Ground,
}

// A heal's size as a share of the target's max HP: the median of public clears, with the p25..p75 spread
// a landing varies within. Estimated sizes (no clear reports them) are marked in the table.
public readonly record struct HealRange(float P25, float Median, float P75)
{
    public static HealRange Around(float median) => new(median * 0.88f, median, median * 1.12f);
}

// One heal action: what lands when it goes off (Direct), and what each regen tick lands after
// (Tick, every shared server tick for Seconds). StatusId is the regen status the party list shows, 0 when
// the action's own status already comes from JobActions.
public sealed record HealAction(
    uint Id, string Name, JobId Job, HealShape Shape, bool Gcd, HealRange? Direct = null, HealRange? Tick = null,
    float Seconds = 0f, ushort StatusId = 0, string StatusName = "", float Radius = 30f, bool UsesLily = false);

internal static class HealCatalogue
{
    // Reach of a party heal around the healer.
    public const float PartyRange = 30f;

    private static HealRange R(float p25, float median, float p75) => new(p25, median, p75);

    public static IReadOnlyList<HealAction> All { get; } =
    [
        // White Mage
        new(16534, "Afflatus Rapture", JobId.WhiteMage, HealShape.Party, true, R(0.144f, 0.160f, 0.222f), UsesLily: true),
        new(37010, "Medica III", JobId.WhiteMage, HealShape.Party, true, R(0.078f, 0.096f, 0.118f), R(0.058f, 0.070f, 0.086f), 15f, 3880, "Medica III"),
        new(3571, "Assize", JobId.WhiteMage, HealShape.Party, false, R(0.146f, 0.161f, 0.190f)),
        new(3569, "Asylum", JobId.WhiteMage, HealShape.Ground, false, Tick: R(0.036f, 0.044f, 0.050f), Seconds: 24f, StatusId: 1911, StatusName: "Asylum", Radius: 5f),
        new(37011, "Divine Caress", JobId.WhiteMage, HealShape.Party, false, Tick: R(0.073f, 0.087f, 0.104f), Seconds: 15f, StatusId: 3904, StatusName: "Divine Aura"),
        // UNVERIFIED sizes: no clear reports these.
        new(3570, "Tetragrammaton", JobId.WhiteMage, HealShape.Single, false, HealRange.Around(0.22f)),
        new(140, "Benediction", JobId.WhiteMage, HealShape.Single, false, new HealRange(1f, 1f, 1f)),

        // Scholar
        new(37013, "Concitation", JobId.Scholar, HealShape.Party, true, R(0.070f, 0.079f, 0.094f)),
        new(186, "Succor", JobId.Scholar, HealShape.Party, true, HealRange.Around(0.117f)),
        new(188, "Sacred Soil", JobId.Scholar, HealShape.Ground, false, Tick: R(0.036f, 0.040f, 0.046f), Seconds: 15f, Radius: 8f),
        new(3583, "Indomitability", JobId.Scholar, HealShape.Party, false, HealRange.Around(0.12f)),
        new(16537, "Whispering Dawn", JobId.Scholar, HealShape.Party, false, Tick: HealRange.Around(0.024f), Seconds: 21f, StatusId: 315, StatusName: "Whispering Dawn"),
        new(16543, "Fey Blessing", JobId.Scholar, HealShape.Party, false, HealRange.Around(0.12f)),
        new(37014, "Seraphism", JobId.Scholar, HealShape.Party, false, Tick: R(0.037f, 0.043f, 0.050f), Seconds: 20f, StatusId: 3885, StatusName: "Seraphism"),
        new(189, "Lustrate", JobId.Scholar, HealShape.Single, false, HealRange.Around(0.19f)),
        new(7434, "Excogitation", JobId.Scholar, HealShape.Single, false, HealRange.Around(0.25f)),

        // Astrologian
        new(37030, "Helios Conjunction", JobId.Astrologian, HealShape.Party, true, R(0.088f, 0.100f, 0.122f), R(0.065f, 0.076f, 0.092f), 15f, 3894, "Helios Conjunction"),
        new(3600, "Helios", JobId.Astrologian, HealShape.Party, true, R(0.146f, 0.166f, 0.195f)),
        new(16553, "Celestial Opposition", JobId.Astrologian, HealShape.Party, false, R(0.069f, 0.079f, 0.094f), R(0.036f, 0.042f, 0.049f), 15f, 1879, "Opposition"),
        new(16557, "Horoscope", JobId.Astrologian, HealShape.Party, false, R(0.137f, 0.156f, 0.187f)),
        new(3614, "Essential Dignity", JobId.Astrologian, HealShape.Single, false, R(0.216f, 0.255f, 0.360f)),
        new(16556, "Celestial Intersection", JobId.Astrologian, HealShape.Single, false, R(0.058f, 0.078f, 0.107f)),

        // Sage
        new(37034, "Eukrasian Prognosis II", JobId.Sage, HealShape.Party, true, R(0.036f, 0.042f, 0.057f)),
        new(24286, "Prognosis", JobId.Sage, HealShape.Party, true, R(0.106f, 0.117f, 0.140f)),
        new(24318, "Pneuma", JobId.Sage, HealShape.Party, true, R(0.229f, 0.254f, 0.360f)),
        new(24299, "Ixochole", JobId.Sage, HealShape.Party, false, R(0.146f, 0.160f, 0.189f)),
        new(24298, "Kerachole", JobId.Sage, HealShape.Party, false, Tick: R(0.036f, 0.041f, 0.048f), Seconds: 15f),
        new(24302, "Physis II", JobId.Sage, HealShape.Party, false, Tick: R(0.047f, 0.055f, 0.063f), Seconds: 15f, StatusId: 2620, StatusName: "Physis II"),
        new(24310, "Holos", JobId.Sage, HealShape.Party, false, R(0.107f, 0.119f, 0.134f)),
        // UNVERIFIED sizes.
        new(24296, "Druochole", JobId.Sage, HealShape.Single, false, HealRange.Around(0.19f)),
        new(24303, "Taurochole", JobId.Sage, HealShape.Single, false, HealRange.Around(0.22f)),
    ];

    private static readonly Dictionary<uint, HealAction> ById = All.ToDictionary(h => h.Id);

    public static HealAction? Find(uint actionId) => ById.GetValueOrDefault(actionId);

    public static IReadOnlyList<HealAction> For(JobId job) => All.Where(h => h.Job == job).ToList();
}

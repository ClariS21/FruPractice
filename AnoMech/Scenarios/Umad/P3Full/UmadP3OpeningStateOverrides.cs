using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Umad.P3Full;

public enum UmadP3Element { Fire, Water }

public sealed class UmadP3OpeningStateOverrides
{
    // --- Fight-wide: one roll the whole sim shares -------------------------------------
    // Which element goes off first; null = random.
    public UmadP3Element? ShortElement { get; set; }
    // The fire crystal's side, looking from the centre at the wind crystal; null = random.
    public bool? FireOnRight { get; set; }
    // ActionId.LongitudinalImplosion / LatitudinalImplosion; null = random.
    public uint? Implosion { get; set; }

    // --- Per player: everyone has their own ---------------------------------------------
    // Entropy (fire) or Dynamic Fluid (water). Each goes to one support and one DPS.
    public PerRoleSetting<UmadP3Element> Debuff { get; set; } = new();

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.ForceSeats) return conflicts;
        foreach (var element in new[] { UmadP3Element.Fire, UmadP3Element.Water })
        {
            var name = element == UmadP3Element.Fire ? "Entropy" : "Dynamic Fluid";
            conflicts.AtMost(1, PerRole.All.Where(r => !r.IsDps() && Debuff[r] == element).ToList(), $"to carry {name} as a support");
            conflicts.AtMost(1, PerRole.All.Where(r => r.IsDps() && Debuff[r] == element).ToList(), $"to carry {name} as a DPS");
        }
        return conflicts;
    }
}

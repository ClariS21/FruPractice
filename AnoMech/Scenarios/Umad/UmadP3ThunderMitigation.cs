using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Scenarios.Umad.P3BlackHole;
using static AnoMech.Scenarios.Umad.UmadMitigationRows;

namespace AnoMech.Scenarios.Umad;

// One Thunder III set on a plan's clock: its two hits and the seats that take them.
internal readonly record struct UmadP3ThunderSet(int Number, float First, float Second, ThunderIIIAssignment Plan);

// The Omnitank sheet's P3 tank cells for the Thunder IIIs, set by set. The tank a set's plan has
// invuln presses its invulnerability for both hits; each tank of a shared set presses the sheet's
// cooldowns for the hit it takes: Rampart, its 90 s and its short one, or, while those are on
// recast (the sheet's second share), its 40 % and its short one. A tank invulns once a phase:
// asked again, it takes both hits on what it has.
internal static class UmadP3ThunderMitigation
{
    // The longest any job's version takes to come back.
    private const float RampartRecast = 90f;
    private const float FortyRecast = 120f;
    private const float ShortRecast = 25f;

    public static IReadOnlyList<MitPlanEntry> Rows(IEnumerable<UmadP3ThunderSet> sets)
    {
        var rampart = new Dictionary<PartyRole, float>();
        var forty = new Dictionary<PartyRole, float>();
        var shortMit = new Dictionary<PartyRole, float>();
        var invulned = new HashSet<PartyRole>();
        var rows = new List<MitPlanEntry>();
        foreach (var set in sets.OrderBy(s => s.First))
        {
            var (first, second) = ThunderIIIPlanning.Roles(set.Plan);
            var label = $"Thunder III {set.Number}";
            if (second is { } partner)
            {
                rows.AddRange(Sink(first, set.First, $"{label} (first hit)", [set.First]));
                rows.AddRange(Sink(partner, set.Second, $"{label} (second hit)", [set.Second]));
            }
            else if (invulned.Add(first))
                rows.AddRange(PerJob(Column(first), Invulnerability, set.First, $"{label} (invulnerable for both hits)", [set.First, set.Second]));
            else
                rows.AddRange(Sink(first, set.First, $"{label} (both hits: its invulnerability is spent)", [set.First, set.Second]));
        }
        return rows;

        IEnumerable<MitPlanEntry> Sink(PartyRole seat, float hit, string label, float[] covers)
        {
            var tank = Column(seat);
            var pressed = hit - Long;
            var sink = new List<MitPlanEntry>();
            if (Ready(rampart, RampartRecast))
            {
                rampart[seat] = pressed;
                sink.Add(Rampart(tank, hit, label, covers));
                sink.AddRange(PerJob(tank, NinetySeconds, hit, label, covers));
            }
            else if (Ready(forty, FortyRecast))
            {
                forty[seat] = pressed;
                sink.AddRange(PerJob(tank, FortyPercent, hit, label, covers));
            }
            if (Ready(shortMit, ShortRecast))
            {
                shortMit[seat] = pressed;
                sink.AddRange(PerJob(tank, ShortMit, hit, label, covers));
            }
            return sink;

            bool Ready(Dictionary<PartyRole, float> last, float recast) => !last.TryGetValue(seat, out var at) || pressed - at >= recast;
        }
    }
}

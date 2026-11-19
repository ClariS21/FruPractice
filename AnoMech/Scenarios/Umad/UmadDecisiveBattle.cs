using System.Linq;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad;

// The Decisive Battle as it resolves: the four nearest Chaos fight Chaos, the rest Exdeath.
internal static class UmadDecisiveBattle
{
    public static void Resolve(SimParty party, SimEnemy? chaos, SimEnemy? exdeath)
    {
        if (chaos is null) return;
        chaos.AddStatus(StatusId.EpicVillain);
        exdeath?.AddStatus(StatusId.FatedVillain);
        var epic = party.Find.ClosestN(chaos.Position, 4);
        foreach (var member in party.ActiveMembers())
            member.AddStatus(epic.Contains(member) ? StatusId.EpicHero : StatusId.FatedHero);
    }
}

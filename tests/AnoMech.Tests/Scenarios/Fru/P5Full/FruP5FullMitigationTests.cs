using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru.P5Full;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Tests;

public class FruP5FullMitigationTests
{
    [Test]
    public void TheChainedPlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP5FullScenario.MitigationPlan(), "Full P5");
    }

    private static IEnumerable<TestCaseData> Pairs()
    {
        JobId[] tanks = [Warrior, Paladin, Gunbreaker, DarkKnight];
        foreach (var main in tanks)
            foreach (var off in tanks)
                yield return new TestCaseData(main, off).SetName($"{main} MT + {off} OT");
    }

    // Every Wings row a tank pair gets in each round's own plan is in the full phase's at its time:
    // the rows the sheet's other tank-pair tabs hold for the same buttons are never this pair's.
    [TestCaseSource(nameof(Pairs))]
    public void TheChainedPlanKeepsEachTankPairsWingsRows(JobId main, JobId off)
    {
        var jobs = new Dictionary<PartyRole, JobId>(MitigationPlanAssert.Comps().First().Jobs) { [PartyRole.MainTank] = main, [PartyRole.OffTank] = off };
        var chained = MitPlan.Resolve(FruP5FullScenario.MitigationPlan().Entries, jobs);
        for (var round = 0; round < FruP5FullScenario.ParadiseRegainedAt.Length; round++)
        {
            var at = FruP5FullScenario.ParadiseRegainedAt[round];
            var wings = MitPlan.Resolve(FruP5ParadiseRegainedMitigation.Plan(round).Entries, jobs)
                .Where(p => p.Slot is PartyRole.MainTank or PartyRole.OffTank && p.Label.StartsWith("Wings Dark and Light")).ToList();
            Assert.That(wings, Is.Not.Empty);
            foreach (var row in wings)
                Assert.That(chained.Any(p => p.Slot == row.Slot && p.Action.ActionId == row.Action.ActionId && MathF.Abs(p.At - (at + row.At)) < 0.01f),
                    $"round {round + 1}: {row.Slot}'s {row.Action.Name} at {at + row.At:F2}");
        }
    }
}

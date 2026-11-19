using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P3Full;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Tests;

public class FruP3FullMitigationTests
{
    [Test]
    public void TheChainedPlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP3FullScenario.MitigationPlan(), "Full P3");
    }

    // Each job's kitchen sink for Black Halo, Rampart included: another job's Rampart on the same column
    // is never this tank's, so it must not hold this one's recast.
    [TestCase(Warrior)]
    [TestCase(Paladin)]
    [TestCase(Gunbreaker)]
    [TestCase(DarkKnight)]
    public void TheChainedPlanKeepsBlackHalosKitchenSinkForEveryMainTankJob(JobId job)
    {
        var jobs = new Dictionary<PartyRole, JobId>(MitigationPlanAssert.Comps().First().Jobs) { [PartyRole.MainTank] = job };
        var plan = MitPlan.Resolve(FruP3FullScenario.MitigationPlan().Entries, jobs);
        var sink = FruMitigationRows.KitchenSink(SheetColumn.MT, job, FruP3FullMitigation.BlackHaloAt, "Black Halo").Select(r => r.ActionId).ToList();
        Assert.That(sink, Does.Contain(MitActionId.Rampart));
        foreach (var button in sink)
            Assert.That(plan.Any(p => p.Slot == PartyRole.MainTank && p.Action.ActionId == button && p.Covers.Contains(FruP3FullMitigation.BlackHaloAt)),
                $"{job}'s {MitCatalogue.Find(button)!.Name} for Black Halo");
    }
}

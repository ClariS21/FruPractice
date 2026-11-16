using AnoMech.Scenarios.Fru.P4DarklitDragonsong;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;

namespace AnoMech.Tests;

public class FruP4DarklitDragonsongMitigationTests
{
    [TestCase(JobId.Warrior, JobId.Paladin, JobId.Warrior, "WARPLD")]
    [TestCase(JobId.Gunbreaker, JobId.Paladin, JobId.Gunbreaker, "GNBPLD")]
    [TestCase(JobId.Gunbreaker, JobId.DarkKnight, JobId.DarkKnight, "GNBDRK")]
    [TestCase(JobId.Paladin, JobId.DarkKnight, JobId.DarkKnight, "PLDDRK")]
    [TestCase(JobId.Warrior, JobId.Gunbreaker, (JobId)0, "WARGNB")]
    [TestCase(JobId.Warrior, JobId.DarkKnight, (JobId)0, "WARDRK")]
    public void SomberBaiterFollowsTheTankPairInEitherSeatOrder(JobId first, JobId second, JobId preferred, string tabName)
    {
        foreach (var (main, off) in new[] { (first, second), (second, first) })
        {
            var expected = preferred == 0 || main == preferred ? PartyRole.MainTank : PartyRole.OffTank;
            Assert.That(FruP4TankPair.SomberBaiter(main, off), Is.EqualTo(expected), $"{main} MT + {off} OT");
            var tab = FruP4TankPair.For(main, off)!;
            Assert.That(tab.Name, Is.EqualTo(tabName));
            Assert.That(tab.ColumnFor(PartyRole.MainTank, main), Is.EqualTo(main == tab.First ? 3 : 10));
            Assert.That(tab.ColumnFor(PartyRole.OffTank, main), Is.EqualTo(main == tab.First ? 6 : 13));
        }
    }

    [Test]
    public void GunbreakerMainTankAndPaladinOffTankUseThePaladinOffTankCooldownRow()
    {
        var jobs = new Dictionary<PartyRole, JobId>
        {
            [PartyRole.MainTank] = JobId.Gunbreaker,
            [PartyRole.OffTank] = JobId.Paladin,
        };
        var plan = MitPlan.Resolve(FruP4CrystallizeTimeMitigation.Plan().Entries, jobs);
        var wings = plan.Where(p => p.Label == "Hallowed Wings").ToList();
        Assert.That(wings.Where(p => p.Slot == PartyRole.MainTank).Select(p => p.Action.Name), Does.Contain("Great Nebula"));
        Assert.That(wings.Where(p => p.Slot == PartyRole.OffTank).Select(p => p.Action.Name), Does.Contain("Guardian"));
        Assert.That(wings.Where(p => p.Slot == PartyRole.OffTank).Select(p => p.Action.Name), Does.Not.Contain("Great Nebula"));
    }

    [Test]
    public void ThePlanHoldsForEveryComp()
    {
        MitigationPlanAssert.IsSound(FruP4DarklitDragonsongMitigation.Plan(), "Darklit Dragonsong");
    }
}

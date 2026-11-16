using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Tests;

public class FruP5WingsTankPairTests
{
    // The sheet's Wings rows per pair: the job that invulns the first wing, then the second.
    [TestCase(Warrior, Paladin, Warrior, Paladin)]
    [TestCase(Warrior, Gunbreaker, Warrior, Gunbreaker)]
    [TestCase(Paladin, Gunbreaker, Paladin, Gunbreaker)]
    [TestCase(Gunbreaker, DarkKnight, Gunbreaker, DarkKnight)]
    [TestCase(Warrior, DarkKnight, Warrior, DarkKnight)]
    [TestCase(Paladin, DarkKnight, Paladin, DarkKnight)]
    public void WingsRowsFollowTheTankPairInEitherSeatOrder(JobId a, JobId b, JobId firstInvuln, JobId secondInvuln)
    {
        foreach (var (main, off) in new[] { (a, b), (b, a) })
        {
            var jobs = new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = main, [PartyRole.OffTank] = off };
            var plan = MitPlan.Resolve(FruP5ParadiseRegainedMitigation.Plan().Entries, jobs);
            foreach (var (seat, job) in new[] { (PartyRole.MainTank, main), (PartyRole.OffTank, off) })
            {
                var mine = plan.Where(p => p.Slot == seat && p.Label.StartsWith("Wings")).ToList();
                var invulnWing = job == firstInvuln ? 1 : 2;
                var sinkWing = 3 - invulnWing;
                var invuln = job switch { Warrior => "Holmgang", Paladin => "Hallowed Ground", Gunbreaker => "Superbolide", _ => "Living Dead" };
                var ctx = $"{main} MT + {off} OT, {job}";
                Assert.That(mine.Where(p => p.Label.EndsWith(invulnWing.ToString())).Select(p => p.Action.Name), Does.Contain(invuln), ctx);
                Assert.That(mine.Where(p => p.Label.EndsWith(sinkWing.ToString())).Select(p => p.Action.Name), Does.Contain("Rampart").And.Not.Contain(invuln), ctx);
                Assert.That(mine.Count(p => p.Action.Name == invuln), Is.EqualTo(1), ctx);
                Assert.That(mine.Count(p => p.Action.Name == "Rampart"), Is.EqualTo(1), ctx);
            }
            Assert.That(FruP5WingsTankPair.FirstCleaveSeat(main, off), Is.EqualTo(main == firstInvuln ? PartyRole.MainTank : PartyRole.OffTank));
        }
    }

    [TestCase(Warrior, Paladin)]
    [TestCase(Gunbreaker, Paladin)]
    public void TheDefaultAndTheGnbMainTankCompsAreSound(JobId main, JobId off)
    {
        Assert.That(FruP5ParadiseRegainedMitigation.Plan().Entries, Is.Not.Empty);
        var jobs = new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = main, [PartyRole.OffTank] = off };
        Assert.That(MitPlan.Validate(MitPlan.Resolve(FruP5ParadiseRegainedMitigation.Plan().Entries, jobs)), Is.Empty);
    }

    private static IEnumerable<TestCaseData> Comps()
    {
        JobId[] tanks = [Warrior, Paladin, Gunbreaker, DarkKnight];
        foreach (var main in tanks)
            foreach (var off in tanks)
                if (main != off) yield return new TestCaseData(main, off).SetName($"{main} MT + {off} OT");
    }

    [TestCaseSource(nameof(Comps))]
    public void ParadiseRegainedSurvivesEveryTankPairInEitherSeatOrder(JobId main, JobId off)
    {
        var jobs = new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = main, [PartyRole.OffTank] = off };
        var type = typeof(FruP5ParadiseRegainedScenario);
        for (var seed = 0; seed < 8; seed++)
            {
                var run = ScenarioRun.Execute(type, 0, seed, new ScenarioRunOptions { PlayerRole = PartyRole.CasterDps, JobOverrides = jobs, Configure = c => c.MitigationPractice = true });
                Assert.That(run.WouldHaveKilled, Is.Empty, $"{type.Name} seed {seed}");
                Assert.That(run.Passed, run.ToString);
            }
    }
}

using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru.P3Full;
using AnoMech.Scenarios.Fru.P4Full;
using AnoMech.Scenarios.Fru.P5Full;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Tests;

public class FruFullTankPairSweepTests
{
    private static IEnumerable<TestCaseData> Comps()
    {
        JobId[] tanks = [Warrior, Paladin, Gunbreaker, DarkKnight];
        foreach (var main in tanks)
            foreach (var off in tanks)
                if (main != off) yield return new TestCaseData(main, off).SetName($"{main} MT + {off} OT");
    }

    private static void Sweep(Type type, JobId main, JobId off, int seeds)
    {
        var jobs = new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = main, [PartyRole.OffTank] = off };
        for (var seed = 0; seed < seeds; seed++)
        {
            var run = ScenarioRun.Execute(type, 0, seed, new ScenarioRunOptions { PlayerRole = PartyRole.CasterDps, JobOverrides = jobs, UserActionsEnabled = true, Configure = c => c.MitigationPractice = true });
            Assert.That(run.WouldHaveKilled, Is.Empty, $"{type.Name} seed {seed}");
            Assert.That(run.Passed, $"{type.Name} seed {seed}: {run}");
        }
    }

    [Test]
    public void P5FullSurvivesTheDefaultComp()
    {
        var run = ScenarioRun.Execute(typeof(FruP5FullScenario), 0, 0, new ScenarioRunOptions { PlayerRole = PartyRole.CasterDps, UserActionsEnabled = true, Configure = c => c.MitigationPractice = true });
        Assert.That(run.WouldHaveKilled, Is.Empty);
        Assert.That(run.Passed, run.ToString);
    }

    // The scripted tank LB3 for Pandora's Box must fire whatever the mitigation settings are.
    [Test]
    public void P5FullSurvivesWithUserActionsDisabled()
    {
        for (var seed = 0; seed < 2; seed++)
        {
            var run = ScenarioRun.Execute(typeof(FruP5FullScenario), 0, seed, new ScenarioRunOptions { PlayerRole = PartyRole.CasterDps, UserActionsEnabled = false, Configure = c => c.MitigationPractice = true });
            Assert.That(run.WouldHaveKilled, Is.Empty, $"seed {seed}");
            Assert.That(run.Passed, $"seed {seed}: {run}");
        }
    }

    [Test]
    public void P5FullSurvivesWithTankMitigationDisabled()
    {
        for (var seed = 0; seed < 2; seed++)
        {
            var run = ScenarioRun.Execute(typeof(FruP5FullScenario), 0, seed, new ScenarioRunOptions { PlayerRole = PartyRole.CasterDps, UserActionsEnabled = true, Configure = c => { c.MitigationPractice = true; c.EnableTankMitigation = false; } });
            Assert.That(run.WouldHaveKilled, Is.Empty, $"seed {seed}");
            Assert.That(run.Passed, $"seed {seed}: {run}");
        }
    }

    [TestCaseSource(nameof(Comps))]
    public void P5FullSurvivesEveryTankPair(JobId main, JobId off) => Sweep(typeof(FruP5FullScenario), main, off, 4);

    [TestCaseSource(nameof(Comps))]
    public void P3FullSurvivesEveryTankPair(JobId main, JobId off) => Sweep(typeof(FruP3FullScenario), main, off, 2);

    [TestCaseSource(nameof(Comps))]
    public void P4FullSurvivesEveryTankPair(JobId main, JobId off) => Sweep(typeof(FruP4FullScenario), main, off, 2);
}

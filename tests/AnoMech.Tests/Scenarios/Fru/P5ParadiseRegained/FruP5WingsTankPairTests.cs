using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Tests;

public class FruP5WingsTankPairTests
{
    private const float Cleave1 = FruP5ParadiseRegainedMitigation.Cleave1;
    private const float Buster1 = FruP5ParadiseRegainedMitigation.Buster1;
    private const float Cleave2 = FruP5ParadiseRegainedMitigation.Cleave2;
    private const float Buster2 = FruP5ParadiseRegainedMitigation.Buster2;

    private static string InvulnOf(JobId job) => job switch { Warrior => "Holmgang", Paladin => "Hallowed Ground", Gunbreaker => "Superbolide", _ => "Living Dead" };
    private static string BuddyOf(JobId job) => job switch { Warrior => "Nascent Flash", Paladin => "Intervention", Gunbreaker => "Heart of Corundum", _ => "The Blackest Night" };

    // The sheet's two Wings rows are the two casts: per cast one tank invulns, the other sinks. The first of
    // Warrior, Paladin, Gunbreaker, Dark Knight invulns the first (a same-job pair: the main tank), the other the second.
    [TestCase(Warrior, Paladin, Warrior)]
    [TestCase(Warrior, Gunbreaker, Warrior)]
    [TestCase(Paladin, Gunbreaker, Paladin)]
    [TestCase(Gunbreaker, DarkKnight, Gunbreaker)]
    [TestCase(Warrior, DarkKnight, Warrior)]
    [TestCase(Paladin, DarkKnight, Paladin)]
    [TestCase(Paladin, Paladin, Paladin)]
    public void WingsRowsFollowTheCastAndTheTankPairInEitherSeatOrder(JobId a, JobId b, JobId firstInvuln)
    {
        foreach (var (main, off) in new[] { (a, b), (b, a) })
            foreach (var round in new[] { 0, 1 })
            {
                var jobs = new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = main, [PartyRole.OffTank] = off };
                var plan = MitPlan.Resolve(FruP5ParadiseRegainedMitigation.Plan(round).Entries, jobs);
                foreach (var (seat, job, partner) in new[] { (PartyRole.MainTank, main, off), (PartyRole.OffTank, off, main) })
                {
                    var mine = plan.Where(p => p.Slot == seat && p.Label.StartsWith("Wings")).ToList();
                    var ctx = $"{main} MT + {off} OT, round {round}, {seat} {job}";
                    var firstOfPair = a == b ? seat == PartyRole.MainTank : job == firstInvuln;
                    var invulns = round == 0 ? firstOfPair : !firstOfPair;
                    var wings = $"Wings Dark and Light {round + 1}";
                    Assert.That(mine.All(p => p.Label == wings), ctx);
                    var names = mine.Select(p => p.Action.Name).ToList();
                    var hits = seat == PartyRole.MainTank ? new[] { Cleave1, Buster2 } : new[] { Buster1, Cleave2 };
                    if (invulns)
                    {
                        var invuln = mine.Single(p => p.Action.Name == InvulnOf(job));
                        Assert.That(invuln.Covers, Is.EqualTo(hits), ctx);
                        Assert.That(names, Does.Not.Contain("Rampart"), ctx);
                    }
                    else
                    {
                        Assert.That(names, Does.Not.Contain(InvulnOf(job)).And.Contain("Rampart"), ctx);
                        Assert.That(mine.Single(p => p.Action.Name == "Rampart").Covers.Concat(mine.Single(p => p.Action.Name == "Rampart").Soft), Is.EquivalentTo(hits), ctx);
                    }
                }
            }
    }

    [Test]
    public void TheInvulnerableTankBuddiesTheSinksSecondHit()
    {
        foreach (var (main, off) in new[] { (Gunbreaker, Paladin), (Warrior, Paladin) })
        {
            var jobs = new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = main, [PartyRole.OffTank] = off };
            var plan = MitPlan.Resolve(FruP5ParadiseRegainedMitigation.Plan(0).Entries, jobs).Where(p => p.Label.StartsWith("Wings")).ToList();
            // Round 0: Warrior/Paladin invulns before Gunbreaker, so the Paladin (OT) invulns when GNB is MT.
            var mtInvulns = main == Warrior;
            var (inv, sink) = mtInvulns ? (PartyRole.MainTank, PartyRole.OffTank) : (PartyRole.OffTank, PartyRole.MainTank);
            var invJob = jobs[inv];
            var buddy = plan.Single(p => p.Slot == inv && p.Action.Name == BuddyOf(invJob));
            Assert.That(buddy.Covers.Concat(buddy.Soft).Single(), Is.EqualTo(sink == PartyRole.MainTank ? Buster2 : Cleave2));
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
                var run = ScenarioRun.Execute(type, 0, seed, new ScenarioRunOptions { PlayerRole = PartyRole.CasterDps, JobOverrides = jobs, UserActionsEnabled = true, Configure = c => c.MitigationPractice = true });
                Assert.That(run.WouldHaveKilled, Is.Empty, $"{type.Name} seed {seed}");
                Assert.That(run.Passed, run.ToString);
            }
    }

    [Test]
    public void TheOffTankBotKeepsItsOwnLaneWhenThePlayerIsTheMainTank(
        [Values(0, 1, 2, 3)] int seed, [Values(true, false)] bool darkFirst)
    {
        var jobs = new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = Gunbreaker, [PartyRole.OffTank] = Paladin };
        var otSpot = darkFirst ? new Vector3(0f, 0f, 1f) : new Vector3(10f, 0f, 10f);
        var mtSpot = darkFirst ? new Vector3(-6.0622f, 0f, -3.5f) : new Vector3(6.0622f, 0f, -3.5f);
        Vector3? seen = null;
        var run = ScenarioRun.Execute(typeof(FruP5ParadiseRegainedScenario), 0, seed, new ScenarioRunOptions
        {
            StopAt = 16f,
            PlayerRole = PartyRole.MainTank,
            JobOverrides = jobs,
            Overrides = o => { var s = (FruP5ParadiseRegainedStateOverrides)o; s.Rotation = 0; s.DarkFirst = darkFirst; },
            Probe = probe => { if (probe.Crossed(15.69f)) seen = probe.Member(PartyRole.OffTank)?.Position; },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(seen, Is.Not.Null);
        Assert.That(Vector3.Distance(seen!.Value, otSpot), Is.LessThan(1.5f), $"at {seen}");
        Assert.That(Vector3.Distance(seen.Value, mtSpot), Is.GreaterThan(3f), $"at {seen}");
    }
}

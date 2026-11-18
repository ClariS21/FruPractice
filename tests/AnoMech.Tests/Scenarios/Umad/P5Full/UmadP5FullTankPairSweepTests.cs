using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P5Full;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Tests;

// The Omnitank sheet's P5 for every ordered tank pair: the first of Warrior, Dark Knight, Gunbreaker,
// Paladin takes the first Surprise Holy and the other the second; the Holy tank provokes as it lands
// and is invulnerable for Chaotic Holy and the two tank stacks after it; the third stack after the
// second Orchestra is shared.
public class UmadP5FullTankPairSweepTests
{
    private static readonly PartyRole[] Tanks = [MainTank, OffTank];

    private static IEnumerable<TestCaseData> Comps()
    {
        JobId[] tanks = [Warrior, Paladin, Gunbreaker, DarkKnight];
        foreach (var main in tanks)
            foreach (var off in tanks)
                if (main != off) yield return new TestCaseData(main, off).SetName($"{main} MT + {off} OT");
    }

    private static ushort Invulnerability(JobId job) => job switch
    {
        Warrior => 409,
        Paladin => 82,
        Gunbreaker => 1836,
        _ => 810,
    };

    [TestCaseSource(nameof(Comps))]
    public void TheTanksFollowTheOmnitankSheet(JobId main, JobId off)
    {
        var jobs = new Dictionary<PartyRole, JobId> { [MainTank] = main, [OffTank] = off };
        for (var seed = 0; seed < 2; seed++)
        {
            var holy = new PartyRole?[2];
            var invulnerable = new bool[2];
            var soloStackOn = new List<PartyRole>[] { [], [] };
            var thirdShared = false;
            var run = ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, seed, new ScenarioRunOptions
            {
                PlayerRole = CasterDps,
                JobOverrides = jobs,
                Probe = p =>
                {
                    for (var round = 0; round < 2; round++)
                    {
                        var at = UmadP5FullScenario.OrchestraAt[round];
                        if (p.Crossed(at + UmadP5Orchestra.SurpriseAt + 0.1f))
                            holy[round] = Tanks.Cast<PartyRole?>().FirstOrDefault(t => p.Member(t!.Value)!.HasStatus(UmadConstants.StatusId.SurpriseHoly));
                        if (p.Crossed(at + UmadP5Orchestra.FinaleAt + 0.02f) && holy[round] is { } tank)
                            invulnerable[round] = p.Member(tank)!.HasStatus(Invulnerability(jobs[tank]));
                        foreach (var (_, stacks) in UmadP5FullScenario.FellForcesAt.Where(f => f.Stacks > at + UmadP5Orchestra.FinaleAt).Take(2))
                            if (p.Crossed(stacks + 0.02f)) soloStackOn[round].Add(Enmity(p).Holder);
                    }
                    if (p.Crossed(UmadP5FullScenario.FellForcesAt[^1].Stacks - 0.02f))
                    {
                        var holder = p.Member(Enmity(p).Holder)!.Position;
                        thirdShared = Tanks.All(t => Vector3.Distance(p.Member(t)!.Position, holder) < 3f);
                    }
                },
            });
            Assert.That(run.Passed, $"seed {seed}: {run}");
            for (var round = 0; round < 2; round++)
            {
                var expected = UmadP5TankPair.HolyTank(main, off, round);
                Assert.That(holy[round], Is.EqualTo(expected), $"seed {seed}: Orchestra {round + 1}'s Surprise Holy");
                Assert.That(invulnerable[round], $"seed {seed}: Orchestra {round + 1}'s Holy tank invulnerable for Chaotic Holy");
                Assert.That(soloStackOn[round], Is.EqualTo(new[] { expected, expected }), $"seed {seed}: the tank stacks after Orchestra {round + 1}");
            }
            Assert.That(thirdShared, $"seed {seed}: both tanks in the third tank stack after the second Orchestra");
        }
    }

    // The player's own Provoke takes Kefka: the Warrior main tank pulls him back from the Paladin, who
    // took him at the start, so the first Surprise Flare is the Warrior's.
    [Test]
    public void ThePlayersProvokeTakesKefka()
    {
        PartyRole? flare = null;
        var run = ScenarioRun.Execute(typeof(UmadP5FullScenario), 0, 4, new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 40f,
            Probe = p =>
            {
                if (p.Crossed(5f))
                    ((FakeUserActions)AnoMech.Core.Native.Interfaces.Natives.UserActions).Act(
                        new AnoMech.Core.Native.Interfaces.PlayerAction(AnoMech.Core.Native.Interfaces.PlayerActionStep.Resolved,
                            FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action, UmadP5Enmity.Provoke, 0));
                if (p.Crossed(39.4f))
                    flare = Tanks.Cast<PartyRole?>().FirstOrDefault(t => p.Member(t!.Value)!.HasStatus(UmadConstants.StatusId.SurpriseFlare));
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(flare, Is.EqualTo(MainTank));
    }

    private static UmadP5Enmity Enmity(ScenarioProbe p)
        => p.Game.Catalog.Scenarios.OfType<UmadP5FullScenario>().Single().Enmity!;
}

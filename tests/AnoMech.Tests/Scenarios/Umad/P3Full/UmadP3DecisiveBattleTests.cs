using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P3LimitCut;
using static AnoMech.Core.Game.Party.PartyRole;
using BNpcBaseId = AnoMech.Scenarios.Umad.UmadConstants.BNpcBaseId;
using StatusId = AnoMech.Scenarios.Umad.UmadConstants.StatusId;

namespace AnoMech.Tests;

// The second Decisive Battle (the Limit Cut's 46.49, 117.6 of the phase) hands out the heroes as the first
// does: the four nearest Chaos as it resolves fight Chaos, the rest Exdeath. The group stands as at the
// first, by who holds which boss: the Chaos tank, H1 and the melee by Chaos; the Exdeath tank, H2 and the
// ranged by Exdeath.
public class UmadP3DecisiveBattleTests
{
    private const float LimitCutAt = 71.1f;
    private const float ResolvesAt = 46.49f;

    private static IEnumerable<TestCaseData> TankPairs()
    {
        JobId[] tanks = [JobId.Warrior, JobId.DarkKnight, JobId.Gunbreaker, JobId.Paladin];
        foreach (var main in tanks)
            foreach (var off in tanks)
                yield return new TestCaseData(main, off).SetName($"{main} MT + {off} OT");
    }

    // A party never seats one job twice, so a same-job pair plays with the second tank on another job:
    // the bosses go by the pair seated.
    [TestCaseSource(nameof(TankPairs))]
    public void TheSecondBattleSplitsTheGroupByWhoHoldsWhichBoss(JobId main, JobId off)
    {
        PartyRole[] epic = [], fated = [];
        var tanks = UmadP3Tanks.MainOnChaos;
        var run = ScenarioRun.Execute(typeof(UmadP3FullScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = LimitCutAt + ResolvesAt + 0.6f,
            PlayerRole = CasterDps,
            JobOverrides = new Dictionary<PartyRole, JobId> { [MainTank] = main, [OffTank] = off },
            Probe = p =>
            {
                if (!p.Crossed(LimitCutAt + ResolvesAt + 0.3f)) return;
                tanks = UmadP3Tanks.For(p.World.Party);
                epic = [.. Holding(p, StatusId.EpicHero)];
                fated = [.. Holding(p, StatusId.FatedHero)];
            },
        });
        Assert.That(run.Passed, run.ToString);
        if (main != off) Assert.That(tanks, Is.EqualTo(UmadP3Tanks.For(main, off)));
        Assert.That(epic, Is.EquivalentTo(new[] { tanks.Chaos, RegenHealer, MeleeDpsA, MeleeDpsB }), "Epic Hero");
        Assert.That(fated, Is.EquivalentTo(new[] { tanks.Exdeath, ShieldHealer, PhysRangedDps, CasterDps }), "Fated Hero");
    }

    // Alone, the drill's own tanks hold the bosses: the main tank Chaos.
    [Test]
    public void TheDrillSplitsTheGroupTheSameWay()
    {
        PartyRole[] epic = [];
        var run = ScenarioRun.Execute(typeof(UmadP3LimitCutScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = ResolvesAt + 0.6f,
            Overrides = o => UmadP3LimitCutScenarioTests.Pin((UmadP3LimitCutStateOverrides)o),
            Probe = p => { if (p.Crossed(ResolvesAt + 0.3f)) epic = [.. Holding(p, StatusId.EpicHero)]; },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(epic, Is.EquivalentTo(new[] { MainTank, RegenHealer, MeleeDpsA, MeleeDpsB }));
    }

    // R steps onto Chaos instead of waiting by Exdeath: R fights Chaos, and whichever of the Chaos group
    // stood farthest from it fights Exdeath.
    [TestCase(false)]
    [TestCase(true)]
    public void StandingByTheWrongBossJoinsItsSide(bool full)
    {
        var offset = full ? LimitCutAt : 0f;
        PartyRole[] epic = [], nearest = [];
        ScenarioRun.Execute(full ? typeof(UmadP3FullScenario) : typeof(UmadP3LimitCutScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = PhysRangedDps,
            StopAt = offset + ResolvesAt + 0.6f,
            Overrides = o =>
            {
                if (o is UmadP3FullStateOverrides fullRolls) UmadP3FullScenarioTests.Pin(fullRolls);
                else UmadP3LimitCutScenarioTests.Pin((UmadP3LimitCutStateOverrides)o);
            },
            Takeovers = [new Takeover(offset + 45f, null)],
            Probe = p =>
            {
                if (p.Crossed(offset + 45.1f)) p.Member(PhysRangedDps)!.SetPosition(Chaos(p) + new Vector3(0.5f, 0f, 0f));
                if (p.Crossed(offset + ResolvesAt))
                    nearest = [.. PerRole.All.OrderBy(r => Vector3.DistanceSquared(p.Member(r)!.Position, Chaos(p))).Take(4)];
                if (p.Crossed(offset + ResolvesAt + 0.3f)) epic = [.. Holding(p, StatusId.EpicHero)];
            },
        });
        Assert.That(epic, Does.Contain(PhysRangedDps));
        Assert.That(epic, Is.EquivalentTo(nearest));
    }

    private static Vector3 Chaos(ScenarioProbe p) => p.World.Children.OfType<SimEnemy>().Single(e => e.BNpcBaseId == BNpcBaseId.ChaosP3).Position;

    private static IEnumerable<PartyRole> Holding(ScenarioProbe p, ushort statusId)
        => PerRole.All.Where(r => p.Member(r) is { } m && m.IsAlive() && m.HasStatus(statusId));
}

using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Tests;

public class AiManagerTimingTests
{
    private SimWorld world = null!;

    [SetUp]
    public void SetUp()
    {
        var fake = FakeGame.Install();
        fake.BattleCharas.Player.ClassJob = (byte)AnoMech.Core.UserActions.JobId.Sage;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.Reseed(12345);
        world.SeatParty(new LocalPartyProvider().Roster((uint)AnoMech.Core.UserActions.JobId.Sage));
        world.Party.Get(PartyRole.MainTank)!.SetPosition(Vector3.Zero);
    }

    [TearDown]
    public void TearDown()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private void Move(bool hold = false, float settleFraction = 0f)
        => new AiManager(world).Move(0f, () => AiMove.Single(PartyRole.MainTank, new Vector2(6.5f, 0f)),
            jitter: 0f, arrivalTime: 8f, settleFraction: settleFraction, hold: hold);

    private float DepartureTime()
    {
        var member = world.Party.Get(PartyRole.MainTank)!;
        for (var i = 0; i < 480; i++)
        {
            world.Events.Tick(1f / 60f);
            if (member.MoveDestination is not null) return world.Events.Elapsed;
        }
        Assert.Fail("The move was never ordered.");
        return float.NaN;
    }

    [Test]
    public void HumanLikeDepartureKeepsDeadlineSafe()
    {
        world.BotTiming = BotTiming.HumanLike;
        Move();

        Assert.That(DepartureTime(), Is.InRange(0f, 6.75f + 1f / 60f));
    }

    [Test]
    public void HoldAndLastMomentLeaveAtLatestSafeTime()
    {
        world.BotTiming = BotTiming.HumanLike;
        Move(hold: true);
        Assert.That(DepartureTime(), Is.EqualTo(6.75f).Within(0.02f));

        world.Events.Clear();
        world.Party.Get(PartyRole.MainTank)!.StopMoving();
        world.BotTiming = BotTiming.LastMoment;
        Move();
        Assert.That(DepartureTime(), Is.EqualTo(6.75f).Within(0.02f));
    }

    [Test]
    public void ReactionDrawsDoNotAdvanceMechanicRandomness()
    {
        world.BotTiming = BotTiming.HumanLike;
        var expected = new AnoMech.Scenarios.Rng(12345).Next(int.MaxValue);
        Move();
        DepartureTime();

        Assert.That(world.Rng.Next(int.MaxValue), Is.EqualTo(expected));
    }

    [Test]
    public void LastMomentKeepsSettleFractionAndHoldOverridesIt()
    {
        world.BotTiming = BotTiming.LastMoment;
        Move(settleFraction: 0.5f);
        Assert.That(DepartureTime(), Is.EqualTo(3.375f + 1f / 60f).Within(0.02f));

        world.Events.Clear();
        world.Party.Get(PartyRole.MainTank)!.StopMoving();
        Move(hold: true, settleFraction: 0.5f);
        Assert.That(DepartureTime(), Is.EqualTo(6.75f).Within(0.02f));
    }

    [Test]
    public void ProfilesAreStablePerSeatAndDoNotAdvanceMechanicRandomness()
    {
        var expected = new AnoMech.Scenarios.Rng(12345).Next(int.MaxValue);
        var first = world.BotProfile(PartyRole.OffTank);
        Assert.That(world.BotProfile(PartyRole.OffTank), Is.SameAs(first));
        var other = world.BotProfile(PartyRole.RegenHealer);
        world.Reseed(12345);
        Assert.Multiple(() =>
        {
            Assert.That(world.BotProfile(PartyRole.OffTank), Is.EqualTo(first));
            Assert.That(world.BotProfile(PartyRole.RegenHealer), Is.EqualTo(other));
            Assert.That(world.Rng.Next(int.MaxValue), Is.EqualTo(expected));
        });
    }

    [Test]
    public void ProfileRollsHaveTheIntendedRangesAndMix()
    {
        var counts = new int[2];
        for (var seed = 0; seed < 1000; seed++)
        {
            var profile = BotMovementProfile.Roll(new AnoMech.Scenarios.Rng(seed));
            Assert.That(profile.Lag, Is.InRange(0.04f, 0.2f));
            Assert.That(profile.ReactionMultiplier, Is.InRange(0.8f, 1.5f));
            counts[(int)profile.Style]++;
        }
        Assert.That(counts[0], Is.InRange(400, 600));
        Assert.That(counts[1], Is.InRange(400, 600));
    }

    [Test]
    public void ProfileArrivalsStayAheadOfTheDeadline()
    {
        var early = new BotMovementProfile(0.1f, 1f, BotArrivalStyle.Early);
        var onTime = new BotMovementProfile(0.1f, 1f, BotArrivalStyle.OnTime);
        var rng = new AnoMech.Scenarios.Rng(17);
        for (var i = 0; i < 100; i++)
        {
            var earlyPlan = AiManager.PlanHumanLike(early, 6.5f, 8f, 0.5f, rng);
            var onTimePlan = AiManager.PlanHumanLike(onTime, 6.5f, 8f, 0.5f, rng);
            Assert.That(8f - earlyPlan.Delay - 6.5f / (earlyPlan.Sprint ? AiManager.SprintSpeed : 6.5f), Is.GreaterThanOrEqualTo(AiManager.StaggeredArrivalMargin));
            Assert.That(8f - onTimePlan.Delay - 6.5f / (onTimePlan.Sprint ? AiManager.SprintSpeed : 6.5f), Is.GreaterThanOrEqualTo(AiManager.StaggeredArrivalMargin));
        }
    }

    [Test]
    public void UnsafeProfileFallsBackToImmediateSprint()
    {
        var onTime = new BotMovementProfile(0.2f, 1.5f, BotArrivalStyle.OnTime);
        var plan = AiManager.PlanHumanLike(onTime, 12f, 1.6f, 0.8f, new AnoMech.Scenarios.Rng(1));
        Assert.That(plan, Is.EqualTo((true, 0f)));
        Assert.That(12f / AiManager.SprintSpeed, Is.LessThan(1.6f));
    }

    [Test]
    public void SharedStagingMoveSpreadsBotDepartures()
    {
        var departures = new Dictionary<PartyRole, float>();
        foreach (var role in Enum.GetValues<PartyRole>())
            if (world.Party.Get(role) is SimPartyNpc bot)
            {
                bot.StopMoving();
                bot.SetPosition(Vector3.Zero);
            }
        new AiManager(world).Move(0f,
            () => AiMove.Create(Enumerable.Repeat<Vector2?>(new Vector2(6.5f, 0f), 8).ToArray()).NaturalOrder(),
            jitter: 0f, arrivalTime: 8f, stagingMove: true);
        for (var frame = 0; frame < 480; frame++)
        {
            world.Events.Tick(1f / 60f);
            foreach (var role in Enum.GetValues<PartyRole>())
                if (!departures.ContainsKey(role) && world.Party.Get(role) is SimPartyNpc { MoveDestination: not null })
                    departures.Add(role, world.Events.Elapsed);
        }
        Assert.That(departures, Has.Count.EqualTo(7));
        Assert.That(departures.Values.Max() - departures.Values.Min(), Is.GreaterThanOrEqualTo(0.3f));
    }

    [Test]
    public void ExplicitDepartureDelayCapsArrivalAndOverridesProfile()
    {
        new AiManager(world).Move(0f, () => AiMove.Single(PartyRole.MainTank, new Vector2(6.5f, 0f)),
            jitter: 0f, arrivalTime: 8f, stagingMove: true, departureDelay: _ => 7f);
        Assert.That(DepartureTime(), Is.EqualTo(6.2f + 1f / 60f).Within(0.02f));
    }
}

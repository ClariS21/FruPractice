using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

// Habit-timed moves through AiManager.Move, on a seated party whose bots really walk.
public class HabitMovementTests
{
    private const float Frame = 1f / 60f;
    private const float Margin = AiManager.StaggeredArrivalMargin;

    private FakeGame fake = null!;
    private SimWorld world = null!;

    [SetUp]
    public void SetUp() => Install(1);

    [TearDown]
    public void TearDown()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private void Install(int seed, MovementHabits? habits = null)
    {
        fake = FakeGame.Install();
        fake.BattleCharas.Player.ClassJob = (byte)AnoMech.Core.UserActions.JobId.Sage;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.MovementHabits = habits ?? ShippedHabits.Value;
        world.Reseed(seed);
        world.SeatParty(new LocalPartyProvider().Roster((uint)AnoMech.Core.UserActions.JobId.Sage));
        foreach (var role in Enum.GetValues<PartyRole>())
            world.Party.Get(role)!.SetPosition(Vector3.Zero);
    }

    private void Reinstall(int seed, MovementHabits? habits = null)
    {
        TearDown();
        Install(seed, habits);
    }

    // Every profile acts the same; a value per metric.
    private static MovementHabits Fixed(float reaction = 0.5f, float? debuff = null, float slackUse = 0f, float settle = 0f,
        float bulge = 0f, float pauseChance = 0f, float pause = 0f, float overshootChance = 0f, float overshoot = 0f,
        float idleRate = 0f, float idleStep = 0f)
        => new(Enum.GetValues<HabitRole>().Select(role => new HabitProfile($"fixed-{role}", role, HabitGroup.Speed, new HabitMetrics
        {
            Debuff = new([debuff ?? reaction]), Cast = new([reaction]), Resolve = new([reaction]), SlackUse = new([slackUse]),
            Settle = new([settle]), Bulge = new([bulge]), PauseChance = new([pauseChance]), Pause = new([pause]),
            OvershootChance = new([overshootChance]), Overshoot = new([overshoot]), IdleRate = new([idleRate]), IdleStep = new([idleStep])
        })).ToArray(), new Dictionary<HabitRole, HabitMetrics>(), new Dictionary<HabitGroup, float> { [HabitGroup.Speed] = 1f });

    private void Step(float seconds, Action? eachFrame = null)
    {
        var frames = (int)MathF.Round(seconds / Frame);
        for (var i = 0; i < frames; i++)
        {
            fake.Frame(Frame, dt =>
            {
                world.Events.Tick(dt);
                world.Tick(dt);
            });
            eachFrame?.Invoke();
        }
    }

    private SimPartyNpc Bot(PartyRole role) => (SimPartyNpc)world.Party.Get(role)!;

    private IEnumerable<PartyRole> BotSeats => Enum.GetValues<PartyRole>().Where(role => role != world.Party.PlayerRole);

    private static Func<IAiMove> To(PartyRole role, float x, float z) => () => AiMove.Single(role, new Vector2(x, z));

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    // When the bot first moves off where it stood.
    private float DepartureOf(PartyRole role, float within = 10f)
    {
        var start = Bot(role).Position;
        var departed = float.NaN;
        Step(within, () =>
        {
            if (float.IsNaN(departed) && Flat(Bot(role).Position, start) > 0.001f) departed = world.Events.Elapsed;
        });
        Assert.That(departed, Is.Not.NaN, "the bot never left");
        return departed;
    }

    // When the bot first comes within `reach` of the point.
    private float ArrivalOf(PartyRole role, Vector3 at, float reach, float within = 10f)
    {
        var arrived = float.NaN;
        Step(within, () =>
        {
            if (float.IsNaN(arrived) && Flat(Bot(role).Position, at) <= reach) arrived = world.Events.Elapsed;
        });
        Assert.That(arrived, Is.Not.NaN, "the bot never arrived");
        return arrived;
    }

    [Test]
    public void ADeclaredMoveLeavesTheProfilesReactionAfterTheCue()
    {
        world.MovementHabits = Fixed(reaction: 1.2f, debuff: 0.4f);
        new AiManager(world).Move(0f, To(PartyRole.MainTank, 6.5f, 0f), knownAt: 0.5f);
        Assert.That(DepartureOf(PartyRole.MainTank), Is.EqualTo(1.7f).Within(Frame + 0.001f));

        Reinstall(1, Fixed(reaction: 1.2f, debuff: 0.4f));
        new AiManager(world).Move(1f, To(PartyRole.MainTank, 6.5f, 0f), knownBy: CueKind.Debuff);
        Assert.That(DepartureOf(PartyRole.MainTank), Is.EqualTo(1.4f).Within(Frame + 0.001f));
    }

    [Test]
    public void AnUndeclaredMoveKeepsTodaysTiming()
    {
        new AiManager(world).Move(0f, To(PartyRole.MainTank, 6.5f, 0f), jitter: 0f, arrivalTime: 8f);
        var departed = DepartureOf(PartyRole.MainTank);
        Assert.Multiple(() =>
        {
            Assert.That(world.Movement.PlanFor(PartyRole.MainTank), Is.Null);
            Assert.That(departed, Is.LessThanOrEqualTo(1.15f + Frame));
        });
    }

    [Test]
    public void StagingMovesAreHabitTimed()
    {
        new AiManager(world).Move(0f, To(PartyRole.MainTank, 6.5f, 0f), arrivalTime: 8f, stagingMove: true);
        Step(Frame);
        Assert.That(world.Movement.PlanFor(PartyRole.MainTank), Is.Not.Null);
    }

    [Test]
    // Full slack use adds at most MaxSlackWait after the reaction; bots no longer idle to the last safe moment.
    public void TheSlackShareAddsAtMostTheCappedWait()
    {
        var spot = new Vector3(6.5f, 0f, 0f);
        world.MovementHabits = Fixed(reaction: 0.5f, slackUse: 0f);
        new AiManager(world).Move(0f, To(PartyRole.MainTank, 6.5f, 0f), arrivalTime: 8f, room: 0f);
        var prompt = ArrivalOf(PartyRole.MainTank, spot, 0.01f);

        Reinstall(1, Fixed(reaction: 0.5f, slackUse: 1f));
        new AiManager(world).Move(0f, To(PartyRole.MainTank, 6.5f, 0f), arrivalTime: 8f, room: 0f);
        var late = ArrivalOf(PartyRole.MainTank, spot, 0.01f);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.EqualTo(1.5f).Within(2 * Frame + 0.001f));
            Assert.That(late, Is.EqualTo(1.5f + HabitTiming.MaxSlackWait).Within(2 * Frame + 0.001f));
        });
    }

    [Test]
    public void EveryArrivalOnTheShippedProfilesKeepsTheMargin()
    {
        var checkedMoves = 0;
        for (var seed = 0; seed < 30; seed++)
        {
            Reinstall(seed);
            var rng = new Rng(seed);
            var ai = new AiManager(world);
            var deadlines = new Dictionary<PartyRole, (Vector3 Spot, float InBy)>();
            foreach (var role in BotSeats)
            {
                var from = new Vector3(rng.NextSingle() * 24f - 12f, 0f, rng.NextSingle() * 24f - 12f);
                var spot = new Vector3(rng.NextSingle() * 24f - 12f, 0f, rng.NextSingle() * 24f - 12f);
                Bot(role).SetPosition(from);
                var reach = Flat(from, spot) + 0.5f;
                // Roomy, squeezed (the reaction doesn't fit), or only a sprint makes it.
                var inBy = ((seed + (int)role) % 3) switch
                {
                    0 => reach / 6.5f + Margin + 0.5f + 4f * rng.NextSingle(),
                    1 => reach / 6.5f + Margin + 0.15f + 0.4f * rng.NextSingle(),
                    _ => reach / AiManager.SprintSpeed + Margin + 0.2f + 0.1f * rng.NextSingle(),
                };
                var seat = role;
                ai.Move(0f, To(seat, spot.X, spot.Z), arrivalTime: inBy, room: 0.5f);
                deadlines[role] = (spot, inBy);
            }
            var last = deadlines.Values.Max(d => d.InBy);
            Step(last + 0.1f, () =>
            {
                var now = world.Events.Elapsed;
                foreach (var (role, (spot, inBy)) in deadlines)
                    if (now >= inBy - Margin && now <= inBy)
                        Assert.That(Flat(Bot(role).Position, spot), Is.LessThanOrEqualTo(0.5f + 0.01f),
                            $"seed {seed}: {role} not on its spot at {now:F2}, {inBy - now:F2} s before {inBy:F2}");
            });
            checkedMoves += deadlines.Count;
        }
        Assert.That(checkedMoves, Is.EqualTo(30 * 7));
    }

    [Test]
    public void OutByTakesTheBotOutOfTheDangerInTime()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            Reinstall(seed);
            new AiManager(world).Move(0f, To(PartyRole.MainTank, 12f, 0f), outBy: 3f, exitDistance: 4f);
            Step(3f - Margin);
            Assert.That(Flat(Bot(PartyRole.MainTank).Position, Vector3.Zero), Is.GreaterThanOrEqualTo(4f - 0.1f), $"seed {seed}");
        }
    }

    [Test]
    public void ANotBeforeSpotIsNeverReachedEarly()
    {
        var spot = new Vector3(6.5f, 0f, 0f);
        for (var seed = 0; seed < 30; seed++)
        {
            Reinstall(seed);
            var tank = Bot(PartyRole.MainTank);
            // Half the time the bot is still walking towards the spot when the move fires.
            if (seed % 2 == 1) tank.MoveTo(new Vector3(5f, 0f, 0f), speed: 6.5f);
            new AiManager(world).Move(0.1f, To(PartyRole.MainTank, spot.X, spot.Z), notBefore: 4f, room: 0f);
            var arrived = ArrivalOf(PartyRole.MainTank, spot, 0.05f);
            Assert.That(arrived, Is.GreaterThanOrEqualTo(4f - Frame), $"seed {seed}");
        }
    }

    [Test]
    public void SettledAndIdlePositionsStayWithinRoom()
    {
        world.MovementHabits = Fixed(reaction: 0.2f, settle: 5f, idleRate: 3f, idleStep: 2f);
        var spot = new Vector3(6.5f, 0f, 0f);
        new AiManager(world).Move(0f, To(PartyRole.MainTank, spot.X, spot.Z), room: 0.8f);
        var tank = Bot(PartyRole.MainTank);
        var stands = new List<Vector3>();
        var wasMoving = true;
        Step(20f, () =>
        {
            if (!world.Movement.IsSettled(PartyRole.MainTank)) return;
            Assert.That(Flat(tank.Position, spot), Is.LessThanOrEqualTo(0.8f + 0.01f), $"at {world.Events.Elapsed:F2}");
            if (wasMoving && !tank.Relocating) stands.Add(tank.Position);
            wasMoving = tank.Relocating;
        });
        Assert.That(stands.Distinct().Count(), Is.GreaterThanOrEqualTo(5), "the bot shuffles while it waits");
    }

    [Test]
    public void IdleStepsStopBeforeTheNextDepartureAndDuringAHold()
    {
        world.MovementHabits = Fixed(reaction: 0.2f, idleRate: 4f, idleStep: 1f);
        var ai = new AiManager(world);
        ai.Move(0f, To(PartyRole.MainTank, 6.5f, 0f), room: 1f);
        ai.Move(6f, To(PartyRole.MainTank, -6.5f, 0f), jitter: 0f, arrivalTime: 20f, hold: true);
        var tank = Bot(PartyRole.MainTank);
        Vector3? before = null;
        var shuffled = false;
        Step(17.5f, () =>
        {
            var now = world.Events.Elapsed;
            if (now is > 2f and < 4.5f && tank.Relocating) shuffled = true;
            if (now < 4.7f) return;
            before ??= tank.Position;
            Assert.That(tank.Position, Is.EqualTo(before.Value), $"moved at {now:F2}, before the hold's departure");
        });
        Assert.That(shuffled, "idle steps while nothing is due");
    }

    [Test]
    public void PathShapingStaysInsideTheRoomAndKeepsTheMargin()
    {
        world.MovementHabits = Fixed(reaction: 0.2f, bulge: 0.5f, pauseChance: 1f, pause: 0.5f, overshootChance: 1f, overshoot: 3f);
        var spot = new Vector3(13f, 0f, 0f);
        new AiManager(world).Move(0f, To(PartyRole.MainTank, spot.X, spot.Z), arrivalTime: 10f, room: 1f);
        var tank = Bot(PartyRole.MainTank);
        var sideways = 0f;
        var furthest = 0f;
        var still = 0f;
        var longestMidwayStop = 0f;
        var previous = tank.Position;
        var settledAt = float.NaN;
        Step(10f, () =>
        {
            var position = tank.Position;
            sideways = MathF.Max(sideways, MathF.Abs(position.Z));
            furthest = MathF.Max(furthest, position.X);
            var moved = Flat(position, previous) > 1e-4f;
            still = moved ? 0f : still + Frame;
            if (!moved && position.X is > 5f and < 8f) longestMidwayStop = MathF.Max(longestMidwayStop, still);
            if (float.IsNaN(settledAt) && world.Movement.IsSettled(PartyRole.MainTank)) settledAt = world.Events.Elapsed;
            previous = position;
        });
        Assert.Multiple(() =>
        {
            Assert.That(sideways, Is.InRange(0.9f, 1f + 0.01f), "one bulge of min(bulge x length, room, 1.5)");
            Assert.That(longestMidwayStop, Is.InRange(0.45f, 0.5f + 0.1f), "one pause 40-60% of the way");
            Assert.That(furthest, Is.InRange(13.5f, 14f + 0.01f), "an overshoot, within room of the spot");
            Assert.That(Flat(tank.Position, spot), Is.LessThan(0.01f), "stepped back onto the spot");
            Assert.That(settledAt, Is.LessThanOrEqualTo(10f - Margin));
        });
    }

    [Test]
    public void PathShapingIsDroppedWhenItWouldEatTheMargin()
    {
        world.MovementHabits = Fixed(reaction: 0.2f, bulge: 0.5f, pauseChance: 1f, pause: 0.5f, overshootChance: 1f, overshoot: 3f);
        var spot = new Vector3(13f, 0f, 0f);
        // A 2 s walk leaves 0.05 s beyond the margin and the frame reserve.
        const float inBy = 0.2f + 2f + 0.95f;
        new AiManager(world).Move(0f, To(PartyRole.MainTank, spot.X, spot.Z), arrivalTime: inBy, room: 1f);
        var tank = Bot(PartyRole.MainTank);
        var sideways = 0f;
        var furthest = 0f;
        var arrived = float.NaN;
        Step(4f, () =>
        {
            sideways = MathF.Max(sideways, MathF.Abs(tank.Position.Z));
            furthest = MathF.Max(furthest, tank.Position.X);
            if (float.IsNaN(arrived) && Flat(tank.Position, spot) <= 1f) arrived = world.Events.Elapsed;
        });
        Assert.Multiple(() =>
        {
            Assert.That(sideways, Is.LessThan(0.01f));
            Assert.That(furthest, Is.LessThanOrEqualTo(13f + 0.01f));
            Assert.That(arrived, Is.LessThanOrEqualTo(inBy - Margin));
        });
    }

    [Test]
    public void TheHumansSeatIsNeverMovedByHabits()
    {
        var player = world.Party.PlayerRole;
        var start = world.Party.Get(player)!.Position;
        var ai = new AiManager(world);
        foreach (var role in Enum.GetValues<PartyRole>())
            ai.Move(0f, To(role, 6f, (int)role), knownAt: 0f, room: 1f, arrivalTime: 6f);
        Step(8f);
        Assert.Multiple(() =>
        {
            Assert.That(world.HabitFor(player), Is.Null);
            Assert.That(world.Movement.PlanFor(player), Is.Null);
            Assert.That(world.Party.Get(player)!.Position, Is.EqualTo(start));
            foreach (var role in BotSeats)
                Assert.That(Flat(Bot(role).Position, new Vector3(6f, 0f, (int)role)), Is.LessThanOrEqualTo(1f + 0.01f), $"{role}");
        });
    }

    [Test]
    public void LastMomentIgnoresTheHabitLayer()
    {
        world.BotTiming = BotTiming.LastMoment;
        new AiManager(world).Move(0f, To(PartyRole.MainTank, 6.5f, 0f), jitter: 0f, arrivalTime: 8f,
            knownAt: 0f, knownBy: CueKind.Debuff, room: 1f, notBefore: 1f);
        Assert.Multiple(() =>
        {
            Assert.That(DepartureOf(PartyRole.MainTank), Is.EqualTo(6.75f).Within(0.02f));
            Assert.That(world.Movement.PlanFor(PartyRole.MainTank), Is.Null);
        });
    }

    [Test]
    public void HoldAndAnExplicitDepartureDelayKeepTheOldTiming()
    {
        new AiManager(world).Move(0f, To(PartyRole.MainTank, 6.5f, 0f), jitter: 0f, arrivalTime: 8f, hold: true, room: 1f);
        Assert.That(DepartureOf(PartyRole.MainTank), Is.EqualTo(6.75f).Within(0.02f));
        Assert.That(world.Movement.PlanFor(PartyRole.MainTank), Is.Null);

        Reinstall(1);
        new AiManager(world).Move(0f, To(PartyRole.MainTank, 6.5f, 0f), jitter: 0f, arrivalTime: 8f, departureDelay: _ => 7f, room: 1f);
        Assert.That(DepartureOf(PartyRole.MainTank), Is.EqualTo(6.2f + Frame).Within(0.02f));
        Assert.That(world.Movement.PlanFor(PartyRole.MainTank), Is.Null);
    }

    [Test]
    public void ANewerOrderReplacesAPendingHabitMove()
    {
        world.MovementHabits = Fixed(reaction: 2f);
        var ai = new AiManager(world);
        ai.Move(0f, To(PartyRole.MainTank, 6.5f, 0f), room: 0f);
        ai.Move(1f, To(PartyRole.MainTank, -6.5f, 0f), room: 0f);
        var tank = Bot(PartyRole.MainTank);
        var furthestEast = 0f;
        Step(6f, () => furthestEast = MathF.Max(furthestEast, tank.Position.X));
        Assert.Multiple(() =>
        {
            Assert.That(furthestEast, Is.LessThan(0.001f));
            Assert.That(Flat(tank.Position, new Vector3(-6.5f, 0f, 0f)), Is.LessThan(0.01f));
        });
    }

    [Test]
    public void ABotSprintsWhenWalkingCannotKeepTheMargin()
    {
        world.MovementHabits = Fixed(reaction: 0.5f);
        var spot = new Vector3(13f, 0f, 0f);
        new AiManager(world).Move(0f, To(PartyRole.MainTank, spot.X, spot.Z), arrivalTime: 2.6f, room: 0f);
        var arrived = ArrivalOf(PartyRole.MainTank, spot, 0.01f);
        Assert.That(arrived, Is.LessThanOrEqualTo(2.6f - Margin));
    }
}

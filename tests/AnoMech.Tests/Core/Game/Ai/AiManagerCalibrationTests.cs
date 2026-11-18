using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Umad;

namespace AnoMech.Tests;

// The zone's calibration decides how AiManager times a bot's move: habit-planned, human-like, or at
// the last moment.
public class AiManagerCalibrationTests
{
    private const float Frame = 1f / 60f;
    private const float WalkSpeed = 6.5f;

    private FakeGame fake = null!;
    private Game game = null!;
    private SimWorld world = null!;

    [SetUp]
    public void SetUp()
    {
        fake = FakeGame.Install();
        fake.BattleCharas.Player.ClassJob = (byte)JobId.Sage;
        game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.MovementHabits = ShippedHabits.Value;
    }

    [TearDown]
    public void TearDown()
    {
        game.Dispose();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private void SeatIn(IZone zone)
    {
        game.ZoneIn(zone, SessionMode.Local);
        fake.Frame(Frame, _ => { });
        world.Reseed(12345);
        world.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
        world.Party.Get(PartyRole.MainTank)!.SetPosition(Vector3.Zero);
    }

    private void Move(float arrivalTime, float? knownAt = null)
        => new AiManager(world).Move(0f, () => AiMove.Single(PartyRole.MainTank, new Vector2(WalkSpeed, 0f)),
            jitter: 0f, arrivalTime: arrivalTime, knownAt: knownAt);

    // When the main tank's MoveTo goes out, or a habit plan's departure.
    private float DepartureTime()
    {
        var member = world.Party.Get(PartyRole.MainTank)!;
        for (var i = 0; i < 600; i++)
        {
            world.Events.Tick(Frame);
            if (member.MoveDestination is not null) return world.Events.Elapsed;
        }
        Assert.Fail("The move was never ordered.");
        return float.NaN;
    }

    [Test]
    public void EnteringAZoneAppliesItsCalibration()
    {
        Assert.That(world.Calibration, Is.SameAs(ZoneCalibration.All), "a world in no zone");
        SeatIn(UmadZone.Instance);
        Assert.Multiple(() =>
        {
            Assert.That(((IZone)FruZone.Instance).Calibration, Is.SameAs(ZoneCalibration.All));
            Assert.That(world.Calibration, Is.EqualTo(new ZoneCalibration { ActEmission = false }));
        });
    }

    [Test]
    public void ADeclaredMoveInADmuWorldIsHabitPlanned()
    {
        SeatIn(UmadZone.Instance);
        Move(arrivalTime: 8f, knownAt: 0f);
        world.Events.Tick(Frame);

        var plan = world.Movement.PlanFor(PartyRole.MainTank);
        Assert.Multiple(() =>
        {
            Assert.That(world.HabitFor(PartyRole.MainTank), Is.Not.Null);
            Assert.That(plan, Is.Not.Null, "habit-planned");
            Assert.That(plan!.Order.InBy, Is.EqualTo(8f).Within(1e-4f));
            var walk = new Vector2(plan.Target.X, plan.Target.Z).Length() / plan.Speed;
            Assert.That(plan.Departure + walk, Is.LessThanOrEqualTo(8f - AiManager.StaggeredArrivalMargin));
        });
    }

    // Reactions run 0.15 to 1.15 s.
    private const float LatestReaction = 1.15f + 2 * Frame;

    [Test]
    public void ADmuBotLeavesAfterAReactionAndKeepsTheArrivalMargin()
    {
        SeatIn(UmadZone.Instance);
        Move(arrivalTime: 8f);
        Assert.That(DepartureTime(), Is.LessThanOrEqualTo(LatestReaction), "a reaction, not the latest safe moment");

        world.Events.Clear();
        world.Party.Get(PartyRole.MainTank)!.StopMoving();
        // 1 s of walking with 0.9 s to spare: only a departure within 0.1 s keeps the margin.
        Move(arrivalTime: 1.9f);
        Assert.That(DepartureTime(), Is.LessThanOrEqualTo(1.9f - AiManager.StaggeredArrivalMargin - 1f + 2 * Frame));
    }

    [Test]
    public void AZoneWithoutHumanLikeMovementLeavesAtTheLatestSafeMoment()
    {
        SeatIn(UmadZone.Instance);
        world.Calibration = new ZoneCalibration { HumanLikeMovement = false };
        Move(arrivalTime: 8f, knownAt: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(DepartureTime(), Is.EqualTo(6.75f).Within(0.02f));
            Assert.That(world.Movement.PlanFor(PartyRole.MainTank), Is.Null);
        });
    }

    [Test]
    public void AZoneWithoutHabitsMovesADeclaredMoveAsAPlainOne()
    {
        SeatIn(UmadZone.Instance);
        world.Calibration = new ZoneCalibration { Habits = false };
        Move(arrivalTime: 8f, knownAt: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(DepartureTime(), Is.LessThanOrEqualTo(LatestReaction));
            Assert.That(world.HabitFor(PartyRole.MainTank), Is.Null);
            Assert.That(world.Movement.PlanFor(PartyRole.MainTank), Is.Null);
        });
    }
}

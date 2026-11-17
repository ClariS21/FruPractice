using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru.P5Full;

namespace AnoMech.Tests;

// What bots do on their own between mechanics: bounded to their strat spot, quiet before the strat's
// next move, still through casts, and never the human's seat.
public class UptimeMovementTests
{
    private const uint BossBNpcBaseId = 19508;
    private const float Frame = 1f / 60f;
    private const float Bound = 2.5f;

    private FakeGame fake = null!;
    private SimWorld world = null!;
    private readonly List<UptimeMove> moves = [];

    [SetUp]
    public void SetUp() => Install(1, Habits());

    [TearDown]
    public void TearDown()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    // Every seat's profile never stands still through a cast and switches flank often, and has no idle
    // shuffles or path quirks, so what moves a bot is uptime.
    private static MovementHabits Habits(float castStill = 0f, float switchRate = 0.4f)
        => new(Enum.GetValues<HabitRole>().Select(role => new HabitProfile($"uptime-{role}", role, HabitGroup.Speed, new HabitMetrics
        {
            Debuff = new([0.4f]), Cast = new([0.4f]), Resolve = new([0.4f]), SlackUse = new([0f]), Settle = new([0f]),
            Bulge = new([0f]), PauseChance = new([0f]), Pause = new([0f]), OvershootChance = new([0f]), Overshoot = new([0f]),
            IdleRate = new([0f]), IdleStep = new([0f]), CastStill = new([castStill]), SwitchRate = new([switchRate])
        })).ToArray(), new Dictionary<HabitRole, HabitMetrics>(), new Dictionary<HabitGroup, float> { [HabitGroup.Speed] = 1f });

    private void Install(int seed, MovementHabits habits)
    {
        moves.Clear();
        fake = FakeGame.Install();
        fake.BattleCharas.Player.ClassJob = (byte)AnoMech.Core.UserActions.JobId.Sage;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.MovementHabits = habits;
        world.Reseed(seed);
        world.SeatParty(new LocalPartyProvider().Roster((uint)AnoMech.Core.UserActions.JobId.Sage));
        world.SpawnEnemy(new EnemySpawnConfig(BossBNpcBaseId, Targetable: true));
        foreach (var role in Enum.GetValues<PartyRole>())
            world.Party.Get(role)!.SetPosition(Vector3.Zero);
        world.Uptime.Issued += moves.Add;
        world.Uptime.Allow(0f, 1000f);
    }

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

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    // The party takes spots on a ring around the boss, at `time` (a declared move, so it's habit-timed).
    private Func<IAiMove> Ring(float radius, float turn = 0f)
        => () => AiMove.Create(Enumerable.Range(0, 8).Select(i =>
        {
            var angle = turn + i * MathF.Tau / 8f;
            return (Vector2?)new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * radius;
        }).ToArray()).NaturalOrder();

    [Test]
    public void DestinationsStayWithinTheBoundOfTheStratSpot()
    {
        new AiManager(world).Move(0f, Ring(4f), knownAt: 0f, room: 0.5f);
        Step(60f);
        Assert.That(moves, Is.Not.Empty, "bots took part in uptime");
        Assert.That(moves.Select(m => m.Role).Distinct().Count(), Is.GreaterThan(3), "across the seats");
        foreach (var move in moves)
        {
            Assert.That(Flat(move.Destination, move.Anchor), Is.LessThanOrEqualTo(Bound), $"{move.Role} destination at {move.At:F1}");
            Assert.That(Flat(move.Settle, move.Anchor), Is.LessThanOrEqualTo(Bound), $"{move.Role} settle spot at {move.At:F1}");
        }
    }

    [Test]
    public void NoUptimeMoveWalksWithinThreeSecondsOfAStratDeparture()
    {
        var ai = new AiManager(world);
        ai.Move(0f, Ring(4f), knownAt: 0f, room: 0.5f);
        ai.Move(25f, Ring(5f, 0.4f), knownAt: 25f, room: 0.5f);
        ai.Move(40f, Ring(3f, 0.9f), knownAt: 40f, room: 0.5f);
        var walked = 0;
        Step(60f, () =>
        {
            var now = world.Events.Elapsed;
            foreach (var role in BotSeats)
            {
                if (world.Movement.PlanFor(role) is not { Order.Uptime: true } || world.Movement.IsSettled(role)) continue;
                // The plan stands from the decision to the settle, the walk and its wait for the reaction both.
                walked++;
                if (world.Departures.NextStratAfter(now, role) is { } due)
                    Assert.That(due - now, Is.GreaterThanOrEqualTo(UptimeMovement.QuietBefore - 2 * Frame), $"{role} at {now:F2}");
            }
        });
        Assert.That(walked, Is.GreaterThan(0));
        Assert.That(moves.Count(m => m.At is > 18f and < 25f), Is.GreaterThan(0), "uptime runs right up to the quiet window");
        foreach (var move in moves)
            if (move.NextStrat is { } due)
                Assert.That(due - move.At, Is.GreaterThanOrEqualTo(UptimeMovement.QuietBefore), $"{move.Role} decided at {move.At:F2}");
    }

    [Test]
    public void CastersNeverMoveMidCast()
    {
        new AiManager(world).Move(0f, Ring(4f), knownAt: 0f, room: 0.5f);
        var casting = 0;
        var seen = 0;
        Step(90f, () =>
        {
            if (world.Events.Elapsed < 4f) return;
            foreach (var role in BotSeats)
            {
                var bot = Bot(role);
                var busy = bot.Casting.IsCasting || world.Party.Combat!.IsCasting(bot);
                if (busy) casting++;
                if (busy && bot.Relocating) Assert.Fail($"{role} (job {bot.ClassJob}) moved at {world.Events.Elapsed:F2} with a cast up: busy {bot.Casting.IsBusy}, combat {world.Party.Combat!.IsCasting(bot)}, plan uptime {world.Movement.PlanFor(role)?.Order.Uptime}, settled {world.Movement.IsSettled(role)}");
            }
        });
        seen = moves.Count(m => SimWorld.HabitRoleOf(m.Role) is HabitRole.Caster or HabitRole.Healer);
        Assert.That(casting, Is.GreaterThan(0), "someone cast");
        Assert.That(seen, Is.GreaterThan(0), "casters and healers took part in uptime");
    }

    [Test]
    public void TheMainTankAndThePlayerSeatAreNeverMoved()
    {
        var player = world.Party.Get(world.Party.PlayerRole)!;
        var at = player.Position;
        new AiManager(world).Move(0f, Ring(4f), knownAt: 0f, room: 0.5f);
        Step(60f);
        Assert.That(moves, Is.Not.Empty);
        Assert.That(moves.Select(m => m.Role), Does.Not.Contain(world.Party.PlayerRole));
        Assert.That(moves.Select(m => m.Role), Does.Not.Contain(PartyRole.MainTank));
        Assert.That(world.Uptime.IsActive(world.Party.PlayerRole), Is.False);
        Assert.That(player.Position, Is.EqualTo(at));
    }

    [Test]
    public void UptimeStaysOutOfAHoldAndWaitsForAStratMove()
    {
        var ai = new AiManager(world);
        ai.Move(0f, Ring(4f), knownAt: 0f, room: 0.5f);
        // A held move supersedes the habit plan, and a hold never counts as a settled strat spot.
        ai.Move(10f, Ring(5f), arrivalTime: 40f, hold: true);
        Step(12f);
        var before = moves.Count;
        Step(25f);
        Assert.That(moves.Count, Is.EqualTo(before), "no uptime while a hold stands");
    }

    [Test]
    public void MeleeSpendMostOfTheirUptimeWithinReachOfTheirTarget()
    {
        var inReach = 0;
        var total = 0;
        var seats = new Dictionary<PartyRole, (int Reach, int Total)>();
        var declared = false;
        var run = ScenarioRun.Execute(typeof(FruP5FullScenario), 0, 1, new ScenarioRunOptions
        {
            Probe = probe =>
            {
                if (!declared) { probe.World.Uptime.Allow(0f, 10000f); declared = true; }
                if (probe.World.Party.Combat?.FindTarget() is not { } boss) return;
                foreach (var role in Enum.GetValues<PartyRole>())
                {
                    if (SimWorld.HabitRoleOf(role) != HabitRole.Melee || !probe.World.Uptime.IsEngaged(role)) continue;
                    var bot = probe.World.Party.Get(role)!;
                    total++;
                    var near = Flat(bot.Position, boss.Position) <= boss.HitboxRadius + 3.5f;
                    if (near) inReach++;
                    seats[role] = seats.GetValueOrDefault(role) is var (r, t) ? (r + (near ? 1 : 0), t + 1) : default;
                }
            }
        });
        TestContext.Out.WriteLine($"melee uptime {inReach}/{total}: {string.Join(", ", seats.Select(s => $"{s.Key} {s.Value.Reach}/{s.Value.Total}"))}");
        // The window covers the whole run, hazards included, so a death is not this test's concern: it
        // measures where uptime puts the melee.
        Assert.That(total, Is.GreaterThan(60), "melee had uptime in the run");
        Assert.That(inReach / (float)total, Is.GreaterThanOrEqualTo(0.8f),
            $"{inReach}/{total} frames in reach; by seat {string.Join(", ", seats.Select(s => $"{s.Key} {s.Value.Reach}/{s.Value.Total}"))}");
    }

    [TestCase(typeof(AnoMech.Scenarios.Fru.P3Apocalypse.FruP3ApocalypseScenario))]
    [TestCase(typeof(AnoMech.Scenarios.Fru.P5ParadiseRegained.FruP5ParadiseRegainedScenario))]
    public void WithNoDeclaredWindowUptimeNeverMovesAnyone(Type scenario)
    {
        var issued = 0;
        var engaged = 0;
        var hooked = false;
        var run = ScenarioRun.Execute(scenario, 0, 1, new ScenarioRunOptions
        {
            Probe = probe =>
            {
                if (!hooked) { probe.World.Uptime.Issued += _ => issued++; hooked = true; }
                foreach (var role in Enum.GetValues<PartyRole>())
                    if (probe.World.Uptime.IsEngaged(role)) engaged++;
            }
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(issued, Is.Zero, "uptime issued a move with no declared window");
        Assert.That(engaged, Is.Zero, "uptime engaged a seat with no declared window");
    }
}

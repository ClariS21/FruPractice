using System.Numerics;
using System.Reflection;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using static AnoMech.Scenarios.Fru.P4CrystallizeTime.FruP4CrystallizeTimeRoutes;

namespace AnoMech.Tests;

// The dragon puddles against the walks: a route never puts its Darkness inside a puddle the same route uses, and a puddle that
// drops across a walk already under way turns the walker aside before it steps in.
public class FruP4CrystallizeTimeReplanTests
{
    private const float PuddleRadius = 1f;
    private const float PuddleKeepOut = 1.5f;
    private static readonly Vector3 Start = new(-8f, 0f, 0f);
    private static readonly Vector3 Across = new(8f, 0f, 0f);

    private Game game = null!;
    private SimWorld world = null!;
    private FruP4CrystallizeTimeState state = null!;
    private FruP4CrystallizeTimeAi ai = null!;
    private PartyRole walker;
    private SimCharacter bot = null!;

    [Test]
    public void EveryRoutesDarknessSpotIsOutsideItsPuddles()
    {
        var routes = typeof(FruP4CrystallizeTimeRoutes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.GetValue(null) is IReadOnlyDictionary<CrystallizeTimeAssignment, CrystallizeTimeDestination>)
            .ToList();
        Assert.That(routes, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var field in routes)
            {
                var route = (IReadOnlyDictionary<CrystallizeTimeAssignment, CrystallizeTimeDestination>)field.GetValue(null)!;
                if (!route.TryGetValue(CrystallizeTimeAssignment.Darkness, out var darkness) || darkness.Puddle is not null) continue;
                foreach (var source in route.Values.Select(d => d.Puddle).OfType<CrystallizeTimeAssignment>())
                    Assert.That(Flat(darkness.Position, CaughtAt(source)), Is.GreaterThan(PuddleKeepOut),
                        $"{field.Name}: the Darkness spot is in {source}'s puddle");
            }
        });
    }

    // The routes' puddles are the Aeros': each drops where its Aero takes the dragon head.
    private static Vector3 CaughtAt(CrystallizeTimeAssignment source) => source switch
    {
        CrystallizeTimeAssignment.AeroWest or CrystallizeTimeAssignment.AeroEast => POST_HG_2_NW[source].Position,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "no catch spot known for this puddle"),
    };

    [Test]
    public void APuddleDroppingAcrossAWalkUnderWayTurnsTheWalkerAside()
    {
        WalkTowardsWherePuddleDrops();
        var closest = float.PositiveInfinity;
        for (var frame = 0; frame < 6 * 60; frame++)
        {
            Frame();
            closest = MathF.Min(closest, Flat(bot.Position, Vector3.Zero));
        }
        Assert.That(closest, Is.GreaterThan(PuddleRadius));
    }

    [Test]
    public void ANewerOrderOutranksTheReplannedWalk()
    {
        WalkTowardsWherePuddleDrops();
        Frame();
        var newer = new Vector3(-8f, 0f, 6f);
        ai.Send(walker, newer, CueKind.Resolve, 0f);
        for (var frame = 0; frame < 6 * 60; frame++) Frame();
        Assert.That(Flat(bot.Position, newer), Is.LessThan(PuddleRadius));
    }

    // The walker heads straight across the middle; once it is on its way, a puddle drops there.
    private void WalkTowardsWherePuddleDrops()
    {
        bot.SetPosition(Start);
        ai.Send(walker, Across, CueKind.Resolve, 0f);
        for (var frame = 0; frame < 3 * 60 && Flat(bot.Position, Start) < 1f; frame++) Frame();
        Assert.That(bot.MoveDestination, Is.Not.Null, "the walker never set off");
        Assert.That(bot.Position.X, Is.LessThan(-5f), "the walker is already too close for the test");
        state.AddDragonPuddle(Enum.GetValues<PartyRole>().First(r => r != walker), Vector3.Zero);
    }

    [SetUp]
    public void SetUp()
    {
        FakeGame.Install();
        game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.Reseed(1);
        world.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
        state = new FruP4CrystallizeTimeState(new Rng(1), world.Party.PlayerRole, new());
        ai = new FruP4CrystallizeTimeAi();
        ai.Run(state, world);
        // Only the test's own orders: none of the drill's steps.
        world.Events.Clear();
        walker = Enum.GetValues<PartyRole>().First(r => world.Party.Get(r) is SimPartyNpc);
        bot = world.Party.Get(walker)!;
    }

    [TearDown]
    public void TearDown()
    {
        game.Dispose();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private void Frame()
    {
        const float delta = 1f / 60f;
        world.Events.Tick(delta);
        foreach (var role in Enum.GetValues<PartyRole>()) world.Party.Get(role)?.Tick(delta);
        ai.Tick(state, world, delta);
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}

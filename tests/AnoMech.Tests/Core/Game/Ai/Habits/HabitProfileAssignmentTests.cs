using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

public class HabitProfileAssignmentTests
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
        world.MovementHabits = ShippedHabits.Value;
        world.Reseed(1);
        world.SeatParty(new LocalPartyProvider().Roster((uint)AnoMech.Core.UserActions.JobId.Sage));
    }

    [TearDown]
    public void TearDown()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private IEnumerable<PartyRole> BotSeats => Enum.GetValues<PartyRole>().Where(role => role != world.Party.PlayerRole);

    private Dictionary<PartyRole, string> Draw(int seed, bool reversed = false)
    {
        world.Reseed(seed);
        var seats = reversed ? BotSeats.Reverse() : BotSeats;
        return seats.ToDictionary(role => role, role => world.HabitFor(role)!.Id);
    }

    [Test]
    public void TheSameSeedGivesTheSameProfilesWhicheverSeatAsksFirst()
    {
        var first = Draw(7);
        var cached = world.HabitFor(PartyRole.MainTank);
        Assert.Multiple(() =>
        {
            Assert.That(world.HabitFor(PartyRole.MainTank), Is.SameAs(cached), "cached per seat");
            Assert.That(Draw(7), Is.EqualTo(first));
            Assert.That(Draw(7, reversed: true), Is.EqualTo(first));
        });
    }

    [Test]
    public void DifferentSeedsMixSpeedAndDecentProfiles()
    {
        var groups = new Dictionary<HabitGroup, int> { [HabitGroup.Speed] = 0, [HabitGroup.Decent] = 0 };
        var ids = new HashSet<string>();
        for (var seed = 0; seed < 60; seed++)
        {
            world.Reseed(seed);
            foreach (var role in BotSeats)
            {
                var profile = world.HabitFor(role)!;
                groups[profile.Group]++;
                ids.Add(profile.Id);
            }
        }
        var speedShare = groups[HabitGroup.Speed] / (float)(groups[HabitGroup.Speed] + groups[HabitGroup.Decent]);
        Assert.Multiple(() =>
        {
            Assert.That(speedShare, Is.InRange(0.55f, 0.78f), "two thirds speed, one third decent");
            Assert.That(ids, Has.Count.GreaterThan(150), "seats vary from run to run");
        });
    }

    [Test]
    public void EachBotSeatDrawsAProfileOfItsRole()
    {
        var expected = new Dictionary<PartyRole, HabitRole>
        {
            [PartyRole.MainTank] = HabitRole.Tank, [PartyRole.OffTank] = HabitRole.Tank,
            [PartyRole.RegenHealer] = HabitRole.Healer, [PartyRole.ShieldHealer] = HabitRole.Healer,
            [PartyRole.MeleeDpsA] = HabitRole.Melee, [PartyRole.MeleeDpsB] = HabitRole.Melee,
            [PartyRole.PhysRangedDps] = HabitRole.Ranged, [PartyRole.CasterDps] = HabitRole.Caster,
        };
        for (var seed = 0; seed < 10; seed++)
        {
            world.Reseed(seed);
            foreach (var role in BotSeats)
                Assert.That(world.HabitFor(role)!.Role, Is.EqualTo(expected[role]), $"{role}, seed {seed}");
        }
    }

    [Test]
    public void ThePlayersSeatGetsNoProfile()
    {
        for (var seed = 0; seed < 10; seed++)
        {
            world.Reseed(seed);
            Assert.That(world.HabitFor(world.Party.PlayerRole), Is.Null);
        }
    }

    [Test]
    public void ADrawnProfileHasEveryFallbackApplied()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            world.Reseed(seed);
            foreach (var role in BotSeats)
            {
                var metrics = world.HabitFor(role)!.Metrics;
                Assert.That(new[] { metrics.Debuff, metrics.Cast, metrics.Resolve, metrics.SlackUse, metrics.Settle, metrics.Bulge,
                    metrics.PauseChance, metrics.Pause, metrics.OvershootChance, metrics.Overshoot, metrics.IdleRate, metrics.IdleStep,
                    metrics.Angle, metrics.SwitchRate, metrics.CastStill }, Has.None.Null, $"{role}, seed {seed}");
            }
        }
    }

    [Test]
    public void ProfileDrawsDoNotAdvanceMechanicRandomness()
    {
        var expected = new Rng(12345).Next(int.MaxValue);
        world.Reseed(12345);
        foreach (var role in Enum.GetValues<PartyRole>()) world.HabitFor(role);
        Assert.That(world.Rng.Next(int.MaxValue), Is.EqualTo(expected));
    }
}

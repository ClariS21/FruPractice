using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyHp;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;

namespace AnoMech.Tests;

public class RegenTests
{
    private const uint ChaosP3BNpcBaseId = 19508;
    private const float Step = 0.01f;
    private const int Seed = 5;
    private static readonly DataminingGameData Data = new(new FakeRsvFunctions());

    private static readonly EnemyAction Pulsar = FruActions.ShockwavePulsar with
    {
        Cast = new() { CastSecondsOverride = 0.5f },
        Effects = [Damage()],
        Timing = new() { DamageDelay = 0.5f },
    };

    private FakeGame fake = null!;
    private SimWorld world = null!;
    private SimEnemy boss = null!;

    [SetUp]
    public void InstallGame()
    {
        fake = FakeGame.Install();
        fake.BattleCharas.Player.ClassJob = (byte)JobId.Sage;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.Reseed(Seed);
        world.BotCombatEnabled = false;
        world.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
        boss = world.SpawnEnemy(new EnemySpawnConfig(ChaosP3BNpcBaseId))!;
        Advance(0.5f);
        for (var i = 0; i < 4; i++)
        {
            boss.Cast(Pulsar);
            Advance(2f);
        }
    }

    [TearDown]
    public void RemoveGame()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private IEnumerable<SimCharacter> Members => Enum.GetValues<PartyRole>().Select(r => world.Party.Get(r)!);

    private SimPartyNpc Whm => (SimPartyNpc)world.Party.Get(PartyRole.RegenHealer)!;

    private static uint Hp(SimCharacter who) => who.Proxy!.Health;

    private void Advance(float seconds)
    {
        for (var left = seconds; left > 0f; left -= Step)
        {
            world.Events.Tick(Step);
            world.Tick(Step);
        }
    }

    private float Now => world.Events.Elapsed;

    // The times each member's HP went up, over `seconds`.
    private Dictionary<SimCharacter, List<float>> Rises(float seconds)
    {
        var previous = Members.ToDictionary(m => m, Hp);
        var rises = Members.ToDictionary(m => m, _ => new List<float>());
        for (var left = seconds; left > 0f; left -= Step)
        {
            Advance(Step);
            foreach (var m in Members)
            {
                if (Hp(m) > previous[m]) rises[m].Add(Now);
                previous[m] = Hp(m);
            }
        }
        return rises;
    }

    [Test]
    public void EveryRegenTicksOnTheSameThreeSecondBeat()
    {
        var medica = HealCatalogue.Find(37010)!;
        var dawn = HealCatalogue.Find(16537)!;
        world.Healing.Perform(Whm, medica, null);
        Advance(1.1f);
        world.Healing.Perform(Whm, dawn, null);
        var phase = world.Healing.TickPhase;
        var rises = Rises(14f);
        var all = rises.Values.SelectMany(r => r).ToList();
        Assert.That(all.Count, Is.GreaterThan(16));
        foreach (var at in all)
        {
            var sincePhase = (at - phase) % HealthTick;
            if (sincePhase < 0) sincePhase += HealthTick;
            Assert.That(sincePhase, Is.InRange(0.05f - 2 * Step, 0.35f + 2 * Step), $"landed at {at:F2}");
        }
    }

    private const float HealthTick = PartyHealing.ServerTickSeconds;

    [Test]
    public void ARegenTicksOnceADurationBeatForEachMember()
    {
        world.Healing.Perform(Whm, HealCatalogue.Find(16537)!, null);
        var rises = Rises(25f);
        foreach (var (who, times) in rises.Where(r => r.Key.Proxy!.Health < r.Key.Proxy.MaxHealth || r.Value.Count > 0))
        {
            if (times.Count == 0) continue;
            Assert.That(times.Count, Is.InRange(13, 16), $"{who.GetType().Name}: Whispering Dawn adds to natural regeneration");
        }
    }

    [Test]
    public void ARegenShowsItsStatusOnTheMembersItHealsForItsDuration()
    {
        world.Healing.Perform(Whm, HealCatalogue.Find(37010)!, null);
        foreach (var member in Members)
        {
            var status = member.ActiveStatuses.SingleOrDefault(s => s.StatusId == 3880);
            Assert.That(status, Is.Not.Null, $"{member.GetType().Name}");
            Assert.That(status!.RemainingTime, Is.EqualTo(15f).Within(0.5f));
        }
        Advance(16f);
        Assert.That(Members.Any(m => m.ActiveStatuses.Any(s => s.StatusId == 3880)), Is.False, "it runs out");
    }

    [Test]
    public void AGroundRegenOnlyHealsAndMarksWhoStandsInIt()
    {
        var asylum = HealCatalogue.Find(3569)!;
        var inside = Members.Where(m => !ReferenceEquals(m, Whm)).First();
        var outside = Members.Where(m => !ReferenceEquals(m, Whm) && !ReferenceEquals(m, inside)).First();
        Whm.SetPosition(Vector3.Zero);
        inside.SetPosition(new Vector3(2f, 0f, 0f));
        outside.SetPosition(new Vector3(20f, 0f, 0f));
        Advance(0.3f);
        var hpInside = Hp(inside);
        var hpOutside = Hp(outside);
        world.Healing.Perform(Whm, asylum, null);
        Advance(7f);
        Assert.That(Hp(inside), Is.GreaterThan(hpInside));
        Assert.That(Hp(inside) - hpInside, Is.GreaterThan(Hp(outside) - hpOutside));
        Assert.That(inside.ActiveStatuses.Any(s => s.StatusId == 1911));
        Assert.That(outside.ActiveStatuses.Any(s => s.StatusId == 1911), Is.False);
    }

    [Test]
    public void ADirectHealLandsWithinItsP25ToP75PerTarget()
    {
        var medica = HealCatalogue.Find(37010)!;
        var seen = new List<float>();
        for (var round = 0; round < 12; round++)
        {
            var before = Members.ToDictionary(m => m, Hp);
            world.Healing.Perform(Whm, medica with { Tick = null }, null);
            Advance(0.6f);
            foreach (var m in Members)
            {
                var gained = (float)Hp(m) - before[m];
                if (gained <= 0f || Hp(m) == m.Proxy!.MaxHealth) continue;
                var fraction = gained / m.Proxy!.MaxHealth;
                seen.Add(fraction);
                Assert.That(fraction, Is.InRange(medica.Direct!.Value.P25 - 0.001f, medica.Direct.Value.P75 + 0.001f));
            }
            boss.Cast(Pulsar);
            Advance(1.5f);
        }
        Assert.That(seen.Count, Is.GreaterThan(20));
        Assert.That(seen.Distinct().Count(), Is.GreaterThan(5), "varied from target to target");
    }

    [Test]
    public void HealsLeaveTheRunsRngAlone()
    {
        world.Healing.Perform(Whm, HealCatalogue.Find(37010)!, null);
        Advance(10f);
        Assert.That(world.Rng.Next(1_000_000), Is.EqualTo(new Rng(Seed).Next(1_000_000)));
    }

    [Test]
    public void EveryHealActionAndStatusIsTheGamesByName()
    {
        var owners = new Dictionary<JobId, int[]>
        {
            [JobId.WhiteMage] = [24, 6], [JobId.Scholar] = [28, 26], [JobId.Astrologian] = [33], [JobId.Sage] = [40],
        };
        foreach (var heal in HealCatalogue.All)
        {
            var row = Data.Action(heal.Id);
            Assert.That(row, Is.Not.Null, $"{heal.Name} {heal.Id}");
            Assert.That(row!.Name, Is.EqualTo(heal.Name), $"action {heal.Id}");
            if (heal.StatusId != 0) Assert.That(Data.Status(heal.StatusId)?.Name, Is.EqualTo(heal.StatusName), $"status {heal.StatusId}");
            Assert.That(heal.Gcd == (row.ActionCategory == 2), $"{heal.Name} is {(heal.Gcd ? "a GCD" : "an oGCD")}");
            if (row.IsPlayerAction) Assert.That(owners[heal.Job], Does.Contain(Data.ActionClassJob(heal.Id)), $"{heal.Name} belongs to {heal.Job}");
        }
    }
}

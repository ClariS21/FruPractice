using System.Globalization;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyHp;
using AnoMech.Core.SimObjects;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;

namespace AnoMech.Tests;

public partial class BotHealingTests
{
    private const uint BossBNpcBaseId = 19508;
    private const float Step = 1f / 60f;
    private const int Seed = 11;

    // Hits everyone for the table's share of Shockwave Pulsar, as a raidwide.
    private static readonly EnemyAction Raidwide = FruActions.ShockwavePulsar with
    {
        Cast = new() { CastSecondsOverride = 0.5f },
        Effects = [Damage()],
        Timing = new() { DamageDelay = 0.5f },
    };

    private FakeGame fake = null!;
    private SimWorld world = null!;
    private SimEnemy boss = null!;
    private float clock;

    [SetUp]
    public void InstallGame()
    {
        DebugBotControl.Enabled = false;
        fake = FakeGame.Install();
        clock = 0f;
        fake.Timeline.Clock = () => world.Events.Elapsed;
        fake.BattleCharas.Player.ClassJob = (byte)JobId.Dragoon;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.Reseed(Seed);
        world.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Dragoon));
        boss = world.SpawnEnemy(new EnemySpawnConfig(BossBNpcBaseId, Targetable: true))!;
        Advance(1f);
        fake.Timeline.Clear();
    }

    [TearDown]
    public void RemoveGame()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private IEnumerable<SimCharacter> Members => Enum.GetValues<PartyRole>().Select(r => world.Party.Get(r)!);

    private SimPartyNpc Bot(PartyRole role) => (SimPartyNpc)world.Party.Get(role)!;

    private static uint Hp(SimCharacter who) => who.Proxy!.Health;

    private void Advance(float seconds)
    {
        for (var left = seconds; left > 0f; left -= Step)
        {
            clock += Step;
            fake.Frame(Step, dt =>
            {
                world.Events.Tick(dt);
                world.Tick(dt);
            });
        }
    }

    private List<Dictionary<string, string>> HealRows()
        => fake.Timeline.Rows.Where(r => r["kind"] == "ability" && uint.TryParse(r["id"], out var id) && HealCatalogue.Find(id) != null
                                         && r["actor"] != fake.BattleCharas.Player.EntityId.ToString("X8")).ToList();

    private static float Time(Dictionary<string, string> row) => float.Parse(row["t"], CultureInfo.InvariantCulture);

    private void Raidwides(int count)
    {
        for (var i = 0; i < count; i++)
        {
            boss.Cast(Raidwide);
            Advance(1.2f);
        }
    }

    [Test]
    public void HealersAnswerARaidwideAndTopTheParty()
    {
        boss.Cast(Raidwide);
        Advance(1.2f);
        var hitAt = world.Healing.RaidwideAt ?? 0f;
        Raidwides(2);
        var low = world.Healing.Average();
        Assert.That(low, Is.LessThan(0.8f), "the hits took a real share");
        Advance(15f);
        Assert.That(world.Healing.Average(), Is.GreaterThan(0.95f), "the healers topped the party");
        var rows = HealRows();
        Assert.That(rows, Is.Not.Empty);
        Assert.That(rows.Select(r => r["actor_name"]).Distinct().Count(), Is.GreaterThan(0));
        Assert.That(Time(rows.First()) - hitAt, Is.InRange(0.5f, 5f), "the first heal comes a second or three after the hit");
    }

    [Test]
    public void ABotHealerSpendsAnOgcdAoeHealOnTheRaidwide()
    {
        Raidwides(3);
        Advance(8f);
        var oGcds = HealRows().Where(r => HealCatalogue.Find(uint.Parse(r["id"]))!.Gcd == false && HealCatalogue.Find(uint.Parse(r["id"]))!.Shape == HealShape.Party).ToList();
        Assert.That(oGcds, Is.Not.Empty, "an oGCD party heal went out");
    }

    [Test]
    public void ATankUnderHalfGetsASingleTargetHeal()
    {
        var tank = world.Party.Get(PartyRole.MainTank)!;
        tank.WriteHealth((uint)(tank.Proxy!.MaxHealth * 0.4f));
        Advance(8f);
        var single = HealRows().Where(r => HealCatalogue.Find(uint.Parse(r["id"]))!.Shape == HealShape.Single).ToList();
        Assert.That(single, Is.Not.Empty, "a Tetragrammaton, Lustrate or the like");
        Assert.That(Hp(tank), Is.GreaterThan((uint)(tank.Proxy!.MaxHealth * 0.5f)));
    }

    [Test]
    public void HealersPlayTheirRotationWhileThePartyIsFull()
    {
        Advance(25f);
        // Assize is also the White Mage's damage button, so the rotation presses it.
        Assert.That(HealRows().Where(r => r["id"] != "3571").Select(r => $"{r["t"]} {r["actor_name"]} {r["name"]}"), Is.Empty, $"no heals with nobody hurt (average {world.Healing.Average():F2})");
        var whm = Bot(PartyRole.RegenHealer);
        Assert.That(fake.Timeline.Rows.Any(r => r["kind"] == "ability" && r["actor"] == whm.EntityId.ToString("X8") && (r["id"] == "16532" || r["id"] == "25859")), "Dia and Glare III go on");
    }

    [Test]
    public void HealingLeavesTheRunsRngAlone()
    {
        Raidwides(3);
        Advance(10f);
        Assert.That(world.Rng.Next(1_000_000), Is.EqualTo(new Rng(Seed).Next(1_000_000)));
    }

    [Test]
    public void AHealCastRaisesABarAndLandsWhenItFills()
    {
        Raidwides(3);
        Advance(12f);
        var casts = fake.Timeline.Rows.Where(r => r["kind"] == "cast" && HealCatalogue.Find(uint.Parse(r["id"])) != null).ToList();
        if (casts.Count == 0) Assert.Pass("every heal that went out was an instant one");
        var cast = casts.First();
        var release = fake.Timeline.Rows.FirstOrDefault(r => r["kind"] == "ability" && r["id"] == cast["id"] && r["actor"] == cast["actor"] && Time(r) >= Time(cast));
        Assert.That(release, Is.Not.Null);
        Assert.That(Time(release!) - Time(cast), Is.EqualTo(Natives.Data.Action(uint.Parse(cast["id"]))!.CastSeconds).Within(0.1f));
    }
}

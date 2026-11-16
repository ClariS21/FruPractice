using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;

namespace AnoMech.Tests;

public class BotCombatTests
{
    private const uint BossBNpcBaseId = 19508;
    private const ushort StunStatusId = 2;
    private const uint ShakeItOff = 7388;
    private const float Frame = 1f / 60f;

    private FakeGame fake = null!;
    private SimWorld world = null!;
    private SimEnemy? boss;
    private float clock;

    [SetUp]
    public void InstallGame() => Install(1);

    [TearDown]
    public void RemoveGame()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private void Install(int seed)
    {
        fake = FakeGame.Install();
        clock = 0f;
        fake.Timeline.Clock = () => clock;
        fake.BattleCharas.Player.ClassJob = (byte)JobId.Sage;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.Reseed(seed);
        world.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
        boss = world.SpawnEnemy(new EnemySpawnConfig(BossBNpcBaseId, Targetable: true));
        Step(1f);
        fake.Timeline.Clear();
    }

    private void Step(float seconds)
    {
        for (var t = 0f; t < seconds; t += Frame)
        {
            clock += Frame;
            fake.Frame(Frame, dt =>
            {
                world.Events.Tick(dt);
                world.Tick(dt);
            });
        }
    }

    private SimPartyNpc Bot(JobId job) => world.Party.AllMembers().OfType<SimPartyNpc>().Single(b => b.ClassJob == (byte)job);

    private List<SimPartyNpc> Bots() => world.Party.AllMembers().OfType<SimPartyNpc>().ToList();

    private List<Dictionary<string, string>> Abilities(SimCharacter bot)
        => fake.Timeline.Rows.Where(r => r["kind"] == "ability" && r["actor"] == bot.EntityId.ToString("X8")).ToList();

    private static float Time(Dictionary<string, string> row) => float.Parse(row["t"], CultureInfo.InvariantCulture);

    private static bool IsAuto(Dictionary<string, string> row) => row["id"] is "7" or "8";

    private List<string> RunSequence(int seed)
    {
        RemoveGame();
        Install(seed);
        Step(20f);
        return fake.Timeline.Rows.Where(r => r["kind"] == "ability").Select(r => $"{r["t"]} {r["actor"]} {r["id"]} {r["anim_var"]}").ToList();
    }

    [Test]
    public void ASeedReplaysTheSameEffects()
    {
        var first = RunSequence(77);
        var second = RunSequence(77);
        var other = RunSequence(78);
        Assert.That(first, Is.Not.Empty);
        Assert.That(second, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
    }

    [Test]
    public void BotsAttackTheBossWithoutDamageOrTurning()
    {
        var bots = Bots();
        var rotations = bots.ToDictionary(b => b, b => b.Rotation);
        Step(20f);
        foreach (var bot in bots)
        {
            var rows = Abilities(bot);
            Assert.That(rows, Is.Not.Empty, bot.DisplayName);
            Assert.That(bot.Rotation, Is.EqualTo(rotations[bot]), $"{bot.DisplayName} was turned");
            foreach (var row in rows)
            {
                Assert.That(row["damage_type"], Is.Empty);
                Assert.That(row["knockback"], Is.Empty);
                // A heal (the White Mage's Assize) goes toward the party it heals.
                if (AnoMech.Core.Game.PartyHp.HealCatalogue.Find(uint.Parse(row["id"])) == null)
                    Assert.That(row["target"], Is.EqualTo(boss!.EntityId.ToString("X8")));
                Assert.That(row["rot"], Is.EqualTo(rotations[bot].ToString("F2", CultureInfo.InvariantCulture)));
            }
        }
        var botIds = bots.Select(b => b.EntityId.ToString("X8")).ToList();
        Assert.That(fake.Timeline.Rows.Where(r => (r["kind"] == "set_pos" || r["kind"] == "move") && botIds.Contains(r["actor"])), Is.Empty);
    }

    [Test]
    public void AutoAttacksComeEveryThreeSecondsAndGcdsEveryTwoAndAHalf()
    {
        var bard = Bot(JobId.Bard);
        var gcdIds = new[] { 16495u, 7406u, 7407u, 7409u, 3560u, 16496u, 36977u }.Select(id => id.ToString()).ToList();
        Step(40f);
        var rows = Abilities(bard);
        var autos = rows.Where(IsAuto).Select(Time).ToList();
        var gcds = rows.Where(r => gcdIds.Contains(r["id"])).Select(Time).ToList();
        Assert.That(autos.Count, Is.GreaterThan(10));
        Assert.That(gcds.Count, Is.GreaterThan(12));
        Assert.That(rows.Where(IsAuto).Select(r => r["id"]).Distinct(), Is.EqualTo(new[] { "8" }), "bards shoot");
        for (var i = 1; i < autos.Count; i++) Assert.That(autos[i] - autos[i - 1], Is.InRange(2.3f, 3.7f), "a swing waits out the bot's animation lock");
        Assert.That((autos[^1] - autos[0]) / (autos.Count - 1), Is.EqualTo(3f).Within(0.05f));
        for (var i = 1; i < gcds.Count; i++) Assert.That(gcds[i] - gcds[i - 1], Is.InRange(2.45f, 2.75f));
        Assert.That(rows.Count(r => !IsAuto(r) && !gcdIds.Contains(r["id"])), Is.GreaterThan(0), "some weave in an oGCD");
    }

    [Test]
    public void BotsDoNotSwingInUnison()
    {
        Step(6f);
        var starts = Bots().Select(b => Time(Abilities(b).First())).Distinct().ToList();
        Assert.That(starts.Count, Is.GreaterThan(3));
    }

    [Test]
    public void ADeadBotDoesNotFight()
    {
        var bard = Bot(JobId.Bard);
        Step(5f);
        bard.Die(0);
        var before = Abilities(bard).Count;
        Step(10f);
        Assert.That(Abilities(bard).Count, Is.EqualTo(before));
    }

    [Test]
    public void AStunnedBotDoesNotFight()
    {
        var bard = Bot(JobId.Bard);
        Step(5f);
        bard.AddStatus(StunStatusId, 8f);
        var before = Abilities(bard).Count;
        Step(7f);
        Assert.That(Abilities(bard).Count, Is.EqualTo(before));
        Step(5f);
        Assert.That(Abilities(bard).Count, Is.GreaterThan(before), "back to it once the stun ends");
    }

    [Test]
    public void ABotOutOfRangeDoesNotFight()
    {
        var bard = Bot(JobId.Bard);
        var dragoon = Bot(JobId.Dragoon);
        bard.SetPosition(new Vector3(40f, 0f, 0f));
        dragoon.SetPosition(new Vector3(40f, 0f, 5f));
        Step(15f);
        Assert.That(Abilities(bard), Is.Empty);
        Assert.That(Abilities(dragoon), Is.Empty, "melee needs to be at the boss");
        Assert.That(Abilities(Bot(JobId.WhiteMage)), Is.Not.Empty);
    }

    [Test]
    public void NoTargetableBossMeansNoCombat()
    {
        boss!.SetTargetable(false);
        Step(10f);
        Assert.That(fake.Timeline.Rows.Where(r => r["kind"] == "ability"), Is.Empty);
    }

    [Test]
    public void TheWorldFlagTurnsCombatOff()
    {
        world.BotCombatEnabled = false;
        Step(10f);
        Assert.That(fake.Timeline.Rows.Where(r => r["kind"] == "ability"), Is.Empty);
    }

    [Test]
    public void APressOfTheBotsOwnHoldsItsCombat()
    {
        var tank = Bot(JobId.Warrior);
        Step(3f);
        tank.UseAction(ShakeItOff);
        var pressedAt = fake.Timeline.Rows.Count;
        Step(1f);
        Assert.That(fake.Timeline.Rows.Skip(pressedAt).Count(r => r["kind"] == "ability" && r["actor"] == tank.EntityId.ToString("X8")), Is.Zero);
    }
    private List<Dictionary<string, string>> Rows(string kind, SimCharacter bot)
        => fake.Timeline.Rows.Where(r => r["kind"] == kind && r["actor"] == bot.EntityId.ToString("X8")).ToList();

    private static readonly Vector3 FarAway = new(30f, 0f, 30f);

    [Test]
    public void ACastTimeGcdRaisesABarAndReleasesWhenItFills()
    {
        var whm = Bot(JobId.WhiteMage);
        Step(30f);
        var casts = Rows("cast", whm);
        Assert.That(casts, Is.Not.Empty);
        var glare = casts.First(c => c["id"] == "25859");
        var release = Abilities(whm).First(a => a["id"] == "25859" && Time(a) >= Time(glare));
        Assert.That(Time(release) - Time(glare), Is.EqualTo(1.5f).Within(0.05f));
        Assert.That(Abilities(whm).Count(a => a["id"] == "25859"), Is.EqualTo(casts.Count(c => c["id"] == "25859")), "each bar is released");
    }

    [Test]
    public void NoCastStartsWithAStratMoveDueBeforeItEnds()
    {
        var whm = Bot(JobId.WhiteMage);
        var due = clock + 12f;
        world.Departures.Add(world.Events.Elapsed + 12f);
        Step(20f);
        var casts = Rows("cast", whm).Select(Time).ToList();
        Assert.That(casts, Is.Not.Empty);
        Assert.That(casts.Where(t => t > due - 1.5f && t < due), Is.Empty);
        Assert.That(casts.Any(t => t > due), "casting resumes after the move");
    }

    [Test]
    public void NoCastStartsWithAPlannedPressDueBeforeItEndsPlusABeat()
    {
        var whm = Bot(JobId.WhiteMage);
        world.MitPractice.Enabled = true;
        var source = new MitSource("Boss");
        var profile = new MitProfile(233000f, 162000f, 146500f, 0.62f, 1f, 0.95f, 0f, 4f);
        var pressAt = world.Events.Elapsed + 1.5f;
        world.MitPractice.Begin(new MitPlanData(profile, [new("hit", "Hit", 1000f, source)], [new(pressAt + 1f, ["hit"])],
            [new MitPlanEntry(SheetColumn.WHM, MitActionId.Temperance, pressAt, "Hit", [pressAt + 1f])], source));
        fake.Timeline.Clear();
        var startedAt = clock;
        Step(1.5f);
        var casts = Rows("cast", whm).Select(Time).Where(t => t >= startedAt).ToList();
        Assert.That(casts, Is.Empty, "a 1.5s bar and a beat don't fit in 1.5s");
        Step(1f);
        Assert.That(whm.HasStatus(1873) || Abilities(whm).Any(a => a["id"] == "16536"), "the press went out");
    }

    [Test]
    public void ABotThatStartsMovingMidCastCancelsIt()
    {
        var whm = Bot(JobId.WhiteMage);
        var seen = 0;
        for (var i = 0; i < 60 * 30 && seen == 0; i++)
        {
            Step(Frame);
            seen = Rows("cast", whm).Count;
        }
        Assert.That(seen, Is.GreaterThan(0));
        var releasesBefore = Abilities(whm).Count;
        whm.MoveTo(FarAway);
        Step(2f);
        Assert.That(Rows("ac", whm).Any(r => r["id"] == "0xF"), "the bar is cancelled");
        Assert.That(Abilities(whm).Count(a => a["id"] == "25859"), Is.LessThan(Rows("cast", whm).Count(c => c["id"] == "25859")));
        Assert.That(Rows("cast", whm).Count, Is.EqualTo(seen), "and no new bar while moving");
        Assert.That(releasesBefore, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void ABarIsCancelledWhenTheTargetStopsBeingFoughtAndNoneStartsAfter()
    {
        var whm = Bot(JobId.WhiteMage);
        var seen = 0;
        for (var i = 0; i < 60 * 30 && seen == 0; i++)
        {
            Step(Frame);
            seen = Rows("cast", whm).Count;
        }
        boss!.SetTargetable(false);
        Step(0.1f);
        Assert.That(Rows("ac", whm).Any(r => r["id"] == "0xF"), "the bar is cancelled");
        Step(10f);
        Assert.That(Rows("cast", whm).Count, Is.EqualTo(seen));
    }

    [Test]
    public void AGapCloserPutsAMeleeBotAtTheBossAndStaysInTheArena()
    {
        world.EnforceArenaBoundary(20f);
        var dragoon = Bot(JobId.Dragoon);
        dragoon.SetPosition(new Vector3(0f, 0f, 14f));
        Step(0.2f);
        Assert.That(dragoon.GapClose(boss!), Is.True);
        Step(1.5f);
        var distance = new Vector3(dragoon.Position.X - boss!.Position.X, 0f, dragoon.Position.Z - boss.Position.Z).Length();
        Assert.That(distance, Is.LessThanOrEqualTo(boss.HitboxRadius + 3.5f));
        Assert.That(world.IsOutsideArena(dragoon.Position), Is.False);
        Assert.That(Abilities(dragoon).Any(a => a["id"] == "36951"), "Winged Glide plays");
        Assert.That(dragoon.GapClose(boss), Is.False, "on cooldown");
    }

    [Test]
    public void AJobWithoutAGapCloserDoesNotDash()
    {
        Assert.That(Bot(JobId.Bard).GapClose(boss!), Is.False);
    }

    [Test]
    public void AHaltedRunCancelsBarsInProgress()
    {
        var whm = Bot(JobId.WhiteMage);
        var seen = 0;
        for (var i = 0; i < 60 * 30 && seen == 0; i++)
        {
            Step(Frame);
            seen = Rows("cast", whm).Count;
        }
        world.BotCombatHalted = true;
        Step(0.1f);
        Assert.That(Rows("ac", whm).Any(r => r["id"] == "0xF"), "the bar is cancelled");
        Step(10f);
        Assert.That(Rows("cast", whm).Count, Is.EqualTo(seen));
    }
}

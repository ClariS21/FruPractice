using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Core.Game.Ai;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

public class BotBuffsTests
{
    private const uint BossBNpcBaseId = 19508;
    private const float Frame = 1f / 60f;
    private static readonly DataminingGameData Data = new(new FakeRsvFunctions());

    private FakeGame fake = null!;
    private SimWorld world = null!;
    private SimEnemy boss = null!;
    private float clock;

    // A scenario that only has anchors, so the bots' burst windows open.
    private sealed class AnchoredScenario(params BurstAnchor[] anchors) : IScenario
    {
        public string Name => "anchored";
        public IPhase Phase => null!;
        public IReadOnlyList<IScenarioAi> AiStrats => [];
        public IReadOnlyList<BurstAnchor> BurstAnchors => anchors;
        public void Run(SimWorld world, int? selectedAi) { }
    }

    [SetUp]
    public void InstallGame()
    {
        fake = FakeGame.Install();
        clock = 0f;
        fake.Timeline.Clock = () => clock;
        fake.BattleCharas.Player.ClassJob = (byte)JobId.Sage;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.Reseed(3);
        world.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
        boss = world.SpawnEnemy(new EnemySpawnConfig(BossBNpcBaseId, Targetable: true))!;
        Step(1f);
    }

    [TearDown]
    public void RemoveGame()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
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

    private static bool Has(SimCharacter who, ushort statusId) => who.ActiveStatuses.Any(s => s.StatusId == statusId);

    private SimPartyNpc Bot(JobId job) => world.Party.AllMembers().OfType<SimPartyNpc>().Single(b => b.ClassJob == (byte)job);

    [Test]
    public void EveryStatusIsTheGamesByName()
    {
        foreach (var (actionId, buffs) in BotBuffs.All)
        {
            Assert.That(Data.Action(actionId), Is.Not.Null, $"action {actionId}");
            foreach (var buff in buffs)
            {
                Assert.That(Data.Status(buff.StatusId)?.Name, Is.EqualTo(buff.StatusName), $"status {buff.StatusId} from {Data.Action(actionId)!.Name}");
                Assert.That(buff.Seconds, Is.GreaterThanOrEqualTo(0f));
            }
        }
    }

    [Test]
    public void ADotLastsAsLongAsTheScriptKeepsItUp()
    {
        foreach (var id in new uint[] { 16532, 16540, 16554, 24314, 36986, 16153 })
            Assert.That(BotBuffs.All[id].Single().Seconds, Is.EqualTo(BotScripts.DotSeconds), Data.Action(id)!.Name);
        foreach (var id in new uint[] { 7407, 7406 })
            Assert.That(BotBuffs.All[id].Single().Seconds, Is.EqualTo(BotScripts.BardDotSeconds));
    }

    // Burst actions whose own buff is not on the status list this project shows.
    private static readonly uint[] NoBuff = [37033, 16472, 107, 16164, 36921];

    [Test]
    public void EveryBurstActionGrantsItsStatusUnlessItIsListedWithout()
    {
        foreach (var (job, script) in BotScripts.All)
            foreach (var id in script.Burst)
                Assert.That(BotBuffs.All.ContainsKey(id) || NoBuff.Contains(id), $"{job} {Data.Action(id)?.Name} ({id})");
    }

    [Test]
    public void ASelfBuffLandsOnTheCasterForItsDuration()
    {
        var warrior = Bot(JobId.Warrior);
        BotBuffs.Apply(7389, warrior, boss, world.Party);
        var inner = warrior.ActiveStatuses.Single(s => s.StatusId == 1177);
        Assert.That(inner.RemainingTime, Is.EqualTo(15f).Within(0.5f));
        Assert.That(inner.Stacks, Is.EqualTo(3));
        Assert.That(Bot(JobId.Paladin).HasStatus(1177), Is.False);
    }

    [Test]
    public void APartyBuffLandsOnEveryoneWithinThirtyYardsPlayerIncluded()
    {
        var dragoon = Bot(JobId.Dragoon);
        var far = Bot(JobId.BlackMage);
        far.SetPosition(new Vector3(40f, 0f, 0f));
        dragoon.SetPosition(Vector3.Zero);
        Step(0.3f);
        BotBuffs.Apply(3557, dragoon, boss, world.Party);
        foreach (var member in world.Party.AllMembers().Where(m => !ReferenceEquals(m, far)))
        {
            Assert.That(Has(member, 786), member.GetType().Name);
            Assert.That(member.ActiveStatuses.Single(s => s.StatusId == 786).RemainingTime, Is.EqualTo(20f).Within(0.5f));
        }
        Assert.That(Has(far, 786), Is.False, "out of range");
        Assert.That(Has(world.Party.Player!, 786), "the player is in the party");
    }

    [Test]
    public void ADotGoesOnTheBossAndIronJawsRefreshesBoth()
    {
        var bard = Bot(JobId.Bard);
        BotBuffs.Apply(7407, bard, boss, world.Party);
        BotBuffs.Apply(7406, bard, boss, world.Party);
        Step(20f);
        Assert.That(boss.ActiveStatuses.Single(s => s.StatusId == 1201).RemainingTime, Is.EqualTo(25f).Within(1f));
        BotBuffs.Apply(3560, bard, boss, world.Party);
        foreach (var id in new ushort[] { 1201, 1200 })
            Assert.That(boss.ActiveStatuses.Single(s => s.StatusId == id).RemainingTime, Is.EqualTo(45f).Within(0.5f));
    }

    [Test]
    public void AMonkHoldsOneFormAtATime()
    {
        var monk = Bot(JobId.Monk);
        BotBuffs.Apply(36945, monk, boss, world.Party);
        Assert.That(monk.ActiveStatuses.Select(s => s.StatusId).Where(id => id is 107 or 108 or 109), Is.EqualTo(new ushort[] { 108 }));
        BotBuffs.Apply(36946, monk, boss, world.Party);
        Assert.That(monk.ActiveStatuses.Select(s => s.StatusId).Where(id => id is 107 or 108 or 109), Is.EqualTo(new ushort[] { 109 }));
    }

    [Test]
    public void RequiescatIsSpentOneStackAtATime()
    {
        var paladin = Bot(JobId.Paladin);
        BotBuffs.Apply(36921, paladin, boss, world.Party);
        Assert.That(paladin.ActiveStatuses.Single(s => s.StatusId == 1368).Stacks, Is.EqualTo(4));
        BotBuffs.Apply(16459, paladin, boss, world.Party);
        BotBuffs.Apply(7384, paladin, boss, world.Party);
        Assert.That(paladin.ActiveStatuses.Single(s => s.StatusId == 1368).Stacks, Is.EqualTo(2));
    }

    [Test]
    public void KardionGoesOnTheMainTankSeat()
    {
        BotBuffs.Apply(24285, Bot(JobId.WhiteMage), boss, world.Party);
        Assert.That(Has(world.Party.Get(PartyRole.MainTank)!, 2605));
        Assert.That(Has(Bot(JobId.WhiteMage), 2604), "the Sage's own Kardia");
    }

    [Test]
    public void TheRotationKeepsItsDotUpOnTheBossAndHaltingTakesItOff()
    {
        world.SimEvents.Listener = new AnchoredScenario();
        Step(40f);
        Assert.That(Has(boss, 1871), "White Mage Dia");
        Assert.That(boss.ActiveStatuses.Single(s => s.StatusId == 1871).RemainingTime, Is.GreaterThan(0f));
        world.BotCombatHalted = true;
        Step(0.2f);
        Assert.That(world.Children.OfType<SimEnemy>().SelectMany(e => e.ActiveStatuses).Select(s => s.StatusId).Intersect(new ushort[] { 1871, 1881, 1200, 1201, 3871 }), Is.Empty);
        Assert.That(Has(Bot(JobId.Warrior), 2677) || Has(Bot(JobId.Warrior), 1177), Is.False, "self buffs come off too");
    }

    [Test]
    public void AChainStratagemDebuffGoesOnTheBoss()
    {
        BotBuffs.Apply(7436, Bot(JobId.WhiteMage), boss, world.Party);
        Assert.That(Has(boss, 1221));
        Assert.That(boss.ActiveStatuses.Single(s => s.StatusId == 1221).RemainingTime, Is.EqualTo(20f).Within(0.5f));
        Assert.That(Has(Bot(JobId.WhiteMage), 1221), Is.False);
    }

    [Test]
    public void BurstsInAWindowPutTheirBuffsUpOnTheRealStatusList()
    {
        world.SimEvents.Listener = new AnchoredScenario(new BurstAnchor(40265u, 1f), new BurstAnchor(40286u, 70f));
        world.BotCombatEnabled = true;
        var player = world.Party.Player!;
        Step(30f);
        Assert.That(Bot(JobId.Warrior).HasStatus(1177) || fake.Timeline.Rows.Any(r => r["kind"] == "ability" && r["id"] == "7389"), "Inner Release played in the first window");
        Step(50f);
        Assert.That(Has(player, 1185) || Has(player, 786) || Has(player, 2964) || Has(player, 141) || Has(player, 1878),
            "a raid window leaves a party buff on the player");
    }
}

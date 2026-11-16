using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru;

namespace AnoMech.Tests;

public class MitPracticeTests
{
    private const uint ChaosP3BNpcBaseId = 19508;
    private const uint ExdeathBNpcBaseId = 19509;
    private static readonly MitProfile Profile = new(325200f, 226600f, 205200f, 0.61f, 1f, 0.92f, 0.69f, 3f);
    private static readonly MitSource Kefka = new("Kefka");

    private FakeGame fake = null!;
    private SimWorld world = null!;

    [SetUp]
    public void InstallGame()
    {
        fake = FakeGame.Install();
        fake.BattleCharas.Player.ClassJob = (byte)JobId.Sage;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.BotCombatEnabled = false;
        world.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
        world.MitPractice.Enabled = true;
    }

    [TearDown]
    public void RemoveGame()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private static MitPlanData Data(params MitPlanEntry[] entries) => new(Profile,
        [new("hit", "Big Hit", 100000f, Kefka), new("unknown", "Mystery", null, Kefka)],
        [new(5f, ["hit"])], entries, Kefka);

    private void Step(float seconds)
    {
        world.Events.Tick(seconds);
        world.Tick(seconds);
    }

    private SimCharacter Slot(PartyRole role) => world.Party.Get(role)!;

    // One HP point of the character's own max: WriteHp rounds to whole HP.
    private const float ShareTolerance = 1e-4f;

    private float HpShare(PartyRole role) => (float)Slot(role).Proxy!.Health / Slot(role).Proxy!.MaxHealth;

    [Test]
    public void BotsPressThePlanWhenItSays()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.OT, MitPlanEntry.PartyMit, 1f, "Big Hit", [5f])));
        Step(0.5f);
        Assert.That(Slot(PartyRole.MeleeDpsA).HasStatus(1362), Is.False);
        Step(0.6f);
        Assert.That(Slot(PartyRole.MeleeDpsA).HasStatus(1362), "the bot PLD's Divine Veil reaches the party");
    }

    [Test]
    public void ABotSkipsAPressInsideItsRecast()
    {
        world.MitPractice.Begin(Data(
            new MitPlanEntry(SheetColumn.OT, MitPlanEntry.PartyMit, 1f, "Big Hit", [5f]),
            new MitPlanEntry(SheetColumn.OT, MitPlanEntry.PartyMit, 20f, "Again", [25f])));
        Step(1.1f);
        Slot(PartyRole.MeleeDpsA).RemoveStatus(1362);
        Step(19.5f);
        Assert.That(Slot(PartyRole.MeleeDpsA).HasStatus(1362), Is.False, "Divine Veil is a 90s recast");
    }

    [Test]
    public void APressPlannedBeforeTheStartIsAppliedForThePlayer()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.SGE, MitActionId.Holos, -6.5f, "Big Hit", [5f])));
        Step(0.1f);
        Assert.That(world.Party.Player!.HasStatus(3003), "the player's Sage had no chance to press Holos");
    }

    [Test]
    public void ADebuffGoesOnTheBossItsRowNames()
    {
        var chaos = world.SpawnEnemy(new EnemySpawnConfig(ChaosP3BNpcBaseId))!;
        var exdeath = world.SpawnEnemy(new EnemySpawnConfig(ExdeathBNpcBaseId))!;
        var chaosSource = new MitSource("Chaos");
        var exdeathSource = new MitSource("Exdeath");
        world.MitPractice.Begin(new MitPlanData(Profile, [new("hit", "Big Hit", null, exdeathSource)], [new(5f, ["hit"])],
                [new MitPlanEntry(SheetColumn.D4, MitActionId.Addle, 1f, "Big Hit", [5f], On: exdeathSource)], chaosSource),
            new Dictionary<MitSource, Func<SimCharacter?>> { [chaosSource] = () => chaos, [exdeathSource] = () => exdeath });
        Step(1.1f);
        Assert.That(exdeath.HasStatus(1203), "the row names Exdeath");
        Assert.That(chaos.HasStatus(1203), Is.False, "though the plan's debuffs go on Chaos");
    }

    [Test]
    public void BeginStartsEveryoneOnFullHp()
    {
        world.MitPractice.Begin(Data());
        Assert.That(HpShare(PartyRole.MainTank), Is.EqualTo(1f));
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.EqualTo(1f));
        Assert.That(HpShare(PartyRole.ShieldHealer), Is.EqualTo(1f), "the player's Sage");
    }

    // The bar shows each class's pool as a share of the character's own max HP.
    [Test]
    public void AHitTakesEachClassItsShare()
    {
        world.MitPractice.Begin(Data());
        world.MitPractice.HitParty("hit");
        Step(0.4f);
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.EqualTo((226600f - 100000f) / 226600f).Within(ShareTolerance));
        Assert.That(HpShare(PartyRole.MainTank), Is.EqualTo((325200f - 61000f) / 325200f).Within(ShareTolerance));
        Assert.That(HpShare(PartyRole.ShieldHealer), Is.EqualTo((205200f - 92000f) / 205200f).Within(ShareTolerance));
    }

    [Test]
    public void ABarrierAbsorbsAndIsUsedUp()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.OT, MitPlanEntry.PartyMit, 1f, "Big Hit", [5f])));
        Step(1.1f);
        world.MitPractice.Hit(Slot(PartyRole.MeleeDpsA), "hit");
        Step(0.4f);
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.EqualTo((226600f - (100000f - 22660f)) / 226600f).Within(ShareTolerance),
            "Divine Veil is 10% of the target's max HP");
        Assert.That(Slot(PartyRole.MeleeDpsA).HasStatus(1362), Is.False);
    }

    [Test]
    public void NaturalRegenerationAndDirectHealsReachThePracticePool()
    {
        world.MitPractice.Begin(Data());
        world.MitPractice.Hit(Slot(PartyRole.MeleeDpsA), "hit");
        Step(2f);
        var down = HpShare(PartyRole.MeleeDpsA);
        Assert.That(down, Is.LessThan(1f));
        for (var i = 0; i < 80; i++) Step(0.1f);
        var regenerated = HpShare(PartyRole.MeleeDpsA);
        Assert.That(regenerated, Is.InRange(down + 0.02f - ShareTolerance, down + 0.03f + ShareTolerance),
            "each server beat adds 1% to the modelled pool");
        world.Healing.Heal(Slot(PartyRole.MeleeDpsA), 0.1f);
        for (var i = 0; i < 5; i++) Step(0.1f);
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.InRange(regenerated + 0.1f - ShareTolerance * 4, regenerated + 0.11f + ShareTolerance * 4));
    }

    [Test]
    public void AWouldBeKillLeavesASliverAndNobodyDies()
    {
        world.MitPractice.Begin(Data());
        for (var i = 0; i < 3; i++) world.MitPractice.Hit(Slot(PartyRole.MeleeDpsA), "hit");
        Step(0.4f);
        Assert.That(Slot(PartyRole.MeleeDpsA).Proxy!.Health, Is.EqualTo(1u));
        Assert.That(Slot(PartyRole.MeleeDpsA).IsAlive());
    }

    [Test]
    public void AHitWithNoKnownDamageMovesNoHp()
    {
        world.MitPractice.Begin(Data());
        world.MitPractice.HitParty("unknown");
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.EqualTo(1f));
    }

    [Test]
    public void OffDoesNothing()
    {
        world.MitPractice.Enabled = false;
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.OT, MitPlanEntry.PartyMit, 1f, "Big Hit", [5f])));
        Step(1.1f);
        world.MitPractice.HitParty("hit");
        Assert.That(world.MitPractice.IsActive, Is.False);
        Assert.That(Slot(PartyRole.MeleeDpsA).HasStatus(1362), Is.False);
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.EqualTo(1f));
    }

    [Test]
    public void ASoloRunHasNoPractice()
    {
        world.Despawn();
        world.SeatParty(new LocalPartyProvider { Solo = true }.Roster((uint)JobId.Sage));
        world.MitPractice.Begin(Data());
        Assert.That(world.MitPractice.IsActive, Is.False);
    }

    [Test]
    public void TheRunsEndPutsThePlayerBackOnFullHp()
    {
        world.MitPractice.Begin(Data());
        world.MitPractice.Hit(world.Party.Player, "hit");
        Step(0.4f);
        Assert.That(fake.BattleCharas.Player.Health, Is.LessThan(fake.BattleCharas.Player.MaxHealth));
        world.Despawn();
        Assert.That(fake.BattleCharas.Player.Health, Is.EqualTo(fake.BattleCharas.Player.MaxHealth));
    }

    [Test]
    public void ATankBotPressesItsOwnCooldown()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.MT, MitActionId.Rampart, 1f, "Big Hit", [5f])));
        Step(1.1f);
        Assert.That(Slot(PartyRole.MainTank).HasStatus(1191), "Rampart");
        Assert.That(Slot(PartyRole.OffTank).HasStatus(1191), Is.False);
    }

    [Test]
    public void ABuddyRowAimsAtTheOtherTank()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.MT, MitActionId.NascentFlash, 1f, "Big Hit", [5f], Buddy: true)));
        Step(1.1f);
        Assert.That(Slot(PartyRole.OffTank).HasStatus(1858), "Nascent Glint goes on the other tank");
        Assert.That(Slot(PartyRole.MainTank).HasStatus(1858), Is.False);
    }

    [Test]
    public void ADebuffLandsWhereverTheBossStands()
    {
        var chaos = world.SpawnEnemy(new EnemySpawnConfig(ChaosP3BNpcBaseId))!;
        chaos.SetPosition(new Placement(new System.Numerics.Vector3(90f, 0f, 90f), 0f));
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.D1, MitActionId.Feint, 1f, "Big Hit", [5f])),
            new Dictionary<MitSource, Func<SimCharacter?>> { [Kefka] = () => chaos });
        Step(1.1f);
        Assert.That(chaos.HasStatus(1195), "Feint, from 130 yalms away");
    }

    [Test]
    public void TheBellIsAPressWithAStatus()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.WHM, MitActionId.LiturgyOfTheBell, 1f, "Big Hit", [])));
        Step(1.1f);
        Assert.That(Slot(PartyRole.RegenHealer).HasStatus(2709));
    }

    [Test]
    public void HellsJudgmentLeavesEveryoneOnOneHp()
    {
        var plan = new MitPlanData(Profile, [new("judgment", "Judgment", 0f, null, ToOneHp: true)], [new(5f, ["judgment"])], []);
        world.MitPractice.Begin(plan);
        world.MitPractice.HitParty("judgment");
        Step(0.4f);
        Assert.That(Slot(PartyRole.MeleeDpsA).Proxy!.Health, Is.EqualTo(1u));
        Assert.That(Slot(PartyRole.MainTank).Proxy!.Health, Is.EqualTo(1u));
        Step(5f);
        Assert.That(Slot(PartyRole.MeleeDpsA).Proxy!.Health, Is.EqualTo(1u), "and stay there until healed");
    }

    [Test]
    public void ABarMovesAfterTheHitLandsNotWithIt()
    {
        world.MitPractice.Begin(Data());
        world.MitPractice.HitParty("hit");
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.EqualTo(1f));
        Step(0.4f);
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.LessThan(1f));
    }

    [Test]
    public void APlayerUnderBotControlPressesItsPlannedRows()
    {
        var plan = Data(new MitPlanEntry(SheetColumn.SGE, MitActionId.Kerachole, 1f, "Big Hit", [5f]));
        world.MitPractice.Begin(plan);
        Step(1.1f);
        Assert.That(world.Party.Player!.HasStatus(2618), Is.False, "a human presses their own");
        DebugBotControl.Enabled = true;
        try
        {
            world.MitPractice.Begin(plan);
            Step(1.1f);
            Assert.That(world.Party.Player!.HasStatus(2618), "a bot-driven one doesn't wait for them");
        }
        finally
        {
            DebugBotControl.Enabled = false;
        }
    }

    [Test]
    public void ATargetOnlyHitLeavesTheOthersItCaughtAlone()
    {
        var plan = new MitPlanData(Profile, [new("buster", "Buster", 100000f, Kefka, ActionIds: [777u], TargetOnly: true)], [new(5f, ["buster"])], []);
        world.MitPractice.Begin(plan);
        var tanks = new[] { Slot(PartyRole.MainTank), Slot(PartyRole.OffTank) };
        var shown = world.MitPractice.Resolved(777u, tanks, 0f, Slot(PartyRole.MainTank));
        Step(0.4f);
        Assert.That(shown.Keys, Is.EqualTo(new[] { tanks[0] }));
        Assert.That(HpShare(PartyRole.MainTank), Is.LessThan(1f));
        Assert.That(HpShare(PartyRole.OffTank), Is.EqualTo(1f));
    }

    [Test]
    public void ADebuffWithNoBossNamedGoesOnEveryBoss()
    {
        var chaos = world.SpawnEnemy(new EnemySpawnConfig(ChaosP3BNpcBaseId))!;
        var exdeath = world.SpawnEnemy(new EnemySpawnConfig(ExdeathBNpcBaseId))!;
        var chaosSource = new MitSource("Chaos");
        var exdeathSource = new MitSource("Exdeath");
        world.MitPractice.Begin(new MitPlanData(Profile, [new("hit", "Big Hit", null, exdeathSource)], [new(5f, ["hit"])],
                [new MitPlanEntry(SheetColumn.MT, MitActionId.Reprisal, 1f, "Big Hit", [5f])]),
            new Dictionary<MitSource, Func<SimCharacter?>> { [chaosSource] = () => chaos, [exdeathSource] = () => exdeath });
        Step(1.1f);
        Assert.That(chaos.HasStatus(1193) && exdeath.HasStatus(1193), "Reprisal reaches both bosses");
    }

    private byte Shield(PartyRole role) => ((FakeBattleChara)Slot(role).Proxy!).Shield;

    private static bool ClientSees(SimCharacter who, ushort status)
        => ((FakeBattleChara)who.Proxy!).StatusIds.Contains(status);

    [Test]
    public void ABarrierPressReachesTheClientAsAStatusAndAShieldShare()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.OT, MitPlanEntry.PartyMit, 1f, "Big Hit", [5f])));
        Step(1.1f);
        Step(0.1f);
        Assert.That(ClientSees(Slot(PartyRole.MeleeDpsA), 1362), "Divine Veil's icon");
        Assert.That(Shield(PartyRole.MeleeDpsA), Is.EqualTo(10), "10% of max HP");
        Assert.That(Shield(PartyRole.MainTank), Is.EqualTo(10));
    }

    [Test]
    public void AHitEatsTheShieldBeforeHp()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.OT, MitPlanEntry.PartyMit, 1f, "Big Hit", [5f])));
        Step(1.2f);
        world.MitPractice.Hit(Slot(PartyRole.MeleeDpsA), "hit");
        Step(0.4f);
        Assert.That(Shield(PartyRole.MeleeDpsA), Is.EqualTo(0));
        Assert.That(ClientSees(Slot(PartyRole.MeleeDpsA), 1362), Is.False);
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.LessThan(1f));
    }

    [Test]
    public void AHitSmallerThanTheShieldLeavesTheRestOfIt()
    {
        var small = new MitPlanData(Profile, [new("small", "Small", 10000f, Kefka)], [new(5f, ["small"])],
            [new MitPlanEntry(SheetColumn.OT, MitPlanEntry.PartyMit, 1f, "Small", [5f])], Kefka);
        world.MitPractice.Begin(small);
        Step(1.2f);
        world.MitPractice.Hit(Slot(PartyRole.MeleeDpsA), "small");
        Step(0.4f);
        Assert.That(Shield(PartyRole.MeleeDpsA), Is.EqualTo(6).Within(1), "22.7k shield minus 10k");
        Assert.That(HpShare(PartyRole.MeleeDpsA), Is.EqualTo(1f));
    }

    [Test]
    public void AnExpiredShieldClearsStatusAndShare()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.OT, MitPlanEntry.PartyMit, 1f, "Big Hit", [5f])));
        Step(1.1f);
        Step(31f);
        Step(0.1f);
        Assert.That(ClientSees(Slot(PartyRole.MeleeDpsA), 1362), Is.False);
        Assert.That(Shield(PartyRole.MeleeDpsA), Is.EqualTo(0));
    }

    [Test]
    public void ABossDebuffPressShowsOnTheBossesStatusList()
    {
        var chaos = world.SpawnEnemy(new EnemySpawnConfig(ChaosP3BNpcBaseId))!;
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.D1, MitActionId.Feint, 1f, "Big Hit", [5f])),
            new Dictionary<MitSource, Func<SimCharacter?>> { [Kefka] = () => chaos });
        Step(1.1f);
        Step(0.1f);
        Assert.That(ClientSees(chaos, 1195));
    }

    [Test]
    public void ADuePressIsReportedOnTheWorldClock()
    {
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.MT, MitActionId.Rampart, 4f, "Big Hit", [5f])));
        var tank = (ISimPartyMember)Slot(PartyRole.MainTank);
        Assert.That(world.MitPractice.NextPressAt(tank), Is.EqualTo(4f).Within(0.05f));
        Step(4.5f);
        Assert.That(world.MitPractice.NextPressAt(tank), Is.Null);
    }

    [Test]
    public void APressArrivingMidCastCancelsTheBarFirst()
    {
        var bot = (SimPartyNpc)Slot(PartyRole.MainTank);
        var boss = world.SpawnEnemy(new EnemySpawnConfig(ChaosP3BNpcBaseId, Targetable: true))!;
        world.MitPractice.Begin(Data(new MitPlanEntry(SheetColumn.MT, MitActionId.Rampart, 1f, "Big Hit", [5f])));
        bot.BeginCast(25865u, 2.5f, boss);
        Assert.That(bot.Proxy!.IsCasting);
        Step(1.1f);
        Assert.That(bot.Proxy!.IsCasting, Is.False, "the bar was cancelled");
        Assert.That(bot.HasStatus(1191), "and Rampart went out");
    }

    private void BeginSomber(PartyRole baiter)
    {
        MitPlanEntry[] rows = [.. FruMitigationRows.Invulns(SheetColumn.MT, 10f, "Somber"), .. FruMitigationRows.Invulns(SheetColumn.OT, 10f, "Somber")];
        world.MitPractice.Begin(new MitPlanData(Profile, [new("hit", "Big Hit", 1000f, Kefka)], [new(10f, ["hit"])], rows, Kefka, () => baiter));
        Step(9f);
    }

    [Test]
    public void OnlyABotPaladinBaiterPressesHallowedGround()
    {
        BeginSomber(PartyRole.OffTank);
        Assert.That(Slot(PartyRole.OffTank).HasStatus(82), "Hallowed Ground");
        Assert.That(Slot(PartyRole.MainTank).HasStatus(409), Is.False, "the Warrior takes nothing for it");
    }

    [Test]
    public void OnlyABotWarriorBaiterPressesHolmgang()
    {
        BeginSomber(PartyRole.MainTank);
        Assert.That(Slot(PartyRole.MainTank).HasStatus(409), "Holmgang");
        Assert.That(Slot(PartyRole.OffTank).HasStatus(82), Is.False, "the Paladin keeps its Hallowed Ground");
    }

    [Test]
    public void NoBotPressesForAPlayerBaiter()
    {
        BeginSomber(PartyRole.ShieldHealer);
        Assert.That(Slot(PartyRole.MainTank).HasStatus(409) || Slot(PartyRole.OffTank).HasStatus(82), Is.False);
    }

    [TestCase(JobId.Gunbreaker, MitActionId.Superbolide)]
    [TestCase(JobId.DarkKnight, MitActionId.LivingDead)]
    [TestCase(JobId.Paladin, MitActionId.HallowedGround)]
    [TestCase(JobId.Warrior, MitActionId.Holmgang)]
    public void EveryTankJobHasItsOwnInvulnerabilityForTheBaiter(JobId job, uint button)
    {
        var jobs = new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = job };
        var resolved = MitPlan.Resolve(FruMitigationRows.Invulns(SheetColumn.MT, 10f, "Somber"), jobs);
        Assert.That(resolved.Select(r => r.Action.ActionId), Is.EqualTo(new[] { button }));
    }
}

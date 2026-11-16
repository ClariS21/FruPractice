using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Core.SimObjects;

namespace AnoMech.Tests;

public class BotWeaponsTests
{
    private static readonly DataminingGameData Data = new(new FakeRsvFunctions());

    private const uint EdenmornItemLevel = 735;
    private const string EdenmornPrefix = "Ultimate Edenmorn";
    private const byte Paladin = (byte)JobId.Paladin;
    private const byte Bard = (byte)JobId.Bard;
    private const byte WhiteMage = (byte)JobId.WhiteMage;

    private static WeaponLook Look(ulong packed) => new((ushort)packed, (ushort)(packed >> 16), (ushort)(packed >> 32), 0, 0);

    [Test]
    public void EveryJobCoversTheFruCombatJobsAtLeast()
    {
        JobId[] required =
        [
            JobId.Warrior, JobId.Paladin, JobId.Gunbreaker, JobId.DarkKnight, JobId.WhiteMage, JobId.Scholar, JobId.Astrologian,
            JobId.Sage, JobId.Dragoon, JobId.Monk, JobId.Bard, JobId.BlackMage,
        ];
        foreach (var job in required)
            Assert.That(BotWeapons.Items.ContainsKey((byte)job), Is.True, job.ToString());
    }

    [Test]
    public void EveryTableItemIsAnUltimateEdenmornWeaponOfItsJob()
    {
        Assert.That(BotWeapons.Items, Is.Not.Empty);
        foreach (var (job, (main, off)) in BotWeapons.Items)
        {
            foreach (var id in new[] { main, off }.Where(id => id != 0))
            {
                var row = Data.Item(id);
                Assert.That(row, Is.Not.Null, $"job {job} item {id} exists");
                Assert.That(row!.Name, Does.StartWith(EdenmornPrefix), $"job {job} item {id}");
                Assert.That(row.LevelItem, Is.EqualTo(EdenmornItemLevel), $"job {job} item {id}");
                Assert.That(Data.CategoryIncludesJob(row.ClassJobCategory, job), Is.True, $"job {job} item {id} {row.Name} is usable by the job");
            }
            Assert.That(Data.Item(main)!.ModelMain, Is.Not.Zero, $"job {job} main hand has a model");
        }
    }

    [Test]
    public void PaladinHoldsTheEdenmornSwordAndScutum()
    {
        var set = BotWeapons.Choose(Paladin, null, Data);
        Assert.That(set, Is.Not.Null);
        Assert.That(Data.Item(BotWeapons.Items[Paladin].Main)!.Name, Is.EqualTo("Ultimate Edenmorn Bastard Sword"));
        Assert.That(set!.MainHand, Is.EqualTo(Look(Data.Item(44721)!.ModelMain)));
        Assert.That(set.OffHand, Is.EqualTo(Look(Data.Item(44742)!.ModelMain)));
        Assert.That(Data.Item(44742)!.Name, Is.EqualTo("Ultimate Edenmorn Scutum"));
    }

    [Test]
    public void BardHoldsTheBowAndItsQuiver()
    {
        var bow = Data.Item(BotWeapons.Items[Bard].Main)!;
        var set = BotWeapons.Choose(Bard, null, Data)!;
        Assert.That(bow.Name, Is.EqualTo("Ultimate Edenmorn Cavalry Bow"));
        Assert.That(set.MainHand, Is.EqualTo(Look(bow.ModelMain)));
        Assert.That(bow.ModelSub, Is.Not.Zero);
        Assert.That(set.OffHand, Is.EqualTo(Look(bow.ModelSub)));
    }

    [Test]
    public void WhiteMageHoldsOnlyTheCane()
    {
        var cane = Data.Item(BotWeapons.Items[WhiteMage].Main)!;
        var set = BotWeapons.Choose(WhiteMage, null, Data)!;
        Assert.That(cane.Name, Is.EqualTo("Ultimate Edenmorn Cane"));
        Assert.That(set.MainHand, Is.EqualTo(Look(cane.ModelMain)));
        Assert.That(set.OffHand, Is.Null);
    }

    [Test]
    public void AJobWithNoTableEntryGetsNoWeapons()
        => Assert.That(BotWeapons.Choose((byte)JobId.Gladiator, null, Data), Is.Null);

    [Test]
    public void HanasOwnWeaponIsKeptWhenItBelongsToTheJobSheIsPlaying()
    {
        var glamour = new WeaponLook(1234, 1, 2, 0, 0);
        var look = new BotLook("Hana Shirosaki", Paladin, new byte[26], new EquipmentLook[10], glamour, new WeaponLook(55, 6, 7, 0, 0));
        var set = BotWeapons.Choose(Paladin, look, Data)!;
        Assert.That(set.MainHand, Is.EqualTo(glamour));
        Assert.That(set.OffHand, Is.EqualTo(look.OffHand));
    }

    [Test]
    public void HanaGetsTheFruWeaponWhenTheCapturedOneIsAnotherJobsOrMissing()
    {
        var fru = BotWeapons.Choose(Paladin, null, Data)!;
        var glamour = new WeaponLook(1234, 1, 2, 0, 0);
        var otherJob = new BotLook("Hana Shirosaki", (byte)JobId.BlackMage, new byte[26], new EquipmentLook[10], glamour, null);
        Assert.That(BotWeapons.Choose(Paladin, otherJob, Data), Is.EqualTo(fru));
        var unarmed = new BotLook("Hana Shirosaki", Paladin, new byte[26], new EquipmentLook[10], new WeaponLook(0, 0, 0, 0, 0), null);
        Assert.That(BotWeapons.Choose(Paladin, unarmed, Data), Is.EqualTo(fru));
        var noWeapon = unarmed with { MainHand = null };
        Assert.That(BotWeapons.Choose(Paladin, noWeapon, Data), Is.EqualTo(fru));
    }

    [Test]
    public void EverySpawnedPartyBotHoldsItsOwnJobsWeaponAndHasItDrawn()
    {
        var fake = FakeGame.Install();
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        try
        {
            fake.BattleCharas.Player.ClassJob = (byte)JobId.Sage;
            game.World.Reseed(1);
            game.World.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
            for (var i = 0; i < 120; i++)
                fake.Frame(1f / 60f, dt => { game.World.Events.Tick(dt); game.World.Tick(dt); });

            var bots = game.World.Party.AllMembers().OfType<SimPartyNpc>().ToList();
            Assert.That(bots, Has.Count.GreaterThanOrEqualTo(6));
            foreach (var bot in bots)
            {
                var expected = BotWeapons.Choose(bot.ClassJob, null, Data);
                Assert.That(expected, Is.Not.Null, $"job {bot.ClassJob} has a weapon");
                Assert.That(fake.BattleCharas.DoppelWeapons[bot.EntityId], Is.EqualTo(expected), $"{bot.DisplayName} (job {bot.ClassJob}) spawn weapons");
                var drawn = fake.Timeline.Rows.Where(r => r["kind"] == "ac" && r["actor"] == bot.EntityId.ToString("X8") && r["id"] == "0x0" && r["p"] == "0x1,0x1");
                Assert.That(drawn, Is.Not.Empty, $"{bot.DisplayName} has the weapon drawn");
            }
        }
        finally
        {
            game.World.Despawn();
            DalamudServices.Install(nameof(Plugin.Game), null!);
        }
    }
}

using System.Text.Json;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Implementations;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

public class BotLookLibraryTests
{
    private static readonly DataminingGameData Data = new(new FakeRsvFunctions());
    private string dir = null!;

    [SetUp]
    public void MakeDir() => dir = Path.Combine(Path.GetTempPath(), "looks-" + Guid.NewGuid());

    [TearDown]
    public void RemoveDir()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private static BotLook Make(string name, JobId job, byte seed, bool armed = true)
        => new(name, (byte)job, Enumerable.Repeat(seed, 26).ToArray(), new EquipmentLook[10],
            armed ? new WeaponLook((ushort)(1000 + seed), 1, 2, 0, 0) : null, null);

    private static string SampleDirectory => Path.Combine(AppContext.BaseDirectory, "main-tank-looks-sample");

    private static IReadOnlyList<PartySlot?> Dress(PartyRole player, IReadOnlyList<BotLook> pool, int seed, BotLook? previous = null)
    {
        var world = new SimWorld(new EventScheduler());
        world.Reseed(seed);
        return BotLooks.Dress(new LocalPartyProvider { RoleOverride = player }.Roster(25), world, pool, previous);
    }

    private static BotSlot Seat(IReadOnlyList<PartySlot?> roster, PartyRole role) => (BotSlot)roster[(int)role]!;

    [Test]
    public void CaptureAddsAndDedupes()
    {
        var first = BotLookLibrary.Add(dir, Make("Hana Shirosaki", JobId.Paladin, 1));
        Assert.That(first.Added, Is.True);
        Assert.That(BotLookLibrary.CaptureMessage(first), Is.EqualTo("Captured look 1 \"PLD 1\" (PLD) - 1 look"));

        var again = BotLookLibrary.Add(dir, Make("Renamed", JobId.Paladin, 1));
        Assert.That(again.Added, Is.False);
        Assert.That(BotLookLibrary.CaptureMessage(again), Is.EqualTo("Look already captured as look 1 \"PLD 1\" - 1 look"));

        var other = BotLookLibrary.Add(dir, Make("Hana Shirosaki", JobId.Paladin, 2));
        Assert.That(BotLookLibrary.CaptureMessage(other), Is.EqualTo("Captured look 2 \"PLD 2\" (PLD) - 2 looks"));
        var named = BotLookLibrary.Add(dir, Make("Hana Shirosaki", JobId.Gunbreaker, 3), "Gunny");
        Assert.That(BotLookLibrary.CaptureMessage(named), Is.EqualTo("Captured look 3 \"Gunny\" (GNB) - 3 looks"));
        Assert.That(BotLookLibrary.LoadFolder(dir), Has.Count.EqualTo(3));

        // The same look on another job, or with another weapon, is a different look.
        Assert.That(BotLookLibrary.Add(dir, Make("Hana Shirosaki", JobId.Warrior, 1)).Added, Is.True);
        Assert.That(BotLookLibrary.Add(dir, Make("Hana Shirosaki", JobId.Paladin, 1, armed: false)).Added, Is.True);
    }

    [Test]
    public void CaptureMessagesStartWithThePluginChatPrefix()
    {
        var message = MainTankLookCapture.Format(BotLookLibrary.CaptureMessage(BotLookLibrary.Add(dir, Make("Hana Shirosaki", JobId.Paladin, 1))));
        Assert.That(message, Is.EqualTo(PluginIdentity.ChatPrefix + " Captured look 1 \"PLD 1\" (PLD) - 1 look"));
        Assert.That(message, Does.StartWith(PluginIdentity.ChatPrefix));
        Assert.That(message, Does.Not.Contain("AnoMech"));
    }

    [Test]
    public void TheLegacyFileIsImportedOnce()
    {
        Directory.CreateDirectory(dir);
        var local = Path.Combine(dir, "collection");
        File.WriteAllText(Path.Combine(dir, BotLookLibrary.LegacyFileName), JsonSerializer.Serialize(Make("Hana Shirosaki", JobId.Warrior, 7)));

        BotLookLibrary.ImportLegacy(dir, local);
        Assert.That(BotLookLibrary.LoadFolder(local), Has.Count.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(dir, BotLookLibrary.LegacyFileName)), Is.False);

        // A look removed afterwards stays removed.
        BotLookLibrary.Remove(local, 1);
        BotLookLibrary.ImportLegacy(dir, local);
        Assert.That(BotLookLibrary.LoadFolder(local), Is.Empty);
    }

    [Test]
    public void RemoveDeletesTheNumberedLook()
    {
        BotLookLibrary.Add(dir, Make("A", JobId.Paladin, 1));
        BotLookLibrary.Add(dir, Make("B", JobId.Warrior, 2));
        BotLookLibrary.Add(dir, Make("C", JobId.Gunbreaker, 3));
        Assert.That(BotLookLibrary.Remove(dir, 2)!.ClassJob, Is.EqualTo((byte)JobId.Warrior));
        Assert.That(BotLookLibrary.LoadFolder(dir).Select(l => l.ClassJob), Is.EqualTo(new[] { (byte)JobId.Paladin, (byte)JobId.Gunbreaker }));
        Assert.That(BotLookLibrary.Remove(dir, 0), Is.Null);
        Assert.That(BotLookLibrary.Remove(dir, 3), Is.Null);
        Assert.That(BotLookLibrary.Add(dir, Make("D", JobId.Paladin, 9)).Index, Is.EqualTo(3));
    }

    [Test]
    public void ABrokenOrMissingFolderIsEmptyNotAnError()
    {
        Assert.That(BotLookLibrary.LoadFolder(null), Is.Empty);
        Assert.That(BotLookLibrary.LoadFolder(Path.Combine(dir, "nope")), Is.Empty);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bad.json"), "not json");
        Assert.That(BotLookLibrary.LoadFolder(dir), Is.Empty);
    }

    [Test]
    public void ThePoolMergesShippedAndLocalLooksWithoutRepeats()
    {
        var shipped = BotLookLibrary.LoadFolder(SampleDirectory);
        Assert.That(shipped, Has.Count.EqualTo(1));
        Assert.That(shipped[0].ClassJob, Is.EqualTo((byte)JobId.Gunbreaker));
        BotLookLibrary.Add(dir, shipped[0] with { Name = "Copy" });
        BotLookLibrary.Add(dir, Make("Hana Shirosaki", JobId.Paladin, 4));
        var pool = BotLookLibrary.Pool(shipped, BotLookLibrary.LoadFolder(dir));
        Assert.That(pool.Select(l => l.ClassJob), Is.EqualTo(new[] { (byte)JobId.Gunbreaker, (byte)JobId.Paladin }));
    }

    [Test]
    public void TheDrawIsDeterministicPerSeedAndNeverRepeatsBackToBack()
    {
        BotLook[] pool = [Make("One", JobId.Paladin, 1), Make("Two", JobId.Warrior, 2), Make("Three", JobId.Gunbreaker, 3)];
        BotLook? previous = null;
        var seen = new HashSet<string>();
        for (var run = 0; run < 50; run++)
        {
            var pick = Seat(Dress(PartyRole.CasterDps, pool, run, previous), PartyRole.MainTank).Look;
            Assert.That(Seat(Dress(PartyRole.CasterDps, pool, run, previous), PartyRole.MainTank).Look, Is.EqualTo(pick), $"run {run} replays");
            if (previous != null) Assert.That(pick, Is.Not.EqualTo(previous), $"run {run} repeats {pick!.Name}");
            seen.Add(pick!.Name);
            previous = pick;
        }
        Assert.That(seen, Has.Count.EqualTo(3));
    }

    [Test]
    public void TheDrawLeavesTheMechanicStreamUntouched()
    {
        var world = new SimWorld(new EventScheduler());
        world.Reseed(11);
        var expectedNext = new Rng(11).Next(100000);
        BotLooks.Dress(new LocalPartyProvider { RoleOverride = PartyRole.CasterDps }.Roster(25), world, [Make("One", JobId.Paladin, 1), Make("Two", JobId.Warrior, 2)], null);
        Assert.That(world.Rng.Next(100000), Is.EqualTo(expectedNext));
    }

    [Test]
    public void ATankLookMakesHanaPlayThatJobKeepingItsWeapon()
    {
        var look = Make("Hana Shirosaki", JobId.Gunbreaker, 3);
        var mt = Seat(Dress(PartyRole.CasterDps, [look], 1), PartyRole.MainTank);
        Assert.That(mt.Preset.ClassJob, Is.EqualTo((byte)JobId.Gunbreaker));
        Assert.That(BotWeapons.Choose(mt.Preset.ClassJob, mt.Look, Data)!.MainHand, Is.EqualTo(look.MainHand));
    }

    [Test]
    public void ANonTankLookMakesHanaPlayTheDefaultTankWithItsEdenmornWeapon()
    {
        var look = Make("Hana Shirosaki", JobId.BlackMage, 5);
        var mt = Seat(Dress(PartyRole.CasterDps, [look], 1), PartyRole.MainTank);
        Assert.That(mt.Preset.ClassJob, Is.EqualTo((byte)JobId.Warrior));
        Assert.That(BotWeapons.Choose(mt.Preset.ClassJob, mt.Look, Data), Is.EqualTo(BotWeapons.Choose((byte)JobId.Warrior, null, Data)));
    }

    [Test]
    public void APaladinHanaForcesTheOffTankBotOffPaladin()
    {
        var roster = Dress(PartyRole.CasterDps, [Make("Hana Shirosaki", JobId.Paladin, 2)], 1);
        Assert.That(Seat(roster, PartyRole.MainTank).Preset.ClassJob, Is.EqualTo((byte)JobId.Paladin));
        var ot = Seat(roster, PartyRole.OffTank).Preset.ClassJob;
        Assert.That(ot, Is.EqualTo((byte)JobId.Warrior));
        Assert.That(BotScripts.For(ot), Is.Not.Null);
        Assert.That(BotWeapons.Choose(ot, Seat(roster, PartyRole.OffTank).Look, Data), Is.EqualTo(BotWeapons.Choose(ot, null, Data)));

        // No clash, no change.
        var clear = Dress(PartyRole.CasterDps, [Make("Hana Shirosaki", JobId.Gunbreaker, 2)], 1);
        Assert.That(Seat(clear, PartyRole.OffTank).Preset.ClassJob, Is.EqualTo((byte)JobId.Paladin));
    }

    [Test]
    public void TheOffTankAvoidsEveryTankHanaCouldTake()
    {
        foreach (var job in new[] { JobId.Warrior, JobId.Paladin, JobId.Gunbreaker, JobId.DarkKnight })
        {
            var roster = Dress(PartyRole.CasterDps, [Make("Hana Shirosaki", job, 2)], 1);
            Assert.That(Seat(roster, PartyRole.OffTank).Preset.ClassJob, Is.Not.EqualTo(Seat(roster, PartyRole.MainTank).Preset.ClassJob), job.ToString());
        }
    }

    [Test]
    public void ThePlayersSeatIsNeverChanged()
    {
        var roster = Dress(PartyRole.OffTank, [Make("Hana Shirosaki", JobId.Paladin, 2)], 1);
        Assert.That(roster[(int)PartyRole.OffTank], Is.TypeOf<PlayerSlot>());
        var mtPlayer = Dress(PartyRole.MainTank, [Make("Hana Shirosaki", JobId.Paladin, 2)], 1);
        Assert.That(mtPlayer[(int)PartyRole.MainTank], Is.TypeOf<PlayerSlot>());
        Assert.That(Seat(mtPlayer, PartyRole.OffTank).Preset.ClassJob, Is.EqualTo((byte)JobId.Paladin));
    }

    [Test]
    public void AnEmptyPoolFallsBackToTheRandomHana()
    {
        var roster = Dress(PartyRole.CasterDps, [], 3);
        var mt = Seat(roster, PartyRole.MainTank);
        Assert.That(mt.Look!.Name, Is.EqualTo("Hana Shirosaki"));
        Assert.That(mt.Preset.ClassJob, Is.EqualTo((byte)JobId.Warrior));
        Assert.That(BotLooks.Pick([], new Rng(1), null), Is.Null);
    }

    [Test]
    public void SeatingThePartyRunAfterRunRotatesTheMainTanksLook()
    {
        var fake = FakeGame.Install();
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        try
        {
            fake.BattleCharas.MainTankLooks = [Make("Look A", JobId.Warrior, 1), Make("Look B", JobId.Paladin, 2)];
            string? last = null;
            for (var run = 0; run < 12; run++)
            {
                game.World.Reseed(run);
                game.World.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
                var name = game.World.Party.AllMembers().OfType<SimPartyNpc>().Single(b => b.Role == PartyRole.MainTank).DisplayName;
                Assert.That(name, Is.Not.EqualTo(last), $"run {run}");
                last = name;
            }
        }
        finally
        {
            game.World.Despawn();
            DalamudServices.Install(nameof(Plugin.Game), null!);
        }
    }
}

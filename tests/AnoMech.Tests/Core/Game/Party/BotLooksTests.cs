using System;
using System.Linq;
using System.Text.Json;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using NUnit.Framework;

namespace AnoMech.Tests.Core.Game.Party;

public class BotLooksTests
{
    [Test]
    public void RandomLooksHaveValidCustomizeAndDistinctNames()
    {
        var world = new SimWorld(new EventScheduler());
        world.Reseed(12345);
        var roster = BotLooks.Dress(new LocalPartyProvider { RoleOverride = PartyRole.CasterDps }.Roster(25), world, null);
        var looks = roster.Skip(1).OfType<BotSlot>().Select(slot => slot.Look!).ToArray();
        Assert.That(looks, Has.Length.EqualTo(6));
        Assert.That(looks.Select(look => look.Name).Distinct().ToArray(), Has.Length.EqualTo(6));
        foreach (var look in looks)
        {
            Assert.That(look.Name, Is.Not.EqualTo("Hana Shirosaki"));
            Assert.That(look.Customize, Has.Length.EqualTo(26));
            Assert.That(look.Customize[0], Is.InRange(1, 8));
            Assert.That(look.Customize[4], Is.AnyOf(look.Customize[0] * 2 - 1, look.Customize[0] * 2));
            Assert.That(look.Customize[3], Is.InRange(0, 100));
            Assert.That(look.Customize[5], Is.InRange(1, 4));
            Assert.That(look.Customize[6], Is.InRange(1, 6));
            Assert.That(look.Customize[1], Is.InRange(0, 1));
            Assert.That(look.Customize[8], Is.InRange(0, 15));
            Assert.That(look.Customize[9], Is.InRange(0, 15));
            Assert.That(look.Customize[10], Is.InRange(0, 15));
            Assert.That(look.Customize[11], Is.InRange(0, 15));
            Assert.That(look.Customize[15], Is.InRange(0, 15));
            Assert.That(look.Customize[20], Is.InRange(0, 15));
            Assert.That(BotLooks.NameFits(look), Is.True, look.Name);
        }
    }

    [Test]
    public void RaceAndGenderFollowEndgamePlayerDistribution()
    {
        const int count = 20000;
        var rng = new Rng(4242);
        var looks = Enumerable.Range(0, count).Select(_ => BotLooks.RandomLook(rng)).ToArray();
        double[] weights = [16.5, 5.9, 10.0, 22.8, 3.7, 20.1, 5.3, 15.7];
        double[] female = [0, 0.45, 0.75, 0.85, 0.30, 0.85, 0.30, 0.65];
        string[] names = ["Hyur", "Elezen", "Lalafell", "Miqote", "Roegadyn", "AuRa", "Hrothgar", "Viera"];
        for (var race = 1; race <= 8; race++)
        {
            var ofRace = looks.Where(l => l.Customize[0] == race).ToArray();
            var share = 100.0 * ofRace.Length / count;
            Assert.That(share, Is.EqualTo(weights[race - 1]).Within(1.5), names[race - 1]);
            if (race == 1)
            {
                var mid = ofRace.Where(l => l.Customize[4] == 1).ToArray();
                var high = ofRace.Where(l => l.Customize[4] == 2).ToArray();
                Assert.That(100.0 * mid.Length / ofRace.Length, Is.EqualTo(70).Within(3));
                Assert.That(100.0 * mid.Count(l => l.Customize[1] == 1) / mid.Length, Is.EqualTo(70).Within(3));
                Assert.That(100.0 * high.Count(l => l.Customize[1] == 1) / high.Length, Is.EqualTo(25).Within(3));
                TestContext.Out.WriteLine($"Hyur {share:F1}% mid {100.0 * mid.Length / ofRace.Length:F1}% midF {100.0 * mid.Count(l => l.Customize[1] == 1) / mid.Length:F1}% highF {100.0 * high.Count(l => l.Customize[1] == 1) / high.Length:F1}%");
                continue;
            }
            var f = (double)ofRace.Count(l => l.Customize[1] == 1) / ofRace.Length;
            Assert.That(f * 100, Is.EqualTo(female[race - 1] * 100).Within(3), names[race - 1]);
            TestContext.Out.WriteLine($"{names[race - 1]} {share:F1}% female {f * 100:F1}%");
            if (race == 3) Assert.That(f, Is.GreaterThanOrEqualTo(0.70));
        }
        Assert.That(looks.All(BotLooks.NameFits), Is.True);
    }

    [Test]
    public void MissingMainTankLookFallsBackToRandomHana()
    {
        var tankWorld = new SimWorld(new EventScheduler());
        tankWorld.Reseed(5);
        var seats = BotLooks.Dress(new LocalPartyProvider { RoleOverride = PartyRole.OffTank }.Roster(19), tankWorld, null);
        Assert.That(((BotSlot)seats[0]!).Look!.Name, Is.EqualTo("Hana Shirosaki"));
    }

    [Test]
    public void LookJsonRoundTrips()
    {
        var look = new BotLook("Hana Shirosaki", 19, Enumerable.Range(0, 26).Select(i => (byte)i).ToArray(),
            Enumerable.Range(0, 10).Select(i => new EquipmentLook((ushort)(100 + i), 2, 3, 4)).ToArray(),
            new WeaponLook(1000, 1, 2, 3, 4), new WeaponLook(2000, 5, 6, 7, 8));
        var copy = JsonSerializer.Deserialize<BotLook>(JsonSerializer.Serialize(look))!;
        Assert.That(copy.Name, Is.EqualTo(look.Name));
        Assert.That(copy.ClassJob, Is.EqualTo(look.ClassJob));
        Assert.That(copy.Customize, Is.EqualTo(look.Customize));
        Assert.That(copy.Equipment, Is.EqualTo(look.Equipment));
        Assert.That(copy.MainHand, Is.EqualTo(look.MainHand));
        Assert.That(copy.OffHand, Is.EqualTo(look.OffHand));
    }

    [Test]
    public void MainTankLookOnlyDressesBotSeatAndMechanicStreamIsUntouched()
    {
        var captured = new BotLook("Hana Shirosaki", 32, new byte[26], new EquipmentLook[10], null, null);
        var world = new SimWorld(new EventScheduler());
        world.Reseed(9876);
        var expectedNext = new Rng(9876).Next(100000);
        var dressed = BotLooks.Dress(new LocalPartyProvider { RoleOverride = PartyRole.OffTank }.Roster(19), world, captured);
        Assert.That(((BotSlot)dressed[0]!).Look, Is.SameAs(captured));
        Assert.That(((BotSlot)dressed[0]!).Preset.ClassJob, Is.EqualTo(32));
        Assert.That(world.Rng.Next(100000), Is.EqualTo(expectedNext));
        var nonTank = captured with { ClassJob = 25 };
        var fallbackJob = BotLooks.Dress(new LocalPartyProvider { RoleOverride = PartyRole.OffTank }.Roster(19), world, nonTank);
        Assert.That(((BotSlot)fallbackJob[0]!).Preset.ClassJob, Is.EqualTo(21));
        Assert.That(((BotSlot)fallbackJob[0]!).Look, Is.SameAs(nonTank));
        var mtPlayer = BotLooks.Dress(new LocalPartyProvider { RoleOverride = PartyRole.MainTank }.Roster(21), world, captured);
        Assert.That(mtPlayer[0], Is.TypeOf<PlayerSlot>());
    }
}

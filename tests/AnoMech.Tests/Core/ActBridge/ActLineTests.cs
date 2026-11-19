using System.Numerics;
using AnoMech.Core.ActBridge;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Tests;

public class ActLineTests
{
    private static readonly ActLineActor Boss = new(0x40000001, "Oracle of Darkness", 1000, 2000, 10000, 10000, new Vector3(100f, 0f, 90.5f), 3.14f);
    private static readonly ActLineActor Player = new(0x10000001, "Player One", 500, 600, 7, 8, new Vector3(1f, 2f, 3f), 1.5f);

    [Test]
    public void StartsUsingWithNoTarget()
        => Assert.That(ActLine.StartsUsing(Boss, 0x9D4A, "Ultimate Relativity", ActLineActor.None, 10f),
            Is.EqualTo("20|40000001|Oracle of Darkness|9D4A|Ultimate Relativity|E0000000||10.00|100.00|0.00|90.50|3.14"));

    [Test]
    public void StartsUsingWithTarget()
        => Assert.That(ActLine.StartsUsing(Boss, 0x9D4A, "Cast", Player, 4.7f),
            Is.EqualTo("20|40000001|Oracle of Darkness|9D4A|Cast|10000001|Player One|4.70|100.00|0.00|90.50|3.14"));

    [Test]
    public void SingleTargetActionEffectIsType21()
    {
        var damage = ActionEffectEntry.Damage(0x4A1B, 5);
        var line = ActLine.ActionEffect(false, Boss, 0x9D4A, "Cast", Player, [damage], 0xA3F1, 0, 1, 1, 0x9D4A, 0.6f, 0x1234);
        Assert.That(line, Is.EqualTo(
            "21|40000001|Oracle of Darkness|9D4A|Cast|10000001|Player One|750003|1B4A0000|0|0|0|0|0|0|0|0|0|0|0|0|0|0"
            + "|500|600|7|8||"
            + "|1.00|2.00|3.00|1.50"
            + "|1000|2000|10000|10000||"
            + "|100.00|0.00|90.50|3.14"
            + "|0000A3F1|0|1|||1|9D4A|9D4A|0.600|1234"));
    }

    [Test]
    public void MultiTargetActionEffectIsType22WithTheTargetIndex()
    {
        var line = ActLine.ActionEffect(true, Boss, 0x9D4A, "Cast", Player, null, 0xA3F1, 3, 8, 1, 0, 0f, 0);
        Assert.That(line, Does.StartWith("22|40000001|Oracle of Darkness|9D4A|Cast|10000001|Player One|0|0|"));
        Assert.That(line, Does.EndWith("|0000A3F1|3|8|||1|9D4A|0|0.000|0000"));
    }

    [Test]
    public void ActionEffectHasTheFieldCountOfTheLog()
        => Assert.That(ActLine.ActionEffect(false, Boss, 1, "A", Player, null, 0, 0, 1, 1, 0, 0f, 0).Split('|'), Has.Length.EqualTo(53));

    [Test]
    public void CancelAbility()
        => Assert.That(ActLine.CancelAbility(Boss, 0x9D4A, "Cast", "Interrupted"), Is.EqualTo("23|40000001|Oracle of Darkness|9D4A|Cast|Interrupted"));

    [Test]
    public void WasDefeated()
        => Assert.That(ActLine.WasDefeated(Player, ActLineActor.None), Is.EqualTo("25|10000001|Player One|E0000000|"));

    [Test]
    public void GainsEffect()
        => Assert.That(ActLine.GainsEffect(0xB9A, "Lightsteeped", 20f, Boss, Player, 3), Is.EqualTo(
            "26|B9A|Lightsteeped|20.00|40000001|Oracle of Darkness|10000001|Player One|03|600|2000"));

    [TestCase(0x00, "00")]
    [TestCase(0x03, "03")]
    [TestCase(0xFF, "FF")]
    [TestCase(0x45F, "45F")]
    [TestCase(0x462, "462")]
    public void StatusCountPreservesTheFullParam(int param, string count)
    {
        Assert.That(ActLine.GainsEffect(0x808, "X", 20, Boss, Player, (ushort)param).Split('|')[8], Is.EqualTo(count));
        Assert.That(ActLine.LosesEffect(0x808, "X", Boss, Player, (ushort)param).Split('|')[8], Is.EqualTo(count));
    }

    [Test]
    public void GainsEffectFromNoSource()
        => Assert.That(ActLine.GainsEffect(0xB9A, "X", 0f, ActLineActor.None, Player, 0), Is.EqualTo("26|B9A|X|0.00|E0000000||10000001|Player One|00|600|0"));

    [Test]
    public void LosesEffect()
        => Assert.That(ActLine.LosesEffect(0xB9A, "Lightsteeped", Boss, Player, 3), Is.EqualTo(
            "30|B9A|Lightsteeped|0.00|40000001|Oracle of Darkness|10000001|Player One|03|600|2000"));

    [Test]
    public void ActorControl()
        => Assert.That(ActLine.ActorControl(0x800375D2, 0x4000000F, 0x1C20), Is.EqualTo("33|800375D2|4000000F|1C20|00|00|00"));

    [Test]
    public void GameLogEcho()
        => Assert.That(ActLine.GameLogEcho("cactbot wipe"), Is.EqualTo("00|0038||cactbot wipe|"));

    [Test]
    public void HeadMarker()
        => Assert.That(ActLine.HeadMarker(Player, 0x17), Is.EqualTo("27|10000001|Player One|0000|0000|0017|0000|0000|0000"));

    [Test]
    public void Tether()
        => Assert.That(ActLine.Tether(Boss, Player, 0x85), Is.EqualTo("35|40000001|Oracle of Darkness|10000001|Player One|0000|0000|0085|10000001|000F|0000"));

    [Test]
    public void MapEffect()
        => Assert.That(ActLine.MapEffect(0x00020001, 8), Is.EqualTo("257|00000000|00020001|08|0000|0000"));

    [Test]
    public void StartsUsingExtra()
        => Assert.That(ActLine.StartsUsingExtra(0x40000001, 0x9D06, new Vector3(1f, 2f, 3f), 0.5f), Is.EqualTo("263|40000001|9D06|1.00|2.00|3.00|0.50"));

    [Test]
    public void ActorSetPos()
        => Assert.That(ActLine.ActorSetPos(0x40000001, new Vector3(100f, 0f, 95f), 1.57f), Is.EqualTo("271|40000001|1.57|00|00|100.00|0.00|95.00"));

    [Test]
    public void ActorControlExtra()
        => Assert.That(ActLine.ActorControlExtra(0x40000001, 0x3F, 4, 0, 0, 0), Is.EqualTo("273|40000001|003F|4|0|0|0"));

    // The expected lines are the LogGuide's examples.
    [Test]
    public void AbilityExtraWithoutALocation()
        => Assert.That(ActLine.AbilityExtra(0x4000619F, 0x8BC0, 0x664, null, 3.141f, 0x1234ABCD),
            Is.EqualTo("264|4000619F|8BC0|00000664|0||||3.141|1234ABCD"));

    [Test]
    public void AbilityExtraWithALocation()
        => Assert.That(ActLine.AbilityExtra(0x4000619D, 0x8BC0, 0x662, new Vector3(1f, 2f, 3f), 1.57f, 0x1234ABCD),
            Is.EqualTo("264|4000619D|8BC0|00000662|1|1.000|2.000|3.000|1.570|1234ABCD"));

    [Test]
    public void AbilityExtraWithNoAnimationTarget()
        => Assert.That(ActLine.AbilityExtra(0x4000619F, 0x8BC0, 0x664, null, 0f, 0), Does.EndWith("|0.000|E0000000"));

    [Test]
    public void SpawnNpcExtra()
        => Assert.That(ActLine.SpawnNpcExtra(0x4000226B, ActLine.NoActor, 0, 1), Is.EqualTo("272|4000226B|E0000000|0000|01"));

    [Test]
    public void SpawnNpcExtraWithATether()
        => Assert.That(ActLine.SpawnNpcExtra(0x40001234, ActLine.NoActor, 0x54, 0), Is.EqualTo("272|40001234|E0000000|0054|00"));
}

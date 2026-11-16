using AnoMech.Core.Game.PartyMit;

namespace AnoMech.Tests;

public class MitHpPoolTests
{
    [Test]
    public void HitsPileUpWithNothingRefillingBetween()
    {
        var pool = new MitHpPool(100f);
        pool.Land(30f);
        Assert.That(pool.Land(30f), Is.EqualTo(40f));
        Assert.That(pool.Land(30f), Is.EqualTo(10f));
    }

    [Test]
    public void AFreshHitLandsOnFullHpRegardless()
    {
        var pool = new MitHpPool(100f);
        pool.Land(70f);
        Assert.That(pool.Land(20f, fresh: true), Is.EqualTo(80f));
    }

    [Test]
    public void AHealRaisesHpOnlyWhenItLandsAndNeverPastFull()
    {
        var pool = new MitHpPool(100f);
        pool.Land(60f);
        Assert.That(pool.Hp, Is.EqualTo(40f));
        pool.Heal(25f);
        Assert.That(pool.Hp, Is.EqualTo(65f));
        pool.Heal(80f);
        Assert.That(pool.Hp, Is.EqualTo(100f));
    }

    [Test]
    public void AHealStartsFromZeroAfterAWouldBeKill()
    {
        var pool = new MitHpPool(100f);
        pool.Land(150f);
        pool.Heal(15f);
        Assert.That(pool.Hp, Is.EqualTo(15f));
    }

    [Test]
    public void HpCanGoBelowZero()
    {
        Assert.That(new MitHpPool(100f).Land(150f), Is.EqualTo(-50f));
    }
}

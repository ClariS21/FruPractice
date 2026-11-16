namespace AnoMech.Tests;

public class ConfigurationTests
{
    [Test]
    public void MitigationPracticeIsOnByDefault()
    {
        Assert.That(new Configuration().MitigationPractice, Is.True);
    }

    [Test]
    public void AVersionZeroConfigWithPracticeOffTurnsItOn()
    {
        var config = new Configuration { Version = 0, MitigationPractice = false };
        Assert.That(config.Migrate(), Is.True);
        Assert.That(config.MitigationPractice, Is.True);
        Assert.That(config.Version, Is.EqualTo(1));
    }

    [Test]
    public void AVersionOneConfigKeepsTheUsersChoice()
    {
        var config = new Configuration { Version = 1, MitigationPractice = false };
        Assert.That(config.Migrate(), Is.False);
        Assert.That(config.MitigationPractice, Is.False);
    }
}

using AnoMech.Core.Game;

namespace AnoMech.Tests;

public class PluginIdentityTests
{
    [Test]
    public void ThePluginIsFruPractice()
    {
        var assemblyName = typeof(Game).Assembly.GetName();
        Assert.Multiple(() =>
        {
            Assert.That(PluginIdentity.DisplayName, Is.EqualTo("FRU Practice"));
            Assert.That(PluginIdentity.InternalName, Is.EqualTo("FruPractice"));
            Assert.That(PluginIdentity.Command, Is.EqualTo("/frup"));
            Assert.That(PluginIdentity.ChatPrefix, Is.EqualTo("[FRU Practice]"));
            Assert.That(PluginIdentity.FilePrefix, Is.EqualTo("fru-practice"));
            Assert.That(assemblyName.Name, Is.EqualTo(PluginIdentity.InternalName));
            Assert.That(assemblyName.Version, Is.EqualTo(new Version(0, 3, 0, 0)));
        });
    }
}

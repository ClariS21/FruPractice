using AnoMech.Core.Game;

namespace AnoMech.Tests;

public class PluginContentsTests
{
    [Test]
    public void NoMultiplayerCodeShipsInThePlugin()
    {
        var assembly = typeof(Game).Assembly;
        Assert.Multiple(() =>
        {
            Assert.That(assembly.GetTypes().Where(t => t.Namespace?.StartsWith("AnoMech.Multiplayer") == true)
                .Select(t => t.FullName), Is.Empty);
            Assert.That(assembly.GetTypes().Select(t => t.Name),
                Has.None.EqualTo("SimNetworkPuppet").And.None.EqualTo("RemoteMotion"));
            Assert.That(assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.StartsWith("AnoMech.Relay"));
            Assert.That(typeof(Configuration).GetMembers().Select(m => m.Name),
                Has.None.Contains("Relay").And.None.Contains("PeerSecret"));
        });
    }
}

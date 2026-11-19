using System.Text;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru.P4Full;

namespace AnoMech.Tests;

// The plugin names its sources from the repo root (the PathMap in AnoMech.csproj), never by the folder it was built in.
public class SourcePathTests
{
    [Test]
    public void ThePluginCarriesNoPathOfTheCheckout()
    {
        var bytes = File.ReadAllBytes(typeof(Game).Assembly.Location);
        var root = RepoRoot();
        Assert.Multiple(() =>
        {
            foreach (var text in new[] { root, Path.GetPathRoot(root)! })
                foreach (var encoding in new[] { Encoding.ASCII, Encoding.Unicode })
                    Assert.That(bytes.AsSpan().IndexOf(encoding.GetBytes(text)), Is.EqualTo(-1), $"{encoding.EncodingName} {text}");
        });
    }

    [Test]
    public void TheSchedulerStillLogsAPluginCallSitesFileAndLine()
    {
        FakeGame.Install();
        var log = TraceLog.Create(() => 0f);
        DalamudServices.Install(nameof(Plugin.Log), log);
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        try
        {
            game.World.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
            new FruP4FullAi().Run(game.World);
            game.World.Events.Tick(1f);
            Assert.That(log.LastFired, Does.Match(@"^FruP4FullAi\.cs:\d+$"));
        }
        finally
        {
            game.Dispose();
            DalamudServices.Install(nameof(Plugin.Game), null!);
        }
    }

    // With the trailing separator, as a source path would carry it.
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AnoMech.sln")))
                return Path.TrimEndingDirectorySeparator(dir.FullName) + Path.DirectorySeparatorChar;
        throw new DirectoryNotFoundException("AnoMech.sln not found above the test directory.");
    }
}

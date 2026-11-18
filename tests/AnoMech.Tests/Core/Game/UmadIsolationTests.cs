using AnoMech.Core.ActBridge;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyHp;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P5Flood;

namespace AnoMech.Tests;

public class UmadIsolationTests
{
    private sealed class Sink : IActLineSink
    {
        public bool Connected => true;
        public List<string> Lines { get; } = [];
        public void Send(string line) => Lines.Add(line);
    }

    // DMU runs the calibrated layers but ACT emission, which writes FRU's director lines.
    [Test]
    public void UmadRestartKeepsItsPoolAndItsCalibration()
    {
        var fake = FakeGame.Install();
        DalamudServices.Install(nameof(Plugin.Config), new Configuration { MitigationPractice = true });
        var sink = new Sink();
        var emitter = ActEmitter.Install(sink, () => true);
        using var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        emitter.Attach(game);
        game.World.MitPractice.Enabled = true;
        var exhausted = new List<uint>();
        game.World.HelperPoolExhausted += exhausted.Add;
        try
        {
            var scenario = game.Catalog.Scenarios.OfType<UmadP5FloodScenario>().Single();
            game.ZoneIn(UmadZone.Instance, SessionMode.Local);
            for (var run = 0; run < 2; run++)
            {
                game.StartScenario(new RunScenarioParams(scenario,
                    new LocalPartyProvider { RoleOverride = PartyRole.MeleeDpsA }, 0, 0, 1));
                for (var frame = 0; frame < 12 * 60; frame++) fake.Frame(1f / 60f, game.Tick);
                Assert.Multiple(() =>
                {
                    Assert.That(game.World.Children.OfType<SimEnemy>().Count(e => e.BNpcBaseId == 9020), Is.EqualTo(37));
                    Assert.That(game.World.Calibration, Is.SameAs(UmadZone.Instance.Calibration));
                    Assert.That(game.World.HabitFor(PartyRole.MainTank), Is.Not.Null);
                    Assert.That(game.World.PartyHp.RollTickDelay(), Is.InRange(PartyHp.TickDelayMin, PartyHp.TickDelayMax));
                    Assert.That(game.World.MitPractice.IsActive, Is.False);
                    Assert.That(game.Guidance.Recording || game.Guidance.Debrief is not null, Is.True, "the path guide followed the run");
                    Assert.That(game.World.Strat.Entries.Any(s => s.Role == PartyRole.MeleeDpsA), Is.True, "the player's strat spots are on the trail");
                    Assert.That(((IScenario)scenario).BurstAnchors, Is.Empty);
                });
            }
            game.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(sink.Lines, Is.Empty);
                Assert.That(exhausted, Is.Empty);
                Assert.That(game.World.Children, Is.Empty);
            });
        }
        finally
        {
            emitter.Uninstall();
            DalamudServices.Install(nameof(Plugin.Game), null!);
        }
    }
}

using AnoMech.Core.ActBridge;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios.Fru.P3Apocalypse;

namespace AnoMech.Tests;

public class ActRunBoundaryTests
{
    private sealed class RecordingSink : IActLineSink
    {
        public bool Connected { get; set; } = true;
        public List<string> Lines { get; } = [];
        public void Send(string line) => Lines.Add(line);
    }

    private sealed class CactbotResetModel
    {
        private bool inCombat = true;
        private bool resetAtThisEnd;
        public int RunEndsReset { get; private set; }

        private void SetInCombat(bool value)
        {
            if (inCombat == value) return;
            inCombat = value;
            if (value && !resetAtThisEnd)
            {
                RunEndsReset++;
                resetAtThisEnd = true;
            }
        }

        public void OnLine(string payload)
        {
            var raw = payload.Insert(3, "2026-01-01T00:00:00|");
            var fields = raw.Split('|');
            if (fields is ["33", _, _, "40000005", ..])
            {
                resetAtThisEnd = false;
                return;
            }
            var wipe = fields is ["33", _, _, "4000000F" or "40000010", ..];
            if (!wipe) return;
            if (!inCombat) SetInCombat(true);
            SetInCombat(false);
        }
    }

    private sealed record Stream(List<string> Lines, int FirstEnd, int SecondStart, int SecondEnd, int ThirdStart, int ThirdEnd);

    private static Stream RunThreeTimes(bool connected = true)
    {
        var fake = FakeGame.Install();
        DalamudServices.Install(nameof(Plugin.Config), new Configuration());
        var sink = new RecordingSink { Connected = connected };
        var emitter = ActEmitter.Install(sink, () => true);
        var game = new Game();
        emitter.Attach(game);
        DalamudServices.Install(nameof(Plugin.Game), game);
        try
        {
            var scenario = game.Catalog.Scenarios.Single(s => s is FruP3ApocalypseScenario);
            var parameters = new RunScenarioParams(scenario, new LocalPartyProvider { RoleOverride = PartyRole.MeleeDpsA }, 0, 0, 1);
            game.ZoneIn(scenario.Phase.Zone, SessionMode.Local);
            game.StartScenario(parameters);
            fake.Frame(1f / 60f, game.Tick);
            for (var i = 0; i < 14 * 60; i++) fake.Frame(1f / 60f, game.Tick);
            var firstEnd = sink.Lines.Count;

            game.StartScenario(parameters);
            fake.Frame(1f / 60f, game.Tick);
            var secondStart = sink.Lines.FindIndex(firstEnd, line => line.StartsWith("33|800375D2|40000006|"));
            for (var i = 0; i < 14 * 60; i++) fake.Frame(1f / 60f, game.Tick);
            var secondEnd = sink.Lines.Count;

            game.StartScenario(parameters);
            fake.Frame(1f / 60f, game.Tick);
            var thirdStart = sink.Lines.FindIndex(secondEnd, line => line.StartsWith("33|800375D2|40000006|"));
            for (var i = 0; i < 14 * 60; i++) fake.Frame(1f / 60f, game.Tick);
            var thirdEnd = sink.Lines.Count;
            return new Stream(sink.Lines.ToList(), firstEnd, secondStart, secondEnd, thirdStart, thirdEnd);
        }
        finally
        {
            game.Dispose();
            emitter.Uninstall();
        }
    }

    [Test]
    public void RestartEmitsWipeThenRecommenceOnlyAtRunBoundaries()
    {
        var stream = RunThreeTimes();
        var lines = stream.Lines;
        Assert.That(stream.SecondStart, Is.GreaterThan(stream.FirstEnd));
        Assert.That(stream.ThirdStart, Is.GreaterThan(stream.SecondEnd));
        var boundary = new[]
        {
            "33|800375D2|40000005|00|00|00|00",
            "33|800375D2|4000000F|1C20|00|00|00",
            "33|800375D2|40000011|00|00|00|00",
            "33|800375D2|4000000F|1C20|00|00|00",
            "33|800375D2|40000006|1C20|00|00|00",
        };
        Assert.That(lines.Skip(stream.SecondStart - 4).Take(5), Is.EqualTo(boundary));
        Assert.That(lines.Skip(stream.ThirdStart - 4).Take(5), Is.EqualTo(boundary));
        Assert.That(lines, Has.None.StartsWith("00|"));
        Assert.That(lines.Take(stream.FirstEnd).Count(IsBoundary), Is.EqualTo(1));
        Assert.That(lines.Skip(stream.SecondStart).Take(stream.SecondEnd - stream.SecondStart).Count(IsBoundary), Is.EqualTo(1));
        Assert.That(lines.Skip(stream.ThirdStart).Take(stream.ThirdEnd - stream.ThirdStart).Count(IsBoundary), Is.EqualTo(1));
    }

    [Test]
    public void SecondWipeCausesCactbotResetAtEachRunEnd()
    {
        var stream = RunThreeTimes();
        var model = new CactbotResetModel();
        foreach (var line in stream.Lines.Take(stream.FirstEnd)) model.OnLine(line);
        Assert.That(model.RunEndsReset, Is.Zero);
        foreach (var line in stream.Lines.Skip(stream.FirstEnd).Take(stream.SecondStart - stream.FirstEnd)) model.OnLine(line);
        Assert.That(model.RunEndsReset, Is.EqualTo(1));
        foreach (var line in stream.Lines.Skip(stream.SecondStart).Take(stream.SecondEnd - stream.SecondStart)) model.OnLine(line);
        Assert.That(model.RunEndsReset, Is.EqualTo(1));
        foreach (var line in stream.Lines.Skip(stream.SecondEnd).Take(stream.ThirdStart - stream.SecondEnd)) model.OnLine(line);
        Assert.That(model.RunEndsReset, Is.EqualTo(2));
        foreach (var line in stream.Lines.Skip(stream.ThirdStart).Take(stream.ThirdEnd - stream.ThirdStart)) model.OnLine(line);
        Assert.That(model.RunEndsReset, Is.EqualTo(2));

        var withoutSecondWipe = new CactbotResetModel();
        foreach (var line in stream.Lines.Take(stream.ThirdEnd).Where((_, index) =>
                     index != stream.SecondStart - 1 && index != stream.ThirdStart - 1))
            withoutSecondWipe.OnLine(line);
        Assert.That(withoutSecondWipe.RunEndsReset, Is.EqualTo(1));
    }

    [Test]
    public void RestartClosesHeldStatusesAndSecondRunGainsThemAgain()
    {
        var stream = RunThreeTimes();
        var first = stream.Lines.Take(stream.FirstEnd).ToList();
        var between = stream.Lines.Skip(stream.FirstEnd).Take(stream.SecondStart - stream.FirstEnd).ToList();
        var second = stream.Lines.Skip(stream.SecondStart).Take(stream.SecondEnd - stream.SecondStart).ToList();
        var gains = first.Where(line => line.StartsWith("26|99D|")).ToList();
        Assert.That(gains, Has.Count.GreaterThan(0));
        Assert.That(second.Count(line => line.StartsWith("26|99D|")), Is.EqualTo(gains.Count));
        foreach (var gain in gains)
        {
            var fields = gain.Split('|');
            Assert.That(between.Count(line => line.StartsWith($"30|99D|{fields[2]}|0.00|{fields[4]}|{fields[5]}|{fields[6]}|{fields[7]}|")), Is.EqualTo(1));
        }

        static string Key(string line)
        {
            var fields = line.Split('|');
            return $"{fields[1]}|{fields[4]}|{fields[6]}";
        }

        var closedDuringRun = first.Where(line => line.StartsWith("30|")).Select(Key).ToHashSet();
        var heldAtEnd = first.Where(line => line.StartsWith("26|")).Select(Key).ToHashSet();
        heldAtEnd.ExceptWith(closedDuringRun);
        Assert.That(heldAtEnd, Is.Not.Empty);
        foreach (var key in heldAtEnd)
            Assert.That(between.Count(line => line.StartsWith("30|") && Key(line) == key), Is.EqualTo(1), key);
    }

    [Test]
    public void DisconnectedBridgeEmitsNothingAcrossRestart()
        => Assert.That(RunThreeTimes(connected: false).Lines, Is.Empty);

    private static bool IsBoundary(string line) => line.StartsWith("33|");
}

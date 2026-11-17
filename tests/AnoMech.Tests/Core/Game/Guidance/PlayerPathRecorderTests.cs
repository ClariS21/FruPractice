using System.Numerics;
using AnoMech.Core.Game.Guidance;

namespace AnoMech.Tests;

public class PlayerPathRecorderTests
{
    [Test]
    public void SamplesAtTenHzInterpolatingAcrossUnevenFrames()
    {
        var recorder = new PlayerPathRecorder();
        recorder.Start(Vector2.Zero);
        recorder.Sample(0.04f, new(0.4f, 0));
        recorder.Sample(0.26f, new(2.6f, 0));
        recorder.Sample(0.31f, new(3.1f, 0));
        Assert.That(recorder.Samples.Select(s => s.t), Is.EqualTo(new[] { 0f, 0.1f, 0.2f, 0.3f }).Within(0.00001f));
        Assert.That(recorder.Samples.Select(s => s.p.X), Is.EqualTo(new[] { 0f, 1f, 2f, 3f }).Within(0.00001f));
    }

    [Test]
    public void NewRunClearsSamplesAndRestartsCadence()
    {
        var recorder = new PlayerPathRecorder();
        recorder.Start(Vector2.Zero);
        recorder.Sample(1, Vector2.One);
        recorder.Start(new(7, 8));
        recorder.Sample(0.1f, new(8, 9));
        Assert.That(recorder.Samples, Is.EqualTo(new (float, Vector2)[] { (0, new(7, 8)), (0.1f, new(8, 9)) }));
    }
}

using System.Collections.Generic;
using System.Numerics;

namespace AnoMech.Core.Game.Guidance;

public sealed class PlayerPathRecorder
{
    private readonly List<(float t, Vector2 p)> samples = [];
    private (float t, Vector2 p) previous;
    private int nextSample;
    public IReadOnlyList<(float t, Vector2 p)> Samples => samples;

    public void Start(Vector2 position)
    {
        samples.Clear();
        previous = (0, position);
        samples.Add(previous);
        nextSample = 1;
    }

    public void Sample(float time, Vector2 position)
    {
        if (time <= previous.t) return;
        while (nextSample * 0.1f <= time + 0.00001f)
        {
            var at = nextSample++ * 0.1f;
            samples.Add((at, Vector2.Lerp(previous.p, position, (at - previous.t) / (time - previous.t))));
        }
        previous = (time, position);
    }
}

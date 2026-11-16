using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Scenarios;

namespace AnoMech.Core.Game;

// The player's recent positions and facings, so a mechanic can be judged where the server would
// see them: its last report from the client, about a ping behind what the player sees. The sim runs
// on the player's own machine, so without this a player dodges later than in the live game.
internal sealed class PositionLag(Func<float> now, Func<int> pingMs, Func<Rng> stream)
{
    private const float HistorySeconds = 2f;
    // Server tick and the client's position-send gap: the lookback is the ping plus up to this much.
    private const float JitterSeconds = 0.1f;
    // A jump this far between two frames is a teleport or snap, not walking: hold the earlier sample.
    private const float TeleportYalms = 3f;

    private readonly List<(float Time, Vector3 Position, float Rotation)> samples = [];
    private float lookbackAt = float.NaN;
    private float lookback;

    public void Record(Vector3 position, float rotation)
    {
        var t = now();
        if (samples.Count > 0 && t < samples[^1].Time) samples.Clear();
        if (samples.Count > 0 && t == samples[^1].Time) samples[^1] = (t, position, rotation);
        else samples.Add((t, position, rotation));
        var oldest = t - HistorySeconds;
        var drop = 0;
        while (drop < samples.Count - 1 && samples[drop + 1].Time <= oldest) drop++;
        if (drop > 0) samples.RemoveRange(0, drop);
    }

    // One lookback per sim instant, so every check in the same snapshot sees the same moment.
    private float Lookback(float t)
    {
        if (lookbackAt != t)
        {
            lookbackAt = t;
            lookback = pingMs() / 1000f + stream().NextSingle() * JitterSeconds;
        }
        return lookback;
    }

    public (Vector3 Position, float Rotation) Judged(Vector3 livePosition, float liveRotation)
    {
        if (pingMs() <= 0 || samples.Count == 0) return (livePosition, liveRotation);
        var t = now();
        var target = t - Lookback(t);
        if (target >= t) return (livePosition, liveRotation);
        if (target <= samples[0].Time) return (samples[0].Position, samples[0].Rotation);

        var earlier = samples[0];
        for (var i = 1; i <= samples.Count; i++)
        {
            var later = i < samples.Count ? samples[i] : (t, livePosition, liveRotation);
            if (target <= later.Item1)
            {
                if (Vector3.Distance(earlier.Position, later.Item2) > TeleportYalms) return (earlier.Position, earlier.Rotation);
                var span = later.Item1 - earlier.Time;
                var f = span <= 0f ? 1f : (target - earlier.Time) / span;
                var delta = MathF.Atan2(MathF.Sin(later.Item3 - earlier.Rotation), MathF.Cos(later.Item3 - earlier.Rotation));
                return (Vector3.Lerp(earlier.Position, later.Item2, f), earlier.Rotation + delta * f);
            }
            earlier = (later.Item1, later.Item2, later.Item3);
        }
        return (livePosition, liveRotation);
    }
}

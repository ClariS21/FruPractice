using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;

namespace AnoMech.Core.Game.Guidance;

public enum PathSafety { Safe, Near, Clip }

public abstract record PathFinding(float Time, Vector2 Position)
{
    public abstract string Text { get; }
    public sealed record Clip(string HazardLabel, float At, Vector2 Point) : PathFinding(At, Point)
    {
        public override string Text => $"clipped: {HazardLabel} at {Time:F1}s";
    }
    public sealed record Late(int SpotIndex, float SecondsLate, float At, Vector2 Point) : PathFinding(At, Point)
    {
        public override string Text => $"late: left {SecondsLate:F1}s after the ideal departure (spot {SpotIndex + 1})";
    }
    public sealed record Wide(int SpotIndex, float Distance, float At, Vector2 Point) : PathFinding(At, Point)
    {
        public override string Text => $"wide: {Distance:F1}y from spot {SpotIndex + 1} at its deadline";
    }
}

public static class PathDebrief
{
    public static Vector2? PositionAt(IReadOnlyList<(float t, Vector2 p)> path, float time)
    {
        if (path.Count == 0 || time < path[0].t || time > path[^1].t) return null;
        for (var i = 1; i < path.Count; i++)
        {
            if (path[i].t < time) continue;
            var (t, p) = path[i - 1];
            return path[i].t <= t ? path[i].p : Vector2.Lerp(p, path[i].p, (time - t) / (path[i].t - t));
        }
        return path[^1].p;
    }

    public static IReadOnlyList<PathFinding> Analyze(IReadOnlyList<(float t, Vector2 p)> path,
        IReadOnlyList<StratTarget> spots, IEnumerable<GuidanceHazard> hazards)
    {
        var findings = new List<PathFinding>();
        foreach (var h in hazards.Distinct())
            if (PositionAt(path, h.FiresAt) is { } p && h.Shape.SignedDistance(p) <= 0)
                findings.Add(new PathFinding.Clip(h.Label, h.FiresAt, p));
        for (var index = 0; index < spots.Count; index++)
        {
            var s = spots[index];
            if (s.Deadline is not { } deadline || PositionAt(path, deadline) is not { } arrival) continue;
            var target = GuidanceLogic.Xz(s.Spot);
            var wide = Vector2.Distance(arrival, target);
            if (wide > 1.5f) findings.Add(new PathFinding.Wide(index, wide, deadline, arrival));
            if (PositionAt(path, s.IssuedAt) is not { } start) continue;
            var distance = Vector2.Distance(start, target);
            if (distance <= 1.5f) continue;
            var departure = deadline;
            var previous = (t: s.IssuedAt, p: start);
            foreach (var sample in path.Where(p => p.t > s.IssuedAt && p.t <= deadline))
            {
                // Ignore sub-centimetre drift; departure is the start of the first approach segment.
                if (Vector2.Distance(previous.p, target) - Vector2.Distance(sample.p, target) > 0.01f)
                {
                    departure = previous.t;
                    break;
                }
                previous = sample;
            }
            var late = departure - GuidanceLogic.IdealDeparture(deadline, distance);
            if (late > 0.05f)
                findings.Add(new PathFinding.Late(index, late, departure, PositionAt(path, departure) ?? start));
        }
        return findings.OrderBy(f => f.Time).ToArray();
    }

    public static IReadOnlyList<PathSafety> Colors(IReadOnlyList<(float t, Vector2 p)> path, IEnumerable<GuidanceHazard> hazards)
    {
        var colors = new PathSafety[path.Count];
        foreach (var h in hazards)
        {
            if (PositionAt(path, h.FiresAt) is not { } position) continue;
            var distance = h.Shape.SignedDistance(position);
            var safety = distance <= 0 ? PathSafety.Clip : distance <= 1 ? PathSafety.Near : PathSafety.Safe;
            if (safety == PathSafety.Safe) continue;
            var index = 0;
            for (var i = 1; i < path.Count; i++)
                if (MathF.Abs(path[i].t - h.FiresAt) < MathF.Abs(path[index].t - h.FiresAt)) index = i;
            if (safety > colors[index]) colors[index] = safety;
        }
        return colors;
    }
}

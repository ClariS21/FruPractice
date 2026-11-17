using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;

namespace AnoMech.Core.Game.Guidance;

public sealed record DangerSegment(Vector2 From, Vector2 To, bool Following);
public sealed record GuidanceFrame(StratTarget? Next, StratTarget? Following, float? GoAt,
    bool ShowGo, bool InDangerNow, IReadOnlyList<DangerSegment> DangerSegments);

public static class GuidanceLogic
{
    public const float Speed = 6f;
    public static Vector2 Xz(Vector3 point) => new(point.X, point.Z);
    public static float IdealDeparture(float deadline, float distance) => deadline - 0.8f - distance / Speed;

    public static GuidanceFrame Compute(PartyRole role, Vector2 position, float now, StratTrail trail,
        IEnumerable<GuidanceHazard> hazards)
    {
        var issued = trail.Entries.Where(s => s.Role == role && s.IssuedAt <= now).ToArray();
        var pending = issued.Where(s => (s.Deadline ?? s.IssuedAt) >= now)
            .OrderBy(s => s.Deadline ?? s.IssuedAt).ToArray();
        var next = pending.FirstOrDefault() ?? issued.LastOrDefault();
        var following = pending.Skip(1).FirstOrDefault();
        var upcoming = hazards.Where(h => h.FiresAt >= now).ToArray();
        var danger = upcoming.Any(h => h.FiresAt <= now + 1.5f && h.Shape.SignedDistance(position) <= 0);
        var segments = new List<DangerSegment>();
        float? goAt = null;
        var distance = 0f;
        if (next is not null)
        {
            var spot = Xz(next.Spot);
            distance = Vector2.Distance(position, spot);
            goAt = IdealDeparture(next.Deadline ?? next.IssuedAt, distance) - 0.5f;
            AddDanger(segments, position, spot, now,
                following is null ? upcoming : upcoming.Where(h => h.FiresAt <= now + distance / Speed), false);
            if (following is not null)
                AddDanger(segments, spot, Xz(following.Spot), now + distance / Speed, upcoming, true);
        }
        return new(next, following, goAt, goAt <= now && distance > 0.75f, danger, segments);
    }

    private static void AddDanger(List<DangerSegment> segments, Vector2 from, Vector2 to, float starts,
        IEnumerable<GuidanceHazard> hazards, bool following)
    {
        var length = Vector2.Distance(from, to);
        if (length < 0.001f) return;
        var direction = (to - from) / length;
        foreach (var h in hazards)
        {
            if (h.FiresAt < starts) continue;
            var predicted = from + direction * Math.Clamp((h.FiresAt - starts) * Speed, 0, length);
            if (h.Shape.SignedDistance(predicted) > 0 || h.Shape is not GuidanceShape.Circle circle) continue;
            var along = Vector2.Dot(circle.Center - from, direction);
            var perpendicularSquared = Vector2.DistanceSquared(circle.Center, from + direction * along);
            var half = MathF.Sqrt(MathF.Max(0, circle.Radius * circle.Radius - perpendicularSquared));
            segments.Add(new(from + direction * Math.Clamp(along - half, 0, length),
                from + direction * Math.Clamp(along + half, 0, length), following));
        }
    }
}

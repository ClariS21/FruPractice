using System;
using System.Numerics;
using AnoMech.Core.Game.Guidance;

namespace AnoMech.Scenarios.Fru.P5Full;

// A rectangle laid from `Origin` along `Forward` (unit) for `Length`, `HalfWidth` either side: the
// shape of a sheet rectangle action.
public sealed record GuidanceRect(Vector2 Origin, Vector2 Forward, float Length, float HalfWidth) : GuidanceShape
{
    public override float SignedDistance(Vector2 point)
    {
        var offset = point - Origin;
        var u = Vector2.Dot(offset, Forward) - Length / 2f;
        var v = Vector2.Dot(offset, new Vector2(-Forward.Y, Forward.X));
        var dx = MathF.Abs(u) - Length / 2f;
        var dy = MathF.Abs(v) - HalfWidth;
        return new Vector2(MathF.Max(dx, 0f), MathF.Max(dy, 0f)).Length() + MathF.Min(MathF.Max(dx, dy), 0f);
    }
}

// A cone from `Apex` out to `Radius`, `HalfAngle` either side of `Bearing` (radians, 0 = +Z, the
// sim's facing convention).
public sealed record GuidanceCone(Vector2 Apex, float Bearing, float HalfAngle, float Radius) : GuidanceShape
{
    public override float SignedDistance(Vector2 point)
    {
        var offset = point - Apex;
        var r = offset.Length();
        if (r < 1e-4f) return -MathF.Min(Radius, 0f);
        var angle = MathF.Abs(Wrap(MathF.Atan2(offset.X, offset.Y) - Bearing));
        if (angle <= HalfAngle)
            return -MathF.Min(Radius - r, r * MathF.Sin(MathF.Min(HalfAngle - angle, MathF.PI / 2f)));
        var past = angle - HalfAngle;
        return past >= MathF.PI / 2f ? r : r * MathF.Sin(past);
    }

    private static float Wrap(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }
}

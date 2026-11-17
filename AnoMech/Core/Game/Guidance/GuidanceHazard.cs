using System.Collections.Generic;
using System.Numerics;

namespace AnoMech.Core.Game.Guidance;

public interface IGuidanceHazardSource
{
    IEnumerable<GuidanceHazard> Upcoming(float now, float horizon);
}

public abstract record GuidanceShape
{
    public abstract float SignedDistance(Vector2 point);

    public sealed record Circle(Vector2 Center, float Radius) : GuidanceShape
    {
        public override float SignedDistance(Vector2 point) => Vector2.Distance(point, Center) - Radius;
    }
}

public sealed record GuidanceHazard(GuidanceShape Shape, float FiresAt, string Label);

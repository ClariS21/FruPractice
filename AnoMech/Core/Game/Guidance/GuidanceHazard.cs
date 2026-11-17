using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;

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

    // The area the sim resolves the hit with.
    public sealed record Area(AoeQuery Query) : GuidanceShape
    {
        public override float SignedDistance(Vector2 point)
            => Query.SignedDistance(new Vector3(point.X, 0f, point.Y)) ?? float.PositiveInfinity;

        // Round moving origins and facing so repeated frames retain one hazard in the guide's set.
        private (uint, int, int, int, float?, AnoMech.Core.Native.Interfaces.CastType?, float) Key =>
            (Query.ActionId, (int)MathF.Round(Query.Source.Position.X * 100f),
                (int)MathF.Round(Query.Source.Position.Z * 100f),
                (int)MathF.Round((Query.Source.Rotation + Query.OmenRotate) * 100f),
                Query.Size, Query.CastTypeOverride, Query.CasterHitboxRadius);

        public bool Equals(Area? other) => other is not null && Key == other.Key;

        public override int GetHashCode() => Key.GetHashCode();
    }
}

public sealed record GuidanceHazard(GuidanceShape Shape, float FiresAt, string Label);

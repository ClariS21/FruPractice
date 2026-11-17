using System;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Fru;

// P4's guide hazards. Each is built from the area query EnemyActionHandler resolves the cast with (the same origin, turn,
// size and caster hitbox), so the shape the guide reports is the one that hits, at the moment it snapshots.
public static class FruP4Hazards
{
    // A cast on a player who is still on the move: its area follows them until it goes off, then stays where it went off.
    // A scenario keeps one per cast for the whole run, so every report of it is the same hazard: the guide keeps each
    // hazard it is shown, and would otherwise keep one per frame, each where the player stood then.
    public sealed class Mark
    {
        // Its own player, who takes it on purpose, isn't warned off it: the guide can't tell whose seat it's guiding.
        internal const float OwnSpot = 1f;

        private readonly SimCharacter target;
        private readonly EnemyAction action;

        public Mark(SimCharacter target, EnemyAction action, float castAt, string label, bool takenByTarget = true)
        {
            this.target = target;
            this.action = action;
            TakenByTarget = takenByTarget;
            Hazard = new(new OnMark(this), castAt + SnapshotAfter(action), label);
        }

        public bool TakenByTarget { get; }

        public GuidanceHazard Hazard { get; }
        public Placement? WentOffAt { get; private set; }

        // A dead player's cast never comes; one that went off stays reported.
        public bool Live => WentOffAt is not null || target.IsAlive();

        internal AoeQuery Query => new(action.ActionId, WentOffAt ?? target.Placement(), action.Cast.Rotation + action.Area.Rotation,
            action.Area.Size, action.Area.CastTypeOverride, 0f);

        // From the scenario's OnActionResolved: fixes the area where this cast went off.
        public void Resolved(ActionResolved resolved)
        {
            if (WentOffAt is null && resolved.Action.ActionId == action.ActionId && ReferenceEquals(resolved.Target, target)) WentOffAt = resolved.Origin;
        }
    }

    public sealed record OnMark(Mark Mark) : GuidanceShape
    {
        public Vector2 Center => new(Mark.Query.Source.Position.X, Mark.Query.Source.Position.Z);

        public override float SignedDistance(Vector2 point)
            => Mark.TakenByTarget && Vector2.Distance(point, Center) < Mark.OwnSpot
                ? float.PositiveInfinity
                : Mark.Query.SignedDistance(new Vector3(point.X, 0f, point.Y)) ?? float.PositiveInfinity;
    }

    // From a cast's start to its snapshot.
    public static float SnapshotAfter(EnemyAction action)
        => (action.Cast.CastSecondsOverride ?? MathF.Max(0f, (Natives.Data.Action(action.ActionId)?.CastSeconds ?? 0f) - CastSpec.ActionEffectOffset))
           + action.Timing.ResolveSnapshotOffset;

    // A cast on a ground spot: its area turns the way the caster faces.
    public static GuidanceHazard AtLocation(EnemyAction action, float castAt, string label, SimCharacter caster, Vector3 location)
        => Of(action, castAt, label, new Placement(location, caster.Rotation), caster);

    // A cast on nothing: its area is the caster's own, from where it stands, or from `at` where it is put to cast.
    public static GuidanceHazard FromCaster(EnemyAction action, float castAt, string label, SimCharacter caster, Placement? at = null)
        => Of(action, castAt, label, at ?? caster.Placement(), caster, fromCaster: true);

    private static GuidanceHazard Of(EnemyAction action, float castAt, string label, Placement origin, SimCharacter caster, bool fromCaster = false)
    {
        var hitbox = fromCaster || origin.Position == caster.Position ? caster.HitboxRadius : 0f;
        var query = new AoeQuery(action.ActionId, origin, action.Cast.Rotation + action.Area.Rotation, action.Area.Size, action.Area.CastTypeOverride, hitbox);
        var castType = action.Area.CastTypeOverride ?? Natives.Data.Action(action.ActionId)?.CastType;
        GuidanceShape shape = castType is CastType.Circle or CastType.Circle2 or CastType.Custom
            ? new GuidanceShape.Circle(new Vector2(origin.Position.X, origin.Position.Z), -(query.SignedDistance(origin.Position) ?? 0f))
            : new GuidanceShape.Area(query);
        return new(shape, castAt + SnapshotAfter(action), label);
    }
}

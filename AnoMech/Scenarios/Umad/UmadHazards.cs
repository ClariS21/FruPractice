using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Umad;

public interface IUmadHazardSource : IGuidanceHazardSource, IGuidanceCastObserver
{
    UmadHazards GuidanceHazards { get; }

    IEnumerable<GuidanceHazard> IGuidanceHazardSource.Upcoming(float now, float horizon)
        => GuidanceHazards.Upcoming(now, horizon);

    void IGuidanceCastObserver.CastStarted(EnemyAction action, SimCharacter caster, SimCharacter? target, Vector3? location, float firesAt)
        => GuidanceHazards.CastStarted(action, caster, target, location, firesAt);

    void IGuidanceCastObserver.CastResolved(EnemyAction action, SimCharacter caster, Placement origin)
        => GuidanceHazards.CastResolved(action, caster, origin);
}

// A DMU scenario's guide hazards, read off the casts the sim runs: every positional action it casts is reported from
// its cast's start with the area query the sim resolves it with (same origin, turn, size and caster hitbox) and its
// snapshot time, so the shape the guide draws is the one that hits. One object per cast, so the guide's set keeps
// exactly one hazard per cast. Raidwides, tank busters, stacks and towers are left out.
public sealed class UmadHazards
{
    // The positional actions the guide warns about. Compared by reference: DMU reuses an action id for a stack and a spread.
    private static readonly HashSet<EnemyAction> Positional = new(ReferenceEqualityComparer.Instance)
    {
        UmadActions.SlapHappySlap, UmadActions.SlapHappyFinalSlap, UmadActions.ShockwaveCone, UmadActions.ImplosionShockwave,
        UmadActions.DamningEdict, UmadActions.LookUponMeAndDespair, UmadActions.BlizzardIII, UmadActions.Nothingness,
        UmadActions.StrayFlames, UmadActions.StraySpray, UmadActions.CrystalInferno, UmadActions.CrystalTsunami,
        UmadActions.ThunderIIIBlast, UmadActions.BigBang, UmadActions.UltimaBlasterCharge,
        UmadActions.BlizzardIIIBlowout, UmadActions.BlizzardIIIBlowoutLie, UmadActions.ThrummingThunderIII, UmadActions.ThrummingThunderIIILie,
        UmadActions.EdgeOfDeath, UmadActions.WhiteAntilight, UmadActions.WhiteAntilightLie, UmadActions.BlackAntilight, UmadActions.BlackAntilightLie,
        UmadActions.DeathBoltSpread, UmadActions.DeathWaveSpread,
        UmadActions.StrayFlamesChariot, UmadActions.StrayFlamesDonut, UmadActions.StraySprayDonut, UmadActions.StraySprayChariot,
        UmadActions.CatastrophicChoiceAero, UmadActions.CatastrophicChoiceEarth,
        UmadActions.ExaflareHit, UmadActions.ExaflareSpread, UmadActions.FloodWave,
        UmadActions.ForsakenGround, UmadActions.ForsakenOrange,
    };

    // The one who takes a cast aimed at them on purpose isn't warned off it, as FRU's Mark.
    private const float OwnSpot = 1f;

    private sealed class Cast(SimCharacter caster, SimCharacter? target, Vector3? location, EnemyAction action)
    {
        public readonly SimCharacter Caster = caster;
        // Null for a cast on nothing or on its own caster.
        public readonly SimCharacter? Target = target == caster ? null : target;
        public readonly Vector3? Location = location;
        public readonly EnemyAction Action = action;
        public Placement? Resolved;
        public float ResolvedHitbox;
        public bool Following => Target is not null && Resolved is null;
        public bool Taken => Target is not null && !Directional;

        private bool Directional => (Action.Area.CastTypeOverride ?? Natives.Data.Action(Action.ActionId)?.CastType)
            is CastType.Cone2 or CastType.Cone or CastType.Rectangle2 or CastType.Rectangle or CastType.Charge;

        public Placement Origin
        {
            get
            {
                if (Resolved is { } at) return at;
                if (Target is { } target) return Directional ? Caster.Placement().Face(target.Position) : target.Placement();
                return Location is { } location ? new Placement(location, Caster.Rotation) : Caster.Placement();
            }
        }

        public AoeQuery Query
        {
            get
            {
                var origin = Origin;
                var hitbox = Resolved is not null ? ResolvedHitbox : origin.Position == Caster.Position ? Caster.HitboxRadius : 0f;
                return new(Action.ActionId, origin, Action.Cast.Rotation + Action.Area.Rotation, Action.Area.Size, Action.Area.CastTypeOverride, hitbox);
            }
        }
    }

    private sealed record OnCast(Cast Cast) : GuidanceShape
    {
        public override float SignedDistance(Vector2 point)
        {
            var origin = Cast.Origin.Position;
            if (Cast.Taken && Vector2.Distance(point, new(origin.X, origin.Z)) < OwnSpot) return float.PositiveInfinity;
            return Cast.Query.SignedDistance(new(point.X, 0f, point.Y)) ?? float.PositiveInfinity;
        }
    }

    private readonly List<(Cast Cast, GuidanceHazard Hazard)> casts = [];

    // Hazards a replay reports by hand, for a scenario whose casts resolve nothing.
    private readonly List<GuidanceHazard> fixedHazards = [];

    public void Clear()
    {
        casts.Clear();
        fixedHazards.Clear();
    }

    public void Add(GuidanceHazard hazard) => fixedHazards.Add(hazard);

    public void CastStarted(EnemyAction action, SimCharacter caster, SimCharacter? target, Vector3? location, float firesAt)
    {
        if (!Positional.Contains(action)) return;
        var cast = new Cast(caster, target, location, action);
        casts.Add((cast, new GuidanceHazard(new OnCast(cast), firesAt, Natives.Data.ActionName(action.ActionId))));
    }

    public void CastResolved(EnemyAction action, SimCharacter caster, Placement origin)
    {
        foreach (var (cast, _) in casts)
        {
            if (cast.Resolved is not null || !ReferenceEquals(cast.Caster, caster) || !ReferenceEquals(cast.Action, action)) continue;
            cast.Resolved = origin;
            cast.ResolvedHitbox = origin.Position == caster.Position ? caster.HitboxRadius : 0f;
            return;
        }
    }

    // A cast on a player follows them until it goes off, so it's only shown once near.
    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
    {
        foreach (var hazard in fixedHazards)
            if (hazard.FiresAt >= now && hazard.FiresAt <= now + horizon) yield return hazard;
        foreach (var (cast, hazard) in casts)
            if (hazard.FiresAt >= now && hazard.FiresAt <= now + horizon && (!cast.Following || HazardReveal.Near(hazard.FiresAt, now)))
                yield return hazard;
    }
}

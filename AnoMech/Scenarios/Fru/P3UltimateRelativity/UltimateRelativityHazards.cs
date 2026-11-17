using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.P3UltimateRelativity.FruP3UltimateRelativityState;

namespace AnoMech.Scenarios.Fru.P3UltimateRelativity;

// The positional hazards of Ultimate Relativity, from the sheet's shapes and the scenario's times:
// - Dark Fire III, a circle around each fire holder, at its wave's FireAt;
// - Sinbound Meltdown, a 5 wide line from an hourglass: the first shot (60 long) at whoever stands nearest it,
//   then nine sweeping shots (50 long), each turned a further 15 degrees;
// - Dark Eruption, a circle around each eruption holder, at FinalAt, where the rewind has put it.
// Stacks (Unholy Darkness, Dark Water, Shell Crusher), the Dark Blizzard hole, raidwides and the gaze are not
// positional hazards. The circles around a player are left out for the human seat itself: it stands in its own.
public sealed class UltimateRelativityHazards(FruP3UltimateRelativityState state, SimWorld world, float startsAt,
    Func<int, Vector3?> laserDirection, Func<PartyRole, Vector3?> rewoundTo) : IGuidanceHazardSource
{
    // The sim resolves an ability on the frame after its time.
    private const float Settled = 0.1f;

    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
    {
        for (var wave = 0; wave < 3; wave++)
        {
            var fireAt = startsAt + FireAt(wave);
            if (InRange(fireAt, now, horizon) && HazardReveal.Near(fireAt, now))
                foreach (var role in Enum.GetValues<PartyRole>().Where(role => state.HasFire(role, wave)))
                    if (Around(role) is { } at)
                        yield return new(new GuidanceShape.Circle(at, Radius(FruConstants.ActionId.DarkFire)), fireAt, "Dark Fire");
            var fired = now >= startsAt + LaserHitAt(wave, 0) + Settled;
            for (var shot = 0; shot < LaserShots; shot++)
            {
                var hitAt = startsAt + LaserHitAt(wave, shot);
                if (!InRange(hitAt, now, horizon)) continue;
                // The first shot goes where the nearest player stands, known only at the end; the sweeps turn from it.
                if (shot == 0 ? !HazardReveal.Near(hitAt, now) : !fired) continue;
                for (var clock = 0; clock < 8; clock++)
                    if (Wave(clock) == wave && Beam(clock, shot, fired) is { } beam)
                        yield return new(beam, hitAt, "Sinbound Meltdown");
            }
        }
        var finalAt = startsAt + FinalAt;
        if (!InRange(finalAt, now, horizon) || !HazardReveal.Near(finalAt, now)) yield break;
        foreach (var role in Enum.GetValues<PartyRole>().Where(role => !state.Shadoweye(role) && !state.DarkWater(role)))
            if (Around(role, rewound: true) is { } at)
                yield return new(new GuidanceShape.Circle(at, Radius(FruConstants.ActionId.DarkEruption)), finalAt, "Dark Eruption");
    }

    private static bool InRange(float at, float now, float horizon) => at >= now && at <= now + horizon;

    private static float Radius(uint actionId) => Natives.Data.Action(actionId)!.EffectRange;

    // Where a player-centred circle will be: the rewound spot, once there is one, else where the player stands.
    private Vector2? Around(PartyRole role, bool rewound = false)
    {
        if (role == world.Party.PlayerRole || world.Party.Get(role) is not { } member || !member.IsAlive()) return null;
        var at = rewound && rewoundTo(role) is { } back ? back : member.JudgedPosition;
        return HazardReveal.Snap(at);
    }

    private GuidanceShape? Beam(int clock, int shot, bool fired)
    {
        var origin = state.Hourglass(clock);
        var direction = fired ? laserDirection(clock) : NearestTo(origin);
        if (direction is not { } d || d.LengthSquared() < 0.0001f) d = -state.Direction(clock);
        if (shot > 0)
        {
            var angle = (state.Clockwise(clock) ? 1 : -1) * shot * MathF.PI / 12f;
            d = new Vector3(d.X * MathF.Cos(angle) - d.Z * MathF.Sin(angle), 0f, d.X * MathF.Sin(angle) + d.Z * MathF.Cos(angle));
        }
        var action = shot == 0 ? FruConstants.ActionId.SinboundMeltdownFirst : FruConstants.ActionId.SinboundMeltdownRest;
        // The facing is rounded to a hundredth of a radian, as the shape compares it.
        return new GuidanceShape.Area(new AoeQuery(action, new Placement(origin, MathF.Round(MathF.Atan2(d.X, d.Z) * 100f) / 100f)));
    }

    // The first shot goes at whoever stands nearest the hourglass.
    private Vector3? NearestTo(Vector3 origin)
    {
        var bait = world.Party.ActiveMembers().MinBy(m => Vector3.DistanceSquared(m.JudgedPosition, origin));
        if (bait is null) return null;
        var offset = bait.JudgedPosition - origin;
        return offset.LengthSquared() > 0.0001f ? Vector3.Normalize(offset) : null;
    }
}

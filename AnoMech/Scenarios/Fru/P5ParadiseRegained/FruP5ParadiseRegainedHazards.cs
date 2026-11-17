using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P5Full;
using static AnoMech.Scenarios.Fru.P5ParadiseRegained.FruP5ParadiseRegainedState;

namespace AnoMech.Scenarios.Fru.P5ParadiseRegained;

// What a player must stay out of: each wing's cleave, a cone off Pandora's facing that goes off as the
// wing's tower does, and each Polarizing Strikes line and its echo, a 6 wide rectangle from the helper
// along the way it was aimed. A line is reported once aimed (0.1 s before its bar), since the baits
// decide where it goes. Towers are soaked, not avoided, and the buster hits one tank.
public sealed class FruP5ParadiseRegainedHazards(
    float startsAt, Func<SimEnemy?> pandora, Func<PartyRole, SimCharacter?> member,
    Func<int, int, SimEnemy?> lineHelper, Func<int, bool> aimed) : IGuidanceHazardSource
{
    // Where each wing's cleave goes off, and when Pandora turns to her target for it.
    public static float CleaveAt(int wing) => wing == 0 ? TowerHitAt(0) : 19.4f;
    public static float FaceAt(int wing) => wing == 0 ? 15.5f : 19.4f;
    public static PartyRole CleaveTarget(int wing) => wing == 0 ? PartyRole.MainTank : PartyRole.OffTank;
    // The first hit of a line snapshots as its 0.5 s bar ends, less the action effect's offset.
    public const float LineFirstHitAfter = 0.5f - 0.3f;
    private const float LineHalfWidth = 3f;
    private const float LineLength = 100f;

    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
    {
        for (var wing = 0; wing < 2; wing++)
        {
            var at = startsAt + CleaveAt(wing);
            if (at < now || at > now + horizon || !HazardReveal.Near(at, now) || pandora() is not { } boss) continue;
            var dark = wing == 0 ? DarkWingFirst?.Invoke() ?? false : !(DarkWingFirst?.Invoke() ?? false);
            var action = dark ? FruActions.WingsCleaveDark : FruActions.WingsCleaveLight;
            var facing = now >= startsAt + FaceAt(wing) || member(CleaveTarget(wing)) is not { } target
                ? boss.Rotation
                : MathF.Atan2(target.Position.X - boss.Position.X, target.Position.Z - boss.Position.Z);
            var query = new AoeQuery(action.ActionId, new Placement(boss.Position, facing), action.Cast.Rotation,
                action.Area.Size, action.Area.CastTypeOverride, boss.HitboxRadius);
            yield return new(new GuidanceShape.Area(query), at, "Wings cleave");
        }
        for (var round = 0; round < 4; round++)
        {
            if (!aimed(round)) continue;
            foreach (var at in new[] { startsAt + LinesAt(round) + LineFirstHitAfter, startsAt + EchoAt(round) })
            {
                if (at < now || at > now + horizon) continue;
                for (var side = 0; side < 2; side++)
                    if (lineHelper(round, side) is { } helper)
                        yield return new(new GuidanceRect(new Vector2(helper.Position.X, helper.Position.Z),
                            new Vector2(MathF.Sin(helper.Rotation), MathF.Cos(helper.Rotation)), LineLength, LineHalfWidth), at, "Cruel Path");
            }
        }
    }

    // Set by the scenario: whether the first wing is dark.
    public Func<bool>? DarkWingFirst { get; init; }
}

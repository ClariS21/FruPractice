using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game.Guidance;
using AnoMech.Scenarios.Fru.P5Full;

namespace AnoMech.Scenarios.Fru.P5FulgentBlade;

// The exaline strips: a rectangle 5 long and 80 wide laid from each seam, a light one one way and a
// dark one the other, rolling 5 y further every hit. Each group's first hit is the 7 s bar's snapshot;
// the rest are instant casts that snapshot as they go off.
public sealed class FruP5FulgentBladeHazards(FruP5FulgentBladeState state, float startsAt) : IGuidanceHazardSource
{
    // The strips' own bars start these long into the drill.
    public static readonly float[] FirstStripCastAt = [11.55f, 15.6f, 19.55f];
    private const float FirstStripBar = 7f - 0.3f;
    private const float StripLength = 5f;
    private const float StripHalfWidth = 40f;

    public static float FiresAt(int group, int hit) => hit == 0 ? FirstStripCastAt[group] + FirstStripBar : FruP5FulgentBladeState.HitAt(group, hit);

    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
    {
        for (var group = 0; group < FruP5FulgentBladeState.GroupCount; group++)
            for (var hit = 0; hit < FruP5FulgentBladeState.MaxHitCount; hit++)
            {
                var at = startsAt + FiresAt(group, hit);
                if (at < now || at > now + horizon) continue;
                for (var seam = 0; seam < FruP5FulgentBladeState.SeamsPerGroup; seam++)
                    for (var light = 0; light < 2; light++)
                    {
                        if (state.Strip(group, seam, light == 1, hit) is not { } strip) continue;
                        var forward = new Vector2(MathF.Sin(strip.Rotation), MathF.Cos(strip.Rotation));
                        yield return new(new GuidanceRect(new Vector2(strip.Position.X, strip.Position.Z), forward, StripLength, StripHalfWidth), at, "Exaline");
                    }
            }
    }
}

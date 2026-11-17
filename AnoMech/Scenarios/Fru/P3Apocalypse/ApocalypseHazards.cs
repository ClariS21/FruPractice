using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Scenarios.Fru.P3Apocalypse;

public sealed class ApocalypseHazards(FruP3ApocalypseState state, float startsAt) : IGuidanceHazardSource
{
    public static float Radius => Natives.Data.Action(FruActions.Apocalypse.ActionId)!.EffectRange;
    public static float SnapshotOffset =>
        (FruActions.Apocalypse.Cast.CastSecondsOverride
         ?? MathF.Max(0, Natives.Data.Action(FruActions.Apocalypse.ActionId)!.CastSeconds - CastSpec.ActionEffectOffset))
        + FruActions.Apocalypse.Timing.ResolveSnapshotOffset;

    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
    {
        for (var wave = 0; wave < FruP3ApocalypseState.WaveCount; wave++)
        {
            var at = startsAt + FruP3ApocalypseState.WaveAt(wave) + SnapshotOffset;
            if (at < now || at > now + horizon) continue;
            foreach (var p in state.Explosions(wave))
                yield return new(new GuidanceShape.Circle(new Vector2(p.X, p.Z), Radius), at, "Apocalypse");
        }
    }
}


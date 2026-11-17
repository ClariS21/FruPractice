using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Fru.P3Apocalypse;

// Apocalypse's lights, and with a world its two player-centred circles: Spirit Taker's (it knocks back
// everyone in it) at its target, and the spread of Dark Eruption on every member. The circles around the human
// seat itself are left out: it stands in its own. Dark Water's stacks are not positional hazards.
public sealed class ApocalypseHazards(FruP3ApocalypseState state, float startsAt, SimWorld? world = null) : IGuidanceHazardSource
{
    public const float SpiritTakerAt = 24.8f;
    public const float DarkEruptionAt = 36f;

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
        if (world is null) yield break;
        var spiritAt = startsAt + SpiritTakerAt;
        if (spiritAt >= now && spiritAt <= now + horizon && HazardReveal.Near(spiritAt, now) && Around(state.SpiritTakerTarget) is { } target)
            yield return new(new GuidanceShape.Circle(target, EffectRange(FruConstants.ActionId.SpiritTaker)), spiritAt, "Spirit Taker");
        var eruptionAt = startsAt + DarkEruptionAt;
        if (eruptionAt < now || eruptionAt > now + horizon || !HazardReveal.Near(eruptionAt, now)) yield break;
        foreach (var role in Enum.GetValues<PartyRole>())
            if (Around(role) is { } at)
                yield return new(new GuidanceShape.Circle(at, EffectRange(FruConstants.ActionId.DarkEruption)), eruptionAt, "Dark Eruption");
    }

    private static float EffectRange(uint actionId) => Natives.Data.Action(actionId)!.EffectRange;

    private Vector2? Around(PartyRole role)
    {
        if (role == world!.Party.PlayerRole || world.Party.Get(role) is not { } member || !member.IsAlive()) return null;
        return HazardReveal.Snap(member.JudgedPosition);
    }
}

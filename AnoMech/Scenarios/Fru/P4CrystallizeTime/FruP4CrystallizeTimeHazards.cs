using System.Collections.Generic;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.FruP4Hazards;
using Actions = AnoMech.Scenarios.Fru.FruActions;

namespace AnoMech.Scenarios.Fru.P4CrystallizeTime;

// What a player has to stay out of: the hourglasses' Maelstrom, the Tidal Light strips, the Ice debuffs' donuts, the
// Eruption's circle and Spirit Taker. Dark Aero's knockback is part of the strat (it throws the Ice and Water across), the
// stacks (Dark Water, Unholy Darkness, Akh Morn) are taken, the heads go off on whoever they touch, and Hallowed Wings lands
// during the stun, on the spots the rewind took everyone back to.
public sealed partial class FruP4CrystallizeTimeScenario : IGuidanceHazardSource
{
    private readonly List<FruP4Hazards.Mark> marks = [];

    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
    {
        if (world is null) yield break;
        foreach (var hazard in Hazards())
            if (hazard.FiresAt >= now && hazard.FiresAt <= now + horizon) yield return hazard;
    }

    private IEnumerable<GuidanceHazard> Hazards()
    {
        for (var wave = 0; wave < MaelstromAt.Length; wave++)
            foreach (var i in state.HourglassPair(wave))
                if (hourglasses[i] is { } hourglass) yield return FromCaster(Actions.Maelstrom, startedAt + MaelstromAt[wave], "Maelstrom", hourglass);
        for (var set = 0; set < 2; set++)
            for (var step = 0; step < 4; step++)
                if (tidalHelpers[set * 4 + step] is { } strip)
                    yield return FromCaster(step == 0 ? Actions.TidalLightFirst : Actions.TidalLightRest,
                        startedAt + (set == 0 ? FirstTidalLightAt : SecondTidalLightAt) + TidalLightStepAfter(step), "Tidal Light", strip,
                        Strip(set == 1, step));
        foreach (var mark in marks)
            if (mark.Live) yield return mark.Hazard;
    }

    // The blasts on players: each Ice's donut and the Eruption's circle at Fireworks, and Spirit Taker on its target.
    private void MarkPlayers()
    {
        marks.Clear();
        foreach (var (debuff, action, label) in new[]
                 {
                     (CrystallizeDebuff.RedIce, Actions.DarkBlizzard, "Dark Blizzard"), (CrystallizeDebuff.BlueIce, Actions.DarkBlizzard, "Dark Blizzard"),
                     (CrystallizeDebuff.BlueEruption, Actions.DarkEruption, "Dark Eruption"),
                 })
            foreach (var role in state.Holders(debuff))
                if (world.Party.Get(role) is { } holder)
                    marks.Add(new(holder, action, startedAt + FireworksAt, label, takenByTarget: action != Actions.DarkBlizzard));
        if (world.Party.Get(state.SpiritTakerTarget) is { } target) marks.Add(new(target, Actions.SpiritTaker, startedAt + SpiritTakerAt, "Spirit Taker"));
    }

    private void FixMarks(ActionResolved resolved)
    {
        foreach (var mark in marks) mark.Resolved(resolved);
    }
}

using System.Collections.Generic;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.FruP4Hazards;
using Actions = AnoMech.Scenarios.Fru.FruActions;

namespace AnoMech.Scenarios.Fru.P4DarklitDragonsong;

// What a player has to stay out of: Akh Rhai's puddles, Path of Light's cones, the Usurper's wing and Spirit Taker. The
// towers, Dark Water, Somber Dance and Akh Morn are taken on purpose, and Darklit Dragonsong and Edge of Oblivion hit everyone.
public sealed partial class FruP4DarklitDragonsongScenario : IGuidanceHazardSource
{
    private FruP4Hazards.Mark? spiritTaker;

    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
    {
        if (world is null) yield break;
        foreach (var hazard in Hazards())
            if (hazard.FiresAt >= now && hazard.FiresAt <= now + horizon) yield return hazard;
    }

    private IEnumerable<GuidanceHazard> Hazards()
    {
        // Each puddle drops where a player stood when Akh Rhai began, then goes off ten more times on that spot.
        for (var i = 0; i < akhRhaiPuddles; i++)
        {
            if (akhRhaiHelpers[i] is not { } puddle) continue;
            yield return AtLocation(Actions.AkhRhaiCast, startedAt + AkhRhaiAt, "Akh Rhai", puddle, puddle.Position);
            for (var hit = 0; hit < AkhRhaiRepeats; hit++)
                yield return AtLocation(Actions.AkhRhai, startedAt + AkhRhaiRepeatAt(hit), "Akh Rhai", puddle, puddle.Position);
        }
        // The cones turn to the four players nearest the center just before they go off.
        for (var i = 0; i < pathOfLightBaits; i++)
            if (helpers[2 + i] is { } cone) yield return FromCaster(Actions.PathOfLight, startedAt + PathOfLightAt, "Path of Light", cone);
        if (usurper is { } wing) yield return AtLocation(Wing, startedAt + HallowedWingsAt, "Hallowed Wings", wing, WingAim);
        if (spiritTaker is { Live: true } mark) yield return mark.Hazard;
    }

    private void MarkSpiritTaker()
        => spiritTaker = world.Party.Get(state.SpiritTakerTarget) is { } target ? new(target, Actions.SpiritTaker, startedAt + SpiritTakerAt, "Spirit Taker") : null;
}

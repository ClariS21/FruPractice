using System;
using System.Collections.Generic;

namespace AnoMech.Core.Game.Party;

// When the strat AI has party members start moving, in EventScheduler time, so a bot can tell
// whether a cast would run into a move. A departure scheduled before the AI has chosen who moves
// counts for everyone; one the AI has already decided on (a delayed start) counts for its seat only.
public sealed class DepartureSchedule
{
    private readonly List<(float Time, PartyRole? Seat)> entries = new();

    public void Add(float time, PartyRole? seat = null) => entries.Add((time, seat));

    public void Clear() => entries.Clear();

    // The soonest scheduled departure at or after `now` that concerns `seat`.
    public float? NextAfter(float now, PartyRole? seat = null)
    {
        float? next = null;
        foreach (var (time, who) in entries)
            if (time >= now && (who is null || who == seat) && (next is null || time < next)) next = time;
        return next;
    }
}

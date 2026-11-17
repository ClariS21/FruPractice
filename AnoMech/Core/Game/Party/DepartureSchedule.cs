using System;
using System.Collections.Generic;

namespace AnoMech.Core.Game.Party;

// When the strat AI has party members start moving, in EventScheduler time, so a bot can tell
// whether a cast would run into a move. A departure scheduled before the AI has chosen who moves
// counts for everyone; one the AI has already decided on (a delayed start) counts for its seat only.
// Uptime moves (a bot's own repositioning between mechanics) are marked, so uptime can ask for the
// strat's departures alone.
public sealed class DepartureSchedule
{
    private readonly List<(float Time, PartyRole? Seat, bool Uptime)> entries = new();

    public void Add(float time, PartyRole? seat = null, bool uptime = false) => entries.Add((time, seat, uptime));

    public void Clear() => entries.Clear();

    // The soonest scheduled departure at or after `now` that concerns `seat`.
    public float? NextAfter(float now, PartyRole? seat = null)
    {
        float? next = null;
        foreach (var (time, who, _) in entries)
            if (time >= now && (who is null || who == seat) && (next is null || time < next)) next = time;
        return next;
    }

    // The same, without uptime's own departures: the next time the strat moves `seat`.
    public float? NextStratAfter(float now, PartyRole? seat = null)
    {
        float? next = null;
        foreach (var (time, who, uptime) in entries)
            if (!uptime && time >= now && (who is null || who == seat) && (next is null || time < next)) next = time;
        return next;
    }
}

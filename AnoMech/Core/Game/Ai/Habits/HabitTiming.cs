using System;

namespace AnoMech.Core.Game.Ai.Habits;

internal static class HabitTiming
{
    internal const float Walk = 6.5f;
    internal const float Margin = AiManager.StaggeredArrivalMargin;

    // When a habit-timed bot sets off, all in scenario seconds. `reaction` is the profile's reaction
    // at quantile position `quantile`; `travel` is the walk in seconds. inBy: be at the spot by then;
    // outBy: the current spot turns dangerous then, and leaving it takes exitDistance yalms (default:
    // the whole walk); notBefore: the spot stays dangerous until then.
    internal static float Departure(float time, float knownAt, float reaction, float quantile, float slackUse,
        float travel, float? inBy, float? outBy, float? exitDistance, float? notBefore)
    {
        var latestSafe = MathF.Min(inBy - Margin - travel ?? float.PositiveInfinity,
            outBy - Margin - (exitDistance / Walk ?? travel) ?? float.PositiveInfinity);
        var ready = knownAt + reaction;
        var slack = latestSafe - ready;
        float depart;
        if (float.IsPositiveInfinity(latestSafe)) depart = ready;
        else if (latestSafe < time) depart = time;
        else if (slack >= 0f) depart = ready + Math.Clamp(slackUse, 0f, 1f) * slack;
        // Too tight for the reaction: each bot's reaction quantile places it in what is left, so the
        // party keeps its order and stagger.
        else depart = time + quantile * (latestSafe - time);
        if (notBefore is { } entry) depart = MathF.Max(depart, entry - travel);
        return MathF.Max(time, depart);
    }
}

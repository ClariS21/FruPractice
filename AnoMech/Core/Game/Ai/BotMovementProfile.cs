using AnoMech.Scenarios;

namespace AnoMech.Core.Game.Ai;

internal enum BotArrivalStyle { Early, OnTime }

internal sealed record BotMovementProfile(float Lag, float ReactionMultiplier, BotArrivalStyle Style)
{
    internal static BotMovementProfile Roll(Rng rng)
    {
        var lag = 0.04f + 0.16f * rng.NextSingle();
        var reactionMultiplier = 0.8f + 0.7f * rng.NextSingle();
        var style = rng.NextSingle() < 0.5f ? BotArrivalStyle.Early : BotArrivalStyle.OnTime;
        return new BotMovementProfile(lag, reactionMultiplier, style);
    }

    internal float ArrivalMargin(Rng rng) => Style switch
    {
        BotArrivalStyle.OnTime => AiManager.StaggeredArrivalMargin + 0.05f + 0.4f * rng.NextSingle(),
        _ => AiManager.StaggeredArrivalMargin + 0.05f
    };
}

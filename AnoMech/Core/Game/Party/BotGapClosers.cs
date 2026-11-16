using System.Collections.Generic;
using AnoMech.Core.UserActions;

namespace AnoMech.Core.Game.Party;

internal static class BotGapClosers
{
    public const float Range = 20f;

    public static uint For(byte classJob) => ByJob.GetValueOrDefault((JobId)classJob);

    public static IEnumerable<KeyValuePair<JobId, uint>> All => ByJob;

    private static readonly Dictionary<JobId, uint> ByJob = new()
    {
        [JobId.Dragoon] = 36951,
        [JobId.Monk] = 25762,
        [JobId.Samurai] = 7492,
        [JobId.Reaper] = 24401,
        [JobId.Ninja] = 2262,
        [JobId.Viper] = 34646,
        [JobId.Warrior] = 7386,
        [JobId.Paladin] = 16461,
        [JobId.Gunbreaker] = 36934,
        [JobId.DarkKnight] = 36926,
    };
}

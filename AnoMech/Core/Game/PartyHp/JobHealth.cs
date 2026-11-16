using System;
using AnoMech.Scenarios;

namespace AnoMech.Core.Game.PartyHp;

// Max HP at FRU's level sync, by ClassJob row.
internal static class JobHealth
{
    private const uint Unknown = 150_000;
    private const float Variation = 0.03f;

    public static uint BaseMaxHealth(byte job) => job switch
    {
        19 or 21 or 32 or 37 => 233_000,
        24 => 145_400,
        28 => 143_600,
        33 => 147_000,
        40 => 146_200,
        20 => 158_300,
        22 => 163_200,
        39 => 162_800,
        34 => 162_700,
        41 => 162_800,
        30 => 162_000,
        38 => 160_900,
        23 or 31 => 161_000,
        42 => 147_000,
        35 => 146_700,
        27 => 146_700,
        25 => 147_000,
        _ => Unknown,
    };

    // Gear and food differ from one player to the next.
    public static uint RollMaxHealth(byte job, Rng stream)
        => (uint)MathF.Round(BaseMaxHealth(job) * (1f + (stream.NextSingle() * 2f - 1f) * Variation));
}

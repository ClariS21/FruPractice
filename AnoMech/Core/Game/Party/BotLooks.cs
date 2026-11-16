using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;

namespace AnoMech.Core.Game.Party;

public sealed record EquipmentLook(ushort Id, byte Variant, byte Stain0, byte Stain1);
public sealed record WeaponLook(ushort Id, ushort Type, ushort Variant, byte Stain0, byte Stain1);
public sealed record BotLook(string Name, byte ClassJob, byte[] Customize, EquipmentLook[] Equipment,
    WeaponLook? MainHand, WeaponLook? OffHand, string? Label = null);

public static class BotLooks
{
    private static readonly string[][] MaleFirst =
    [
        ["Aldric", "Brenn", "Corvin", "Darian"], ["Rhun", "Thorin", "Brennan", "Eirik"],
        ["Tataru", "Luludu", "Pipino", "Nanamo"], ["Y'kato", "Y'shato", "Y'rhiko", "Y'mhira"],
        ["Rael", "Varen", "Soren", "Kael"], ["Haran", "Jorin", "Maren", "Tolan"],
        ["Gorak", "Rughan", "Borun", "Zoran"], ["Aren", "Rhen", "Qorin", "Varis"]
    ];
    private static readonly string[][] FemaleFirst =
    [
        ["Elise", "Mira", "Cecile", "Rosalie"], ["Brynja", "Sigrid", "Freya", "Astrid"],
        ["Nanari", "Luluna", "Popori", "Momomo"], ["Y'shtola", "Y'mhasi", "Y'naago", "Y'lyhia"],
        ["Alisa", "Vira", "Seren", "Kaia"], ["Hana", "Juna", "Mira", "Tala"],
        ["Gorza", "Ruzha", "Borja", "Zorha"], ["Ari", "Rhea", "Qira", "Vena"]
    ];
    private static readonly string[][] Last =
    [
        ["Ashford", "Briarwood", "Fairmont", "Westfall"], ["Stonehelm", "Frostwind", "Ironhart", "Stormborn"],
        ["Taru", "Ludu", "Pino", "Namo"], ["Yhisa", "Yrhika", "Ymato", "Ytaro"],
        ["Duskwhisper", "Moonshade", "Silverwind", "Starweaver"], ["Khan", "Boran", "Doran", "Oron"],
        ["Gorak", "Rughan", "Borun", "Zoran"], ["Sae", "Ren", "Qir", "Var"]
    ];

    public const string FileName = "main-tank-look.json";

    // The tank jobs a bot can be given, in the order they are tried.
    private static readonly byte[] TankJobs = [(byte)JobId.Warrior, (byte)JobId.Paladin, (byte)JobId.Gunbreaker, (byte)JobId.DarkKnight];

    public static IReadOnlyList<PartySlot?> Dress(IReadOnlyList<PartySlot?> roster, SimWorld world, BotLook? mainTank)
        => Dress(roster, world, mainTank is null ? [] : [mainTank], null);

    // The main-tank bot wears a look drawn from `pool` (never the `previous` one while there is
    // another), or a random one when the pool is empty. A tank look makes her play that job, any
    // other look the seat's default tank; the off-tank bot then moves off a job she would share.
    public static IReadOnlyList<PartySlot?> Dress(IReadOnlyList<PartySlot?> roster, SimWorld world, IReadOnlyList<BotLook> pool, BotLook? previous)
    {
        var result = new PartySlot?[roster.Count];
        var rng = world.Stream("bot-looks");
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Hana Shirosaki" };
        byte? mainTankJob = null;
        for (var i = 0; i < roster.Count; i++)
        {
            if (roster[i] is not BotSlot bot) { result[i] = roster[i]; continue; }
            if (i == (int)PartyRole.MainTank)
            {
                var look = Pick(pool, rng, previous) ?? RandomLook(rng, "Hana Shirosaki");
                used.Add(look.Name);
                var job = IsTank(look.ClassJob) ? look.ClassJob : bot.Preset.ClassJob;
                mainTankJob = job;
                result[i] = bot with { Preset = bot.Preset with { Name = look.Name, ClassJob = job }, Look = look };
                continue;
            }
            BotLook random;
            do random = RandomLook(rng); while (!used.Add(random.Name));
            result[i] = bot with { Preset = bot.Preset with { Name = random.Name }, Look = random };
        }
        var offTank = (int)PartyRole.OffTank;
        if (mainTankJob is { } taken && result[offTank] is BotSlot ot && ot.Preset.ClassJob == taken)
            result[offTank] = ot with { Preset = ot.Preset with { ClassJob = TankJobs.First(j => j != taken) } };
        return result;
    }

    // One look from the pool; the next one when the draw lands on `previous`. Null for an empty pool.
    public static BotLook? Pick(IReadOnlyList<BotLook> pool, Rng rng, BotLook? previous)
    {
        if (pool.Count == 0) return null;
        var index = rng.Next(pool.Count);
        if (pool.Count > 1 && previous != null && BotLookLibrary.SameLook(pool[index], previous)) index = (index + 1) % pool.Count;
        return pool[index];
    }

    public static bool IsTank(byte job) => job is 19 or 21 or 32 or 37;

    public static bool NameFits(BotLook look)
    {
        var race = look.Customize[0] - 1;
        if (race is < 0 or >= 8) return false;
        var first = look.Customize[1] == 0 ? MaleFirst[race] : FemaleFirst[race];
        for (var i = 0; i < first.Length; i++)
        {
            if (!look.Name.StartsWith(first[i] + " ", StringComparison.Ordinal)) continue;
            var surname = look.Name[(first[i].Length + 1)..];
            if (race == 2) return surname == Last[race][i];
            if (Array.IndexOf(Last[race], surname) >= 0) return true;
        }
        return false;
    }

    // Hyur, Elezen, Lalafell, Miqo'te, Roegadyn, Au Ra, Hrothgar, Viera.
    private static readonly double[] RaceWeights = [16.5, 5.9, 10.0, 22.8, 3.7, 20.1, 5.3, 15.7];
    private static readonly double[] FemaleChance = [0, 0.45, 0.75, 0.85, 0.30, 0.85, 0.30, 0.65];
    private const double MidlanderChance = 0.70;
    private const double MidlanderFemaleChance = 0.70;
    private const double HighlanderFemaleChance = 0.25;

    public static BotLook RandomLook(Rng rng, string? fixedName = null)
    {
        var roll = rng.NextDouble() * RaceWeights.Sum();
        var race = 0;
        while (race < RaceWeights.Length - 1 && (roll -= RaceWeights[race]) >= 0) race++;
        int tribe;
        double femaleChance;
        if (race == 0)
        {
            var midlander = rng.NextDouble() < MidlanderChance;
            tribe = midlander ? 1 : 2;
            femaleChance = midlander ? MidlanderFemaleChance : HighlanderFemaleChance;
        }
        else
        {
            tribe = race * 2 + 1 + rng.Next(2);
            femaleChance = FemaleChance[race];
        }
        var sex = rng.NextDouble() < femaleChance ? 1 : 0;
        var first = (sex == 0 ? MaleFirst : FemaleFirst)[race];
        var firstIndex = rng.Next(first.Length);
        var lastIndex = race == 2 ? firstIndex : rng.Next(Last[race].Length);
        var name = fixedName ?? $"{first[firstIndex]} {Last[race][lastIndex]}";
        var customize = new byte[26];
        customize[0] = (byte)(race + 1);
        customize[1] = (byte)sex;
        customize[2] = 1;
        customize[3] = (byte)rng.Next(101);
        customize[4] = (byte)tribe;
        customize[5] = (byte)(1 + rng.Next(4));
        customize[6] = (byte)(1 + rng.Next(6));
        for (var i = 7; i < customize.Length; i++) customize[i] = (byte)rng.Next(16);
        return new BotLook(name, 0, customize, new EquipmentLook[10], null, null);
    }
}

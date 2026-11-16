using System.Collections.Generic;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.UserActions;

namespace AnoMech.Core.Game.Party;

// What a bot holds: its main hand, and an off hand where its job has one.
public sealed record BotWeaponSet(WeaponLook MainHand, WeaponLook? OffHand);

// The Ultimate Edenmorn weapons (item level 735, the FRU reward set), one per job, which every
// party bot holds. The client picks a character's auto-attack and weaponskill animations from the
// equipped weapon, and an unarmed one falls back to the fists, so a Paladin without a sword fights
// like a Monk. The models are read from the Item sheet by id, so they stay right across patches.
public static class BotWeapons
{
    // Job id -> main-hand item, and the separate off-hand item (the Paladin's shield) or 0.
    public static IReadOnlyDictionary<byte, (uint Main, uint Off)> Items { get; } = new Dictionary<byte, (uint, uint)>
    {
        [(byte)JobId.Paladin] = (44721, 44742), // Bastard Sword, Scutum
        [(byte)JobId.Monk] = (44722, 0),        // Sainti
        [(byte)JobId.Warrior] = (44723, 0),     // Battleaxe
        [(byte)JobId.Dragoon] = (44724, 0),     // Halberd
        [(byte)JobId.Bard] = (44725, 0),        // Cavalry Bow
        [(byte)JobId.WhiteMage] = (44726, 0),   // Cane
        [(byte)JobId.BlackMage] = (44727, 0),   // Rod
        [(byte)JobId.Summoner] = (44728, 0),    // Index
        [(byte)JobId.Scholar] = (44729, 0),     // Codex
        [(byte)JobId.Ninja] = (44730, 0),       // Main Gauches
        [(byte)JobId.DarkKnight] = (44731, 0),  // Zweihander
        [(byte)JobId.Machinist] = (44732, 0),   // Pistol
        [(byte)JobId.Astrologian] = (44733, 0), // Torquetum
        [(byte)JobId.Samurai] = (44734, 0),     // Samurai Blade
        [(byte)JobId.RedMage] = (44735, 0),     // Rapier
        [(byte)JobId.Gunbreaker] = (44736, 0),  // Gunblade
        [(byte)JobId.Dancer] = (44737, 0),      // Chakrams
        [(byte)JobId.Reaper] = (44738, 0),      // Sickle
        [(byte)JobId.Sage] = (44739, 0),        // Pendulums
        [(byte)JobId.Viper] = (44740, 0),       // Twinfangs
        [(byte)JobId.Pictomancer] = (44741, 0), // Round Brush
    };

    // The weapons `job` spawns with. A captured look's own weapon (the main-tank bot's glamour) is
    // kept when it is armed and was captured on this job; otherwise the job's Edenmorn weapon.
    // Null for a job with none, or when the Item sheet has no such row.
    public static BotWeaponSet? Choose(byte job, BotLook? look, IGameData data)
    {
        if (look is { MainHand.Id: not 0 } && look.ClassJob == job) return new BotWeaponSet(look.MainHand, look.OffHand);
        if (!Items.TryGetValue(job, out var items) || data.Item(items.Main) is not { ModelMain: not 0 } main) return null;
        var offModel = items.Off != 0 ? data.Item(items.Off)?.ModelMain ?? 0 : main.ModelSub;
        return new BotWeaponSet(Decode(main.ModelMain), offModel != 0 ? Decode(offModel) : null);
    }

    // Set in bits 0-15, base in 16-31, variant in 32-47; the item sheet carries no dye.
    public static WeaponLook Decode(ulong packed) => new((ushort)packed, (ushort)(packed >> 16), (ushort)(packed >> 32), 0, 0);
}

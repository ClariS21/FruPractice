using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace AnoMech.Core.Game.Party;

public enum BuffTarget
{
    Self,
    // Every party member within PartyRange of the caster, the player included.
    Party,
    // The boss the bot is fighting.
    Boss,
    // Whoever sits in the main tank seat.
    MainTank,
}

// A status an action puts on the status list, so the party list and the target bar show what a real
// party's would. Cosmetic: nothing in the damage model reads these. Duration is the action's own (the
// sheet has none), 0 for one that never runs out. Replaces are statuses of the same holder that give
// way to it (a Monk's forms); Consume takes one stack instead of adding.
internal sealed record BotBuff(BuffTarget Target, ushort StatusId, string StatusName, float Seconds, int Stacks = 1,
    ushort[]? Replaces = null, bool Consume = false);

// Mitigation and shields are not here: the mit plans own those statuses.
internal static class BotBuffs
{
    public const float PartyRange = 30f;
    private const float Dot = BotScripts.DotSeconds;
    private const float BardDot = BotScripts.BardDotSeconds;
    private const float Forms = 30f;
    private static readonly ushort[] FormIds = [107, 108, 109];

    private static BotBuff Self(ushort id, string name, float seconds, int stacks = 1) => new(BuffTarget.Self, id, name, seconds, stacks);
    private static BotBuff Party(ushort id, string name, float seconds) => new(BuffTarget.Party, id, name, seconds);
    private static BotBuff OnBoss(ushort id, string name, float seconds) => new(BuffTarget.Boss, id, name, seconds);
    private static BotBuff Form(ushort id, string name) => new(BuffTarget.Self, id, name, Forms, Replaces: FormIds);
    private static BotBuff SpendRequiescat() => new(BuffTarget.Self, 1368, "Requiescat", 0f, Consume: true);

    public static IReadOnlyDictionary<uint, BotBuff[]> All { get; } = new Dictionary<uint, BotBuff[]>
    {
        // Burst
        [7389] = [Self(1177, "Inner Release", 15f, 3)],
        [20] = [Self(76, "Fight or Flight", 20f)],
        [16138] = [Self(1831, "No Mercy", 20f)],
        [7390] = [Self(1972, "Delirium", 15f, 3)],
        [85] = [Self(1864, "Lance Charge", 20f)],
        [7395] = [Self(1181, "Riddle of Fire", 20f)],
        [101] = [Self(125, "Raging Strikes", 20f)],
        [136] = [Self(157, "Presence of Mind", 15f)],
        [3573] = [Self(737, "Ley Lines", 30f), Self(738, "Circle of Power", 30f)],
        [3557] = [Party(786, "Battle Litany", 20f)],
        [7396] = [Party(1185, "Brotherhood", 15f)],
        [16552] = [Party(1878, "Divination", 20f)],
        [118] = [Party(141, "Battle Voice", 15f)],
        [25785] = [Party(2964, "Radiant Finale", 15f)],
        [7436] = [OnBoss(1221, "Chain Stratagem", 20f)],

        // DoTs on the boss, refreshed whenever the script refreshes them
        [16532] = [OnBoss(1871, "Dia", Dot)],
        [16540] = [OnBoss(1895, "Biolysis", Dot)],
        [16554] = [OnBoss(1881, "Combust III", Dot)],
        [24314] = [OnBoss(2616, "Eukrasian Dosis III", Dot)],
        [7407] = [OnBoss(1201, "Stormbite", BardDot)],
        [7406] = [OnBoss(1200, "Caustic Bite", BardDot)],
        [3560] = [OnBoss(1201, "Stormbite", BardDot), OnBoss(1200, "Caustic Bite", BardDot)],
        [36986] = [OnBoss(3871, "High Thunder", Dot)],
        [25772] = [OnBoss(2719, "Chaotic Spring", 24f)],
        [16153] = [OnBoss(1837, "Sonic Break", Dot)],
        [16159] = [OnBoss(1838, "Bow Shock", 15f)],
        [23] = [OnBoss(248, "Circle of Scorn", 15f)],

        // What the rotation keeps up on the bot itself
        [45] = [Self(2677, "Surging Tempest", Dot)],
        [36955] = [Self(2720, "Power Surge", Dot)],
        [36945] = [Form(108, "Raptor Form")],
        [36946] = [Form(109, "Coeurl Form")],
        [36947] = [Form(107, "Opo-opo Form")],
        [36921] = [Self(1368, "Requiescat", Dot, 4)],
        [16459] = [SpendRequiescat()],
        [7384] = [SpendRequiescat()],
        [7421] = [Self(1211, "Triplecast", 15f, 3)],

        // Party-wide and on a seat
        [3559] = [Party(2009, "Wanderer's Minuet", 45f)],
        [24285] = [Self(2604, "Kardia", 0f), new(BuffTarget.MainTank, 2605, "Kardion", 0f)],
    };

    public static List<(SimCharacter Who, ushort StatusId, GameObjectId Source)> Apply(uint actionId, SimPartyNpc caster, SimEnemy boss, SimParty party)
    {
        var applied = new List<(SimCharacter, ushort, GameObjectId)>();
        if (!All.TryGetValue(actionId, out var buffs)) return applied;
        var source = caster.GameObjectId;
        foreach (var buff in buffs)
            foreach (var who in Holders(buff.Target, caster, boss, party))
            {
                foreach (var gone in buff.Replaces ?? [])
                    if (gone != buff.StatusId) who.RemoveStatus(gone, source);
                if (buff.Consume) who.AddStatus(buff.StatusId, buff.Seconds, -1, sourceObject: source);
                else if (who.AddStatus(buff.StatusId, buff.Seconds, buff.Stacks, overrideStacks: buff.Stacks > 1, sourceObject: source) != null)
                    applied.Add((who, buff.StatusId, source));
            }
        return applied;
    }

    private static IEnumerable<SimCharacter> Holders(BuffTarget target, SimPartyNpc caster, SimEnemy boss, SimParty party)
    {
        switch (target)
        {
            case BuffTarget.Self: return [caster];
            case BuffTarget.Boss: return [boss];
            case BuffTarget.MainTank: return party.Get(PartyRole.MainTank) is { } tank && tank.IsAlive() ? [tank] : [];
            default: return party.ActiveMembers().Where(m => DistanceXZ(m.Position, caster.Position) <= PartyRange).ToList();
        }
    }

    private static float DistanceXZ(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}

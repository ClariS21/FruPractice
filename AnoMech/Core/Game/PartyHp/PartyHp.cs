using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Game.PartyHp;

// The party's HP bars. The client's own HP is the truth, written by hits (through the EffectResult) and
// by heals (PartyHealing); hits never kill, the mechanic checks do. HP changes only on those discrete
// events, including natural regeneration on the shared server tick. Members mitigation practice models are left to it.
internal sealed class PartyHp(SimWorld world)
{
    private const float HitVariation = 0.10f;
    internal const float TickDelayMin = 0.05f;
    internal const float TickDelayMax = 0.35f;

    internal bool Owns(SimCharacter who)
        => who is ISimPartyMember && !world.MitPractice.Covers(who);

    // The HP a hit takes; a character that isn't a party member gets the severity's stock number.
    public uint RollHit(SimCharacter who, uint actionId, Severity severity)
    {
        if (who is not ISimPartyMember member || who.Proxy is not { Exists: true } chara) return severity.FlyTextAmount(kills: false);
        var fraction = HitFractions.For(actionId, severity, member.Role);
        var spread = 1f + (world.Stream("hp-damage").NextSingle() * 2f - 1f) * HitVariation;
        return (uint)MathF.Round(fraction * chara.MaxHealth * spread);
    }

    // Per-member server tick: when its HP update lands after the confirmation.
    public float RollTickDelay()
        => world.Calibration.HpTickJitter
            ? TickDelayMin + world.Stream("hp-tick").NextSingle() * (TickDelayMax - TickDelayMin)
            : 0f;

    // The character's HP after `damage` lands, never below 1.
    public uint Hit(SimCharacter who, uint damage)
    {
        if (who.Proxy is not { Exists: true } chara) return 0;
        if (!Owns(who) || who.Dead) return chara.Health;
        world.Healing.NoteHit(who);
        return chara.Health > damage ? chara.Health - damage : 1u;
    }

    public void SetToOne(SimCharacter who)
    {
        if (!Owns(who) || who.Dead || who.Proxy is not { Exists: true }) return;
        world.Healing.NoteHit(who);
        who.WriteHealth(1);
    }
}

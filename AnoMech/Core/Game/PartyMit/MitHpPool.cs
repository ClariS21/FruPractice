using System;

namespace AnoMech.Core.Game.PartyMit;

// One member's simulated HP. It changes when a hit or heal lands, including natural regeneration.
public sealed class MitHpPool(float max)
{
    public float Max { get; } = max;
    public float Hp { get; private set; } = max;

    // Returns the HP left, which may be below zero: the caller warns, nobody dies of it. A fresh hit
    // lands on a party the plan's healers had already topped up.
    public float Land(float loss, bool fresh = false)
    {
        var left = (fresh ? Max : MathF.Max(0f, Hp)) - loss;
        Hp = left;
        return left;
    }

    // Leaves the pool at `hp` outright, whatever it was (Hell's Judgment).
    public void SetTo(float hp) => Hp = hp;

    // HP back, never past full and never on a pool a would-be kill left below zero's worth of nothing.
    public void Heal(float amount) => Hp = MathF.Min(Max, MathF.Max(0f, Hp) + amount);
}

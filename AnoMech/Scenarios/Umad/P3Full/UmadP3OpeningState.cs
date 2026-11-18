using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3LimitCut;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P3Full;

// The opening's rolls. The wind crystal's corner, the winds, the tanks' bosses and the Thunder III
// plan are the ones the Limit Cut that follows plays with.
public sealed class UmadP3OpeningState
{
    public UmadP3Element ShortElement { get; }
    public UmadP3Element LongElement => ShortElement == UmadP3Element.Fire ? UmadP3Element.Water : UmadP3Element.Fire;
    // +1: the fire crystal is on the right, looking from the centre at the wind crystal.
    public int FireSide { get; }
    public uint Implosion { get; }
    // One support and one DPS on each element.
    public IReadOnlyDictionary<PartyRole, UmadP3Element> Debuffs { get; }

    public int BossSpot { get; }
    public IReadOnlyDictionary<PartyRole, Wind> Winds { get; }
    public UmadP3Tanks Tanks { get; }
    public ThunderIIIAssignment Thunder { get; }

    public UmadP3OpeningState(Rng rng, PartyRole localPlayerRole, UmadP3OpeningStateOverrides overrides, UmadP3LimitCutState limitCut,
        ThunderIIIAssignment thunder)
    {
        ShortElement = overrides.ShortElement ?? (rng.NextBool() ? UmadP3Element.Fire : UmadP3Element.Water);
        FireSide = overrides.FireOnRight is { } right ? right ? 1 : -1 : rng.NextSign();
        Implosion = overrides.Implosion ?? rng.NextObj(ActionId.LongitudinalImplosion, ActionId.LatitudinalImplosion);
        Debuffs = BuildDebuffs(rng, localPlayerRole, overrides);
        BossSpot = limitCut.BossSpot;
        Winds = limitCut.Winds;
        Tanks = limitCut.Tanks;
        Thunder = thunder;
    }

    // A forced element takes its role's place on it outright; a second seat of the same kind asking for
    // the same element keeps the fight's own roll.
    private static Dictionary<PartyRole, UmadP3Element> BuildDebuffs(Rng rng, PartyRole localPlayerRole, UmadP3OpeningStateOverrides overrides)
    {
        var debuffs = new Dictionary<PartyRole, UmadP3Element>();
        foreach (var (role, element) in overrides.Debuff.Resolve(localPlayerRole))
        {
            if (debuffs.Any(kv => kv.Key.IsDps() == role.IsDps() && kv.Value == element))
            {
                DiagnosticLog.Warn($"[UmadP3Opening] {role} asked for {element}, already forced for another of its kind -- leaving {role} to the roll.");
                continue;
            }
            debuffs[role] = element;
        }
        var supports = rng.Shuffle(PerRole.All.Where(r => !r.IsDps())).ToList();
        var dps = rng.Shuffle(PerRole.All.Where(r => r.IsDps())).ToList();
        foreach (var element in new[] { UmadP3Element.Fire, UmadP3Element.Water })
        {
            if (!debuffs.Any(kv => !kv.Key.IsDps() && kv.Value == element)) debuffs[supports.First(r => !debuffs.ContainsKey(r))] = element;
            if (!debuffs.Any(kv => kv.Key.IsDps() && kv.Value == element)) debuffs[dps.First(r => !debuffs.ContainsKey(r))] = element;
        }
        return debuffs;
    }

    public UmadP3Element? DebuffOf(PartyRole role) => Debuffs.TryGetValue(role, out var element) ? element : null;

    public int Side(UmadP3Element element) => element == UmadP3Element.Fire ? FireSide : -FireSide;

    // The wind frame: `a` toward the wind crystal, `b` to the right of someone at the centre facing it.
    public float Hold => UmadP3LimitCutState.SpotHeading(BossSpot);

    public Vector2 Wind(float a, float b)
    {
        var (sin, cos) = MathF.SinCos(Hold);
        return new Vector2(a * sin - b * cos, a * cos + b * sin);
    }

    public Vector2 WindCrystal => Wind(CrystalRadius, 0f);
    public Vector2 Crystal(UmadP3Element element) => Wind(0f, CrystalRadius * Side(element));

    private const float CrystalRadius = 14f;
}

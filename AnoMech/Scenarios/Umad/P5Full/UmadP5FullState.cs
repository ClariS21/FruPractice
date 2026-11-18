using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Umad.P5Celestriad;
using AnoMech.Scenarios.Umad.P5Exaflares;
using AnoMech.Scenarios.Umad.P5Flood;

namespace AnoMech.Scenarios.Umad.P5Full;

// The healer and DPS stacks of one Fell Forces round; the tank stack goes on Kefka's highest enmity.
public sealed record UmadP5FellForcesRoll(PartyRole Healer, PartyRole Dps);

// Every slice's rolls, made once at the run's start.
public sealed class UmadP5FullState
{
    private static readonly PartyRole[] NonTanks =
    [
        PartyRole.RegenHealer, PartyRole.ShieldHealer,
        PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps,
    ];

    public UmadP5FloodState Flood { get; }
    public UmadP5CelestriadState Celestriad { get; }
    public UmadP5ExaflaresState Exaflares { get; }
    public IReadOnlyList<UmadP5FellForcesRoll> FellForces { get; }
    // The first notes' three Holy targets, per Orchestra.
    public IReadOnlyList<IReadOnlyList<PartyRole>> OrchestraHoly { get; }
    public IReadOnlyList<UmadP5ForsakenRoll> Forsaken { get; }
    public float KillAt { get; }
    public bool BossSurvives { get; }

    public UmadP5FullState(UmadP5FloodState flood, UmadP5CelestriadState celestriad, UmadP5ExaflaresState exaflares,
        Rng rng, UmadP5FullStateOverrides overrides)
    {
        Flood = flood;
        Celestriad = celestriad;
        Exaflares = exaflares;
        FellForces = [.. UmadP5FullScenario.FellForcesAt.Select(_ => new UmadP5FellForcesRoll(overrides.FellForcesHealer ?? rng.NextHealerRole(), overrides.FellForcesDps ?? rng.NextDpsRole()))];
        OrchestraHoly = [.. UmadP5FullScenario.OrchestraAt.Select(_ => overrides.OrchestraHoly ?? [.. rng.Shuffle(NonTanks).Take(3)])];
        Forsaken = [.. UmadP5Forsaken.MarkerAt.Select(_ => new UmadP5ForsakenRoll(
            overrides.ForsakenMarker ?? rng.NextObj(NonTanks), overrides.ForsakenSide ?? rng.NextSign(), overrides.ForsakenPick ?? rng.Next(4)))];
        KillAt = Math.Clamp(overrides.KillAt, UmadP5FullStateOverrides.KillAtMin, UmadP5FullStateOverrides.KillAtMax);
        BossSurvives = overrides.BossSurvives;
    }
}

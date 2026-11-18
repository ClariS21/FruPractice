using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Umad.P3LimitCut;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P3Full;

// P3 from the bosses' arrival to the Limit Cut: the first Decisive Battle, Bowels of Agony's elements
// and winds, Exdeath's Thunder III blast, the first buster and Implosion between the two elements.
internal sealed class UmadP3Opening : IWorldEventListener
{
    // Each element goes off this long after its debuff runs out, and its crystal a second after that.
    private const float StrayDelay = 0.1f;
    internal const float ShortCrystalAt = 42.73f;
    internal const float LongCrystalAt = 69.70f;
    internal const float BowelsResolvesAt = 22.56f;
    internal const float ShortDebuffSeconds = 19.0f;
    internal const float LongDebuffSeconds = 46.0f;
    internal const float WindSeconds = 68.0f;
    // Retail strips a wind's thrown holder of the wind, and the crystal answers this long after.
    private const float CycloneAfterThrow = 1.7f;
    private const float AutoEvery = 3.03f;

    internal const float ThunderBlastAt = 34.77f;
    internal static readonly float[] ThunderHitsAt = [50.97f, 54.00f];
    internal const float ImplosionAt = 58.81f;
    internal static readonly float[] ShockwavesAt = [64.52f, 66.52f];

    private SimWorld world = null!;
    private SimParty party = null!;
    private UmadP3OpeningState state = null!;
    private UmadP3Cast cast = null!;
    private Placement implosionFrom;

    public void Play(SimWorld worldParam, bool withAi, UmadP3OpeningState stateParam, UmadP3Cast castParam)
    {
        world = worldParam;
        party = world.Party;
        state = stateParam;
        cast = castParam;
        DiagnosticLog.Info($"[UmadP3Opening] Roll: {state.ShortElement} first, fire crystal on the {(state.FireSide > 0 ? "right" : "left")}, "
            + $"wind crystal {UmadP3LimitCutState.SpotName(state.BossSpot)}, {(state.Implosion == ActionId.LongitudinalImplosion ? "Longitudinal" : "Latitudinal")} Implosion, "
            + string.Join(" ", state.Debuffs.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")) + ".");
        if (withAi) new UmadP3OpeningAi().Run(state, cast, world);

        world.Events.Add(0f, SpawnActors);
        world.Events.Add(0.14f, () => cast.Exdeath?.Cast(ActionId.DecisiveBattle_Exdeath, animationLock: 3.1f));
        world.Events.Add(0.18f, () => cast.Chaos?.Cast(ActionId.DecisiveBattle_Chaos, animationLock: 3.1f));
        world.Events.Add(1.0f, () =>
        {
            cast.Chaos?.Follow(ChaosTank);
            cast.Exdeath?.Follow(ExdeathTank);
        });
        world.Events.Add(3.15f, ResolveDecisiveBattle);
        world.Events.Add(4.37f, () => cast.Kefka?.Cast(ActionId.KefkaRest, animationLock: 1.1f));
        world.Events.Add(7.36f, () => cast.Kefka?.SetAnimationState(0, 1));
        for (var at = 6f; at < 69f; at += AutoEvery)
        {
            world.Events.Add(at, () => AutoAttack(cast.Chaos, ChaosTank, UmadActions.ChaosAutoAttack));
            world.Events.Add(at + 0.35f, () => AutoAttack(cast.Exdeath, ExdeathTank, UmadActions.ExdeathAutoAttack));
        }
        world.Events.Add(17.59f, () => cast.Chaos?.Cast(UmadActions.BowelsOfAgony));
        world.Events.Add(BowelsResolvesAt, ApplyBowelsOfAgony);
        // Exdeath holds the middle from his blast until the buster is done.
        world.Events.Add(ThunderBlastAt - 0.07f, () =>
        {
            StripHeroes();
            cast.Exdeath?.Follow();
        });
        world.Events.Add(ThunderBlastAt, () => cast.Exdeath?.Cast(UmadActions.ThunderIIIBlast));
        world.Events.Add(ShortCrystalAt, () => FireCrystal(state.ShortElement));
        world.Events.Add(45.89f, () => cast.Exdeath?.Cast(ActionId.ThunderIII_Cast, animationLock: 3.1f));
        foreach (var at in ThunderHitsAt) world.Events.Add(at, ResolveThunder);
        world.Events.Add(54.5f, () => cast.Exdeath?.Follow(ExdeathTank));
        world.Events.Add(56.61f, () => cast.Kefka?.Cast(ActionId.KefkaUnrest, animationLock: 1.1f));
        world.Events.Add(58.71f, () => cast.Kefka?.Cast(ActionId.Trance, animationLock: 1.1f));
        world.Events.Add(ImplosionAt, CastImplosion);
        world.Events.Add(62.58f, () => cast.Kefka?.AddStatusParam(StatusId.KefkaTrance, 0x1FF));
        world.Events.Add(ShockwavesAt[0], () => Shockwaves(0));
        world.Events.Add(ShockwavesAt[1], () => Shockwaves(1));
        world.Events.Add(LongCrystalAt, () => FireCrystal(state.LongElement));
        world.Events.Add(LongCrystalAt + 0.2f, SettleBossesForTheLimitCut);
    }

    private SimCharacter? ChaosTank => party.Get(state.Tanks.Chaos);
    private SimCharacter? ExdeathTank => party.Get(state.Tanks.Exdeath);

    private void SpawnActors()
    {
        cast.Chaos ??= UmadP3Cast.SpawnChaos(world, new Placement(new Vector3(-8f, 0f, 0f), MathF.PI / 2f));
        cast.Exdeath ??= UmadP3Cast.SpawnExdeath(world, new Placement(new Vector3(8f, 0f, 0f), -MathF.PI / 2f));
        cast.Kefka ??= UmadP3Cast.SpawnKefka(world, new Placement(new Vector3(0f, 0f, -10f), 0f), SpawnVisibility.Visible);
    }

    // The tanks have them at the wind crystal; they settle where the Limit Cut takes them up.
    private void SettleBossesForTheLimitCut()
    {
        foreach (var (boss, at) in new[]
                 {
                     (cast.Chaos, UmadP3LimitCutScenario.ChaosStart(state.BossSpot)),
                     (cast.Exdeath, UmadP3LimitCutScenario.ExdeathStart(state.BossSpot)),
                 })
        {
            boss?.Follow();
            boss?.MoveTo(at.Position);
        }
    }

    private void AutoAttack(SimEnemy? boss, SimCharacter? tank, EnemyAction auto)
    {
        if (boss is null || boss.IsCasting || !tank.IsAlive()) return;
        boss.Cast(auto, tank);
    }

    // The four nearest Chaos fight it, the rest Exdeath.
    private void ResolveDecisiveBattle()
    {
        if (cast.Chaos is not { } chaos) return;
        chaos.AddStatus(StatusId.EpicVillain);
        cast.Exdeath?.AddStatus(StatusId.FatedVillain);
        var epic = party.Find.ClosestN(chaos.Position, 4);
        foreach (var member in party.ActiveMembers())
            member.AddStatus(epic.Contains(member) ? StatusId.EpicHero : StatusId.FatedHero);
    }

    private void StripHeroes()
    {
        foreach (var member in party.ActiveMembers())
        {
            member.RemoveStatus(StatusId.EpicHero);
            member.RemoveStatus(StatusId.FatedHero);
        }
        cast.Chaos?.RemoveStatus(StatusId.EpicVillain);
        cast.Exdeath?.RemoveStatus(StatusId.FatedVillain);
    }

    private void ApplyBowelsOfAgony()
    {
        foreach (var (role, element) in state.Debuffs)
            party.Get(role)?.AddStatus(element == UmadP3Element.Fire ? StatusId.EntropyP3 : StatusId.DynamicFluidP3,
                element == state.ShortElement ? ShortDebuffSeconds : LongDebuffSeconds);
        foreach (var member in party.ActiveMembers())
            if (member is ISimPartyMember pm)
                member.AddStatus(state.Winds[pm.Role] == Wind.Headwind ? StatusId.Headwind : StatusId.Tailwind, WindSeconds);
        var crystal = state.WindCrystal;
        cast.WindCrystal ??= UmadP3Cast.SpawnWindCrystal(world, state.BossSpot, new Vector3(crystal.X, 0f, crystal.Y), state.Hold + MathF.PI);
    }

    public void OnStatusLost(StatusLost lost)
    {
        if (lost.Reason != StatusLossReason.Expired) return;
        var element = lost.StatusId switch
        {
            StatusId.EntropyP3 => UmadP3Element.Fire,
            StatusId.DynamicFluidP3 => UmadP3Element.Water,
            _ => (UmadP3Element?)null,
        };
        if (element is not { } goingOff) return;
        var holder = lost.Holder;
        var at = CrystalPlacement(goingOff);
        world.Events.Add(StrayDelay, () => cast.Chaos?.CastFromHelper(
            goingOff == UmadP3Element.Fire ? UmadActions.StrayFlames : UmadActions.StraySpray, holder, at: at));
    }

    // Aimed at the two players nearest the crystal as it fires.
    private void FireCrystal(UmadP3Element element)
    {
        var at = CrystalPlacement(element);
        foreach (var target in party.Find.ClosestN(at.Position, 2))
            cast.Chaos?.CastFromHelper(element == UmadP3Element.Fire ? UmadActions.CrystalInferno : UmadActions.CrystalTsunami, target, at: at);
    }

    private Placement CrystalPlacement(UmadP3Element element)
    {
        var p = state.Crystal(element);
        return new Placement(new Vector3(p.X, 0f, p.Y), 0f).Face(Vector3.Zero);
    }

    public void OnActionResolved(ActionResolved resolved)
    {
        if (resolved.Action.ActionId is not (ActionId.Inferno_Crystal or ActionId.Tsunami_Crystal)) return;
        var thrown = resolved.Hits.Where(h => !ReferenceEquals(h, resolved.Target) && !resolved.Killed.Contains(h)
                                              && (h.HasStatus(StatusId.Headwind) || h.HasStatus(StatusId.Tailwind))).ToList();
        if (thrown.Count == 0) return;
        var who = string.Join(", ", thrown.OfType<ISimPartyMember>().Select(m => m.Role));
        world.Announce($"{Natives.Data.ActionName(resolved.Action.ActionId)} threw {who} off their wind: the wind crystal answers early.");
        world.Events.Add(CycloneAfterThrow, () => FireCyclones(thrown.Count));
    }

    private void FireCyclones(int count)
    {
        var crystal = state.WindCrystal;
        var at = new Placement(new Vector3(crystal.X, 0f, crystal.Y), 0f);
        foreach (var target in party.Find.ClosestN(at.Position, count))
            cast.Chaos?.CastFromHelper(UmadActions.Cyclone, target, at: at);
    }

    private void ResolveThunder()
    {
        if (cast.Exdeath is not { } exdeath) return;
        exdeath.CastFromHelper(UmadActions.ThunderIII, party.Find.Closest(exdeath.Position));
    }

    // The shockwaves keep the facing Chaos casts Implosion with, not wherever it turns after.
    private void CastImplosion()
    {
        if (cast.Chaos is not { } chaos) return;
        implosionFrom = chaos.Placement();
        chaos.Cast(state.Implosion, animationLock: 6.1f);
    }

    private void Shockwaves(int set)
    {
        foreach (var axis in UmadImplosion.ConeAxes(implosionFrom.Rotation, state.Implosion, set))
            cast.Chaos?.CastFromHelper(UmadActions.ImplosionShockwave, at: implosionFrom with { Rotation = axis });
    }
}

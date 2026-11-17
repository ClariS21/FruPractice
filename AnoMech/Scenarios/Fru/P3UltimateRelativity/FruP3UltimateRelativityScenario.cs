using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.FruConstants;
using static AnoMech.Scenarios.Fru.P3UltimateRelativity.FruP3UltimateRelativityState;
using Actions = AnoMech.Scenarios.Fru.FruActions;

namespace AnoMech.Scenarios.Fru.P3UltimateRelativity;

public sealed class FruP3UltimateRelativityScenario : IScenario, IGuidanceHazardSource
{
    private UltimateRelativityHazards? guidanceHazards;
    public IEnumerable<GuidanceHazard> Upcoming(float now, float horizon)
        => guidanceHazards?.Upcoming(now, horizon) ?? [];
    public string Name => "Ultimate Relativity";
    public IPhase Phase => FruZone.P3;

    public bool SupportsMitigationPractice => true;

    public IReadOnlyList<IScenarioAi> AiStrats => [new FruP3UltimateRelativityAi()];

    public void DrawSettings() => settingsWindow.Draw();
    public object SettingsOverrides => settingsWindow.Overrides;
    private readonly FruP3UltimateRelativitySettingsWindow settingsWindow = new();

    private SimWorld world = null!;
    private FruP3UltimateRelativityState state = null!;
    private SimEnemy? oracle;
    private readonly SimEnemy?[] hourglasses = new SimEnemy?[8];
    private readonly SimEnemy?[] lasers = new SimEnemy?[8];
    private readonly Vector3[] laserDirections = new Vector3[8];
    private readonly SimEnemy?[] debuffHelpers = new SimEnemy?[8];
    private readonly List<SimTether> speedTethers = [];
    private readonly Dictionary<PartyRole, Vector3> returns = new();

    public IReadOnlyList<BurstAnchor> BurstAnchors => [new(ActionId.ShellCrusherCast, 55.7f)];

    public void Run(SimWorld worldParam, int? selectedAi) => Play(worldParam, selectedAi, Roll(worldParam), new FruP3Cast());

    internal FruP3UltimateRelativityState Roll(SimWorld worldParam) => new(worldParam.Rng, settingsWindow.Overrides);

    internal void Play(SimWorld worldParam, int? selectedAi, FruP3UltimateRelativityState stateParam, FruP3Cast cast)
    {
        world = worldParam;
        state = stateParam;
        speedTethers.Clear();
        returns.Clear();
        Array.Clear(laserDirections);
        guidanceHazards = new UltimateRelativityHazards(state, world, world.Events.Elapsed,
            clock => laserDirections[clock], role => returns.TryGetValue(role, out var at) ? at : null);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<FruP3UltimateRelativityState>)AiStrats[idx]).Run(state, world);

        if (!cast.Chained)
            world.MitPractice.Begin(FruP3UltimateRelativityMitigation.Plan(), new Dictionary<MitSource, Func<SimCharacter?>>
            {
                [FruMitigation.Oracle] = () => oracle,
            });

        world.Events.Add(0f, () => SpawnOracleAndHelpers(cast));
        world.Events.Add(0.87f, StopTime);
        world.Events.Add(1f, () => oracle?.Cast(Actions.UltimateRelativity));
        world.Events.Add(5.9f, OracleBattleTalk);
        world.Events.Add(AssignAt, AssignDebuffs);
        world.Events.Add(AssignAt, SpawnHourglasses);
        world.Events.Add(15f, TetherHourglasses);
        world.Events.Add(19.8f, () => SetHourglasses(MapEffect.RelativityHourglassFlowing, _ => true));
        world.Events.Add(20.65f, SpeedUpAndSlowDownHourglasses);
        for (var wave = 0; wave < 3; wave++)
        {
            var w = wave;
            world.Events.Add(HourglassFlowAt(w), () => SetHourglasses(MapEffect.RelativityHourglassDraining, clock => Wave(clock) == w));
            world.Events.Add(FireAt(w), () => ResolveFire(w));
            world.Events.Add(LaserStartAt(w), () => StartLasers(w));
            if (w < 2) world.Events.Add(ReturnAt(earlyReturn: w == 0), () => SaveReturns(earlyReturn: w == 0));
            world.Events.Add(LaserHitAt(w, 0), () => FireFirstLasers(w));
            world.Events.Add(LaserHitAt(w, 0) + 0.35f, () => TurnLasersToFirstSweep(w));
            for (var shot = 1; shot < LaserShots; shot++)
            {
                var s = shot;
                world.Events.Add(LaserHitAt(w, s), () => SweepLasers(w, s));
            }
        }
        world.Events.Add(StunAt, () => world.Party.ActiveMembers().ToList().ForEach(m => m.AddStatus(StatusId.Stun, StunEndAt - StunAt)));
        world.Events.Add(RewindAt, Rewind);
        world.Events.Add(FinalAt, ResolveReturnDebuffs);
        world.Events.Add(55.7f, () => oracle?.Cast(ActionId.ShellCrusherCast, animationLock: 0.2f));
        world.Events.Add(59.2f, ShellCrusher);
        if (!cast.Chained) return;
        world.Events.Add(StunEndAt, ThawTime);
        world.Events.Add(LaserHitAt(2, LaserShots - 1) + HourglassLingerSeconds, DespawnHourglasses);
    }

    private void StopTime()
    {
        world.Map.SetWeather(WeatherId.TimeStop, 0f);
        world.Map.AddEffect(MapEffect.FreezeFloor, MapEffect.OracleStage);
    }

    private void OracleBattleTalk()
    {
        if (oracle is { } boss) world.Map.DirectorUpdate(0x80000027U, 0xDU, 0x2U, BNpcNameId.Oracle, boss.EntityId);
    }

    private void ThawTime()
    {
        world.Map.SetWeather(WeatherId.Oracle, 0f);
        world.Map.AddEffect(MapEffect.ThawFloor, MapEffect.OracleStage);
    }

    // Relativity's hourglasses leave a beat after the last laser. The scenery objects need the hide
    // map effect as well as the actors despawning, or they stay lit through the rest of the phase.
    private const float HourglassLingerSeconds = 1.5f;

    private void DespawnHourglasses()
    {
        SetHourglasses(MapEffect.HourglassHide, _ => true);
        foreach (var hourglass in hourglasses) hourglass?.Despawn();
        Array.Clear(hourglasses);
    }

    private void SpawnOracleAndHelpers(FruP3Cast cast)
    {
        oracle = cast.Oracle ??= FruP3Cast.SpawnOracle(world, targetable: true);
        for (var i = 0; i < debuffHelpers.Length; i++) debuffHelpers[i] = SpawnHelper(BNpcNameId.Oracle);
        for (var i = 0; i < lasers.Length; i++) lasers[i] = SpawnHelper(BNpcNameId.DelightsHourglass);
    }

    private SimEnemy? SpawnHelper(uint nameId) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: BNpcBaseId.Helper,
        NameId: nameId,
        Level: 1,
        EnemyList: EnemyListMode.Never,
        Visibility: SpawnVisibility.InvisibleHelper,
        Placement: new Placement(Vector3.Zero, MathF.PI)));

    private void AssignDebuffs()
    {
        foreach (var role in Enum.GetValues<PartyRole>())
        {
            if (world.Party.Get(role) is not { } member || !member.IsAlive()) continue;
            if (role == state.IceHolder) member.AddStatus(StatusId.DarkBlizzardWaiting, FireAt(1) - AssignAt);
            else member.AddStatus(StatusId.DarkFireWaiting, FireAt(FireWave(state.Debuff(role))) - AssignAt);
            var finalDebuff = state.Shadoweye(role) ? StatusId.ShadoweyeWaiting
                : state.DarkWater(role) ? StatusId.DarkWaterWaiting : StatusId.DarkEruptionWaiting;
            member.AddStatus(finalDebuff, FinalAt - AssignAt);
            member.AddStatus(StatusId.ReturnWaiting, ReturnAt(state.EarlyReturn(role)) - AssignAt);
            member.AddStatus(StatusId.RelativityClock, 42f);
        }
        for (var wave = 0; wave < 3; wave++)
            world.Party.Get(state.Darkness(wave))?.AddStatus(StatusId.UnholyDarknessWaiting, FireAt(wave) - AssignAt);
    }

    private void SpawnHourglasses()
    {
        for (var clock = 0; clock < 8; clock++)
        {
            hourglasses[clock] = world.SpawnEnemy(new EnemySpawnConfig(
                BNpcBaseId: BNpcBaseId.SorrowsHourglassP3,
                NameId: BNpcNameId.DelightsHourglass,
                Level: Level,
                EnemyList: EnemyListMode.Always,
                Placement: new Placement(state.Hourglass(clock), Facing(-state.Direction(clock))),
                LayoutId: LayoutId.RelativityHourglass));
        }
        SetHourglasses(MapEffect.RelativityHourglassShow, _ => true);
    }

    private void SetHourglasses(uint effect, Func<int, bool> clocks)
    {
        for (var clock = 0; clock < 8; clock++)
            if (clocks(clock)) world.Map.AddEffect(effect, state.HourglassSlot(clock));
    }

    private static float Facing(Vector3 direction) => MathF.Atan2(direction.X, direction.Z);

    private static bool Quickened(int clock) => Wave(clock) == 0;
    private static bool Slowed(int clock) => Wave(clock) == 2;

    private void TetherHourglasses()
    {
        oracle?.Cast(ActionId.Speed, animationLock: 3.1f);
        for (var clock = 0; clock < 8; clock++)
            if (Quickened(clock) || Slowed(clock))
                speedTethers.Add(world.Tether(hourglasses[clock], oracle, Quickened(clock) ? TetherId.Quicken : TetherId.Slow));
    }

    private void SpeedUpAndSlowDownHourglasses()
    {
        speedTethers.ForEach(tether => tether.Despawn());
        speedTethers.Clear();
        var helper = 0;
        for (var clock = 0; clock < 8; clock++)
            if (Quickened(clock) || Slowed(clock))
                debuffHelpers[helper++]?.Cast(Quickened(clock) ? Actions.Quicken : Actions.Slow, hourglasses[clock]);
    }

    private void ResolveFire(int wave)
    {
        var helper = 0;
        foreach (var role in Enum.GetValues<PartyRole>().Where(role => state.HasFire(role, wave)))
            CastOn(debuffHelpers[helper++], Actions.DarkFire, role);
        if (wave == 1) CastOn(debuffHelpers[helper++], Actions.DarkBlizzard, state.IceHolder);
        CastOn(debuffHelpers[helper], Actions.UnholyDarkness, state.Darkness(wave));
    }

    private void CastOn(SimEnemy? helper, EnemyAction action, PartyRole role)
    {
        if (world.Party.Get(role) is { } target && target.IsAlive()) helper?.Cast(action, target);
    }

    private void StartLasers(int wave)
    {
        for (var clock = 0; clock < 8; clock++)
        {
            if (Wave(clock) != wave) continue;
            var param = state.Clockwise(clock) ? HourglassClockwiseParam : HourglassCounterclockwiseParam;
            hourglasses[clock]?.AddStatusParam(StatusId.HourglassRotation, param, 4f);
            hourglasses[clock]?.Cast(ActionId.SinboundMeltdownCast, animationLock: 0.2f);
        }
        SetHourglasses(MapEffect.RelativityHourglassEmptying, clock => Wave(clock) == wave);
    }

    // The first shot goes at whoever stands nearest the hourglass; the rest sweep on from there.
    private void FireFirstLasers(int wave)
    {
        for (var clock = 0; clock < 8; clock++)
        {
            if (Wave(clock) != wave) continue;
            var origin = state.Hourglass(clock);
            var bait = world.Party.ActiveMembers().MinBy(m => Vector3.DistanceSquared(m.JudgedPosition, origin));
            var offset = bait is null ? Vector3.Zero : bait.JudgedPosition - origin;
            laserDirections[clock] = offset.LengthSquared() > 0.0001f ? Vector3.Normalize(offset) : -state.Direction(clock);
            lasers[clock]?.SetPosition(new Placement(origin, Facing(laserDirections[clock])));
            lasers[clock]?.Cast(Actions.SinboundMeltdownFirst);
        }
    }

    private void TurnLasersToFirstSweep(int wave)
    {
        for (var clock = 0; clock < 8; clock++)
            if (Wave(clock) == wave) lasers[clock]?.TurnTo(SweepFacing(clock, 1));
    }

    private void SweepLasers(int wave, int shot)
    {
        for (var clock = 0; clock < 8; clock++)
        {
            if (Wave(clock) != wave) continue;
            if (shot > 1) lasers[clock]?.SetRotation(SweepFacing(clock, shot));
            lasers[clock]?.Cast(Actions.SinboundMeltdownRest);
        }
    }

    private float SweepFacing(int clock, int shot)
    {
        var angle = (state.Clockwise(clock) ? 1 : -1) * shot * MathF.PI / 12f;
        var d = laserDirections[clock];
        return Facing(new Vector3(d.X * MathF.Cos(angle) - d.Z * MathF.Sin(angle), 0f, d.X * MathF.Sin(angle) + d.Z * MathF.Cos(angle)));
    }

    private void SaveReturns(bool earlyReturn)
    {
        foreach (var role in Enum.GetValues<PartyRole>().Where(role => state.EarlyReturn(role) == earlyReturn))
        {
            if (world.Party.Get(role) is not { } member || !member.IsAlive()) continue;
            returns[role] = member.JudgedPosition;
            member.AddStatus(StatusId.Return, StunAt - ReturnAt(earlyReturn));
            world.PlaceGroundVfx(VfxId.ReturnMarker, member.JudgedPosition);
        }
    }

    // Players keep the facing they have now, not the one they had when the Return was saved.
    private void Rewind()
    {
        foreach (var (role, position) in returns)
            if (world.Party.Get(role) is { } member && member.IsAlive())
                member.CarryTo(position, TimelineId.Rewind);
    }

    private void ResolveReturnDebuffs()
    {
        var helper = 0;
        foreach (var role in Enum.GetValues<PartyRole>())
        {
            if (!state.Shadoweye(role))
                CastOn(debuffHelpers[helper++], state.DarkWater(role) ? Actions.DarkWater : Actions.DarkEruption, role);
            else if (world.Party.Get(role) is { } holder && holder.IsAlive() && debuffHelpers[helper++] is { } helperOnHolder)
            {
                helperOnHolder.SetPosition(holder.Placement());
                helperOnHolder.Cast(Actions.Shadoweye);
            }
        }
    }

    private void ShellCrusher()
    {
        if (world.Party.Get(state.ShellCrusherTarget) is { } target && target.IsAlive()) oracle?.Cast(Actions.ShellCrusher, target);
    }
}

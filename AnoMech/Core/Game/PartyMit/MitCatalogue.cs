using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.UserActions;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Core.Game.PartyMit;

// What a plan entry's button does, for the bot schedule and the grader: None grants nothing the
// damage model counts (it prepares another button, or heals).
public enum MitKind { None, Mit, Debuff, Ground, Shield }

// A party mitigation button. Recast and Duration are the level-100 values; Requires is the status
// the caster must hold to press it, spent by the press.
public sealed record MitActionDef(uint ActionId, string Name, float Recast, float Duration, JobId[] Jobs, MitKind Kind,
    ushort Requires = 0, uint[]? Aliases = null, bool PetAction = false);

public static class MitCatalogue
{
    private static readonly JobId[] Tanks = [Paladin, Warrior, DarkKnight, Gunbreaker];
    private static readonly JobId[] Melee = [Monk, Dragoon, Ninja, Samurai, Reaper, Viper];
    private static readonly JobId[] Casters = [BlackMage, Summoner, RedMage, Pictomancer];

    private const float Gcd = 2.5f;

    public static readonly IReadOnlyList<MitActionDef> All =
    [
        new(MitActionId.Reprisal, "Reprisal", 60f, 15f, Tanks, MitKind.Debuff),
        new(MitActionId.ShakeItOff, "Shake It Off", 90f, 30f, [Warrior], MitKind.Shield),
        new(MitActionId.DivineVeil, "Divine Veil", 90f, 30f, [Paladin], MitKind.Shield),
        new(MitActionId.DarkMissionary, "Dark Missionary", 90f, 15f, [DarkKnight], MitKind.Mit),
        new(MitActionId.HeartOfLight, "Heart of Light", 90f, 15f, [Gunbreaker], MitKind.Mit),
        new(MitActionId.LastBastion, "Last Bastion", 0f, 8f, [Paladin], MitKind.Mit),
        new(MitActionId.LandWaker, "Land Waker", 0f, 8f, [Warrior], MitKind.Mit),
        new(MitActionId.DarkForce, "Dark Force", 0f, 8f, [DarkKnight], MitKind.Mit),
        new(MitActionId.GunmetalSoul, "Gunmetal Soul", 0f, 8f, [Gunbreaker], MitKind.Mit),

        new(MitActionId.Rampart, "Rampart", 90f, 20f, Tanks, MitKind.Mit),
        new(MitActionId.Damnation, "Damnation", 120f, 15f, [Warrior], MitKind.Mit, Aliases: [44]),
        new(MitActionId.ThrillOfBattle, "Thrill of Battle", 90f, 10f, [Warrior], MitKind.Mit),
        new(MitActionId.Bloodwhetting, "Bloodwhetting", 25f, 8f, [Warrior], MitKind.Mit, Aliases: [3551]),
        new(MitActionId.NascentFlash, "Nascent Flash", 25f, 8f, [Warrior], MitKind.Mit),
        new(MitActionId.Holmgang, "Holmgang", 240f, 10f, [Warrior], MitKind.Mit),
        new(MitActionId.Guardian, "Guardian", 120f, 15f, [Paladin], MitKind.Mit, Aliases: [17]),
        new(MitActionId.Bulwark, "Bulwark", 90f, 10f, [Paladin], MitKind.Mit),
        new(MitActionId.HolySheltron, "Holy Sheltron", 5f, 8f, [Paladin], MitKind.Mit, Aliases: [3542]),
        new(MitActionId.Intervention, "Intervention", 10f, 8f, [Paladin], MitKind.Mit),
        new(MitActionId.HallowedGround, "Hallowed Ground", 420f, 10f, [Paladin], MitKind.Mit),
        new(MitActionId.GreatNebula, "Great Nebula", 120f, 15f, [Gunbreaker], MitKind.Mit, Aliases: [16148]),
        new(MitActionId.HeartOfCorundum, "Heart of Corundum", 25f, 8f, [Gunbreaker], MitKind.Mit, Aliases: [16161]),
        new(MitActionId.Camouflage, "Camouflage", 90f, 20f, [Gunbreaker], MitKind.Mit),
        new(MitActionId.Superbolide, "Superbolide", 360f, 10f, [Gunbreaker], MitKind.Mit),
        new(MitActionId.ShadowedVigil, "Shadowed Vigil", 120f, 15f, [DarkKnight], MitKind.Mit, Aliases: [3636]),
        new(MitActionId.TheBlackestNight, "The Blackest Night", 15f, 7f, [DarkKnight], MitKind.Shield),
        new(MitActionId.DarkMind, "Dark Mind", 60f, 10f, [DarkKnight], MitKind.Mit),
        new(MitActionId.Oblation, "Oblation", 60f, 10f, [DarkKnight], MitKind.Mit),
        new(MitActionId.LivingDead, "Living Dead", 300f, 10f, [DarkKnight], MitKind.Mit),

        new(MitActionId.Aquaveil, "Aquaveil", 60f, 8f, [WhiteMage], MitKind.Mit),
        new(MitActionId.DivineBenison, "Divine Benison", 30f, 15f, [WhiteMage], MitKind.Shield),
        new(MitActionId.Protraction, "Protraction", 60f, 10f, [Scholar], MitKind.Mit),
        new(MitActionId.Excogitation, "Excogitation", 45f, 30f, [Scholar], MitKind.Shield),
        new(MitActionId.Exaltation, "Exaltation", 60f, 8f, [Astrologian], MitKind.Mit),
        new(MitActionId.CelestialIntersection, "Celestial Intersection", 30f, 30f, [Astrologian], MitKind.Shield),
        new(MitActionId.Taurochole, "Taurochole", 45f, 15f, [Sage], MitKind.Mit),
        new(MitActionId.Haima, "Haima", 120f, 15f, [Sage], MitKind.Shield),

        new(MitActionId.Temperance, "Temperance", 120f, 20f, [WhiteMage], MitKind.Mit),
        new(MitActionId.DivineCaress, "Divine Caress", 1f, 10f, [WhiteMage], MitKind.Shield, Requires: 3881),
        new(MitActionId.PlenaryIndulgence, "Plenary Indulgence", 60f, 10f, [WhiteMage], MitKind.Mit),
        new(MitActionId.LiturgyOfTheBell, "Liturgy of the Bell", 180f, 20f, [WhiteMage], MitKind.None),

        new(MitActionId.NeutralSect, "Neutral Sect", 120f, 20f, [Astrologian], MitKind.None),
        new(MitActionId.SunSign, "Sun Sign", 1f, 15f, [Astrologian], MitKind.Mit, Requires: 3895),
        new(MitActionId.HeliosConjunction, "Helios Conjunction", Gcd, 30f, [Astrologian], MitKind.Shield, Aliases: [MitActionId.AspectedHelios]),
        new(MitActionId.CollectiveUnconscious, "Collective Unconscious", 60f, 10f, [Astrologian], MitKind.Mit),
        new(MitActionId.Macrocosmos, "Macrocosmos", 180f, 15f, [Astrologian], MitKind.None),

        new(MitActionId.DeploymentTactics, "Deployment Tactics", 90f, 30f, [Scholar], MitKind.Shield),
        new(MitActionId.Concitation, "Concitation", Gcd, 30f, [Scholar], MitKind.Shield, Aliases: [MitActionId.Succor]),
        new(MitActionId.Expedient, "Expedient", 120f, 20f, [Scholar], MitKind.Mit),
        new(MitActionId.SacredSoil, "Sacred Soil", 30f, 15f, [Scholar], MitKind.Ground),
        new(MitActionId.SummonSeraph, "Summon Seraph", 120f, 30f, [Scholar], MitKind.Shield, PetAction: true),
        new(MitActionId.Consolation, "Consolation", 30f, 30f, [Scholar], MitKind.Shield, PetAction: true),
        new(MitActionId.FeyIllumination, "Fey Illumination", 120f, 20f, [Scholar], MitKind.Mit, PetAction: true),
        new(MitActionId.Seraphism, "Seraphism", 180f, 20f, [Scholar], MitKind.None),
        new(MitActionId.Recitation, "Recitation", 90f, 15f, [Scholar], MitKind.None),

        new(MitActionId.Kerachole, "Kerachole", 30f, 15f, [Sage], MitKind.Mit),
        new(MitActionId.Zoe, "Zoe", 90f, 30f, [Sage], MitKind.None),
        new(MitActionId.EukrasianPrognosisII, "Eukrasian Prognosis II", 1.5f, 30f, [Sage], MitKind.Shield, Aliases: [MitActionId.EukrasianPrognosis]),
        new(MitActionId.Holos, "Holos", 120f, 20f, [Sage], MitKind.Mit),
        new(MitActionId.Panhaima, "Panhaima", 120f, 15f, [Sage], MitKind.Shield),
        new(MitActionId.Philosophia, "Philosophia", 180f, 20f, [Sage], MitKind.None),

        new(MitActionId.Feint, "Feint", 90f, 15f, Melee, MitKind.Debuff),
        new(MitActionId.Addle, "Addle", 90f, 15f, Casters, MitKind.Debuff),
        new(MitActionId.Troubadour, "Troubadour", 90f, 15f, [Bard], MitKind.Mit),
        new(MitActionId.Tactician, "Tactician", 90f, 15f, [Machinist], MitKind.Mit),
        new(MitActionId.ShieldSamba, "Shield Samba", 90f, 15f, [Dancer], MitKind.Mit),
        new(MitActionId.Dismantle, "Dismantle", 120f, 10f, [Machinist], MitKind.Debuff),
        new(MitActionId.MagickBarrier, "Magick Barrier", 120f, 10f, [RedMage], MitKind.Mit),
        new(MitActionId.TemperaCoat, "Tempera Coat", 120f, 10f, [Pictomancer], MitKind.Shield),
        new(MitActionId.TemperaGrassa, "Tempera Grassa", 1f, 10f, [Pictomancer], MitKind.Shield, Requires: 3686),
        new(MitActionId.Improvisation, "Improvisation", 120f, 15f, [Dancer], MitKind.None),
        new(MitActionId.ImprovisedFinish, "Improvised Finish", 1.5f, 30f, [Dancer], MitKind.Shield, Requires: 1827),
    ];

    private static readonly Dictionary<uint, MitActionDef> ById = All
        .SelectMany(def => (def.Aliases ?? []).Append(def.ActionId).Select(id => (id, def)))
        .ToDictionary(x => x.id, x => x.def);

    // A pressed id (an alias resolves to its base button).
    public static MitActionDef? Find(uint actionId) => ById.GetValueOrDefault(actionId);

    public static MitActionDef? PartyMitFor(JobId job) => Find(job switch
    {
        Paladin => MitActionId.DivineVeil,
        Warrior => MitActionId.ShakeItOff,
        DarkKnight => MitActionId.DarkMissionary,
        Gunbreaker => MitActionId.HeartOfLight,
        Bard => MitActionId.Troubadour,
        Machinist => MitActionId.Tactician,
        Dancer => MitActionId.ShieldSamba,
        _ => 0u,
    });

    public static MitActionDef? TankLb3For(JobId job) => Find(job switch
    {
        Paladin => MitActionId.LastBastion,
        Warrior => MitActionId.LandWaker,
        DarkKnight => MitActionId.DarkForce,
        Gunbreaker => MitActionId.GunmetalSoul,
        _ => 0u,
    });

    // A plan entry's button for one job, placeholders resolved.
    public static MitActionDef? For(uint actionId, JobId job) => actionId switch
    {
        MitPlanEntry.PartyMit => PartyMitFor(job),
        MitPlanEntry.TankLb3 => TankLb3For(job),
        _ => Find(actionId),
    };

    public static bool CanPress(JobId job, MitActionDef def) => Array.IndexOf(def.Jobs, job) >= 0;
}

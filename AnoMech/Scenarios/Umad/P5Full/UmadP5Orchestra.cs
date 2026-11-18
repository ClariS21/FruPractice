using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad.P5Full;

// Maddening Orchestra: Flare on both tanks and Holy on three others; Surprise Flare on Kefka's
// highest enmity and Surprise Holy on the other tank; Chaotic Flare on the tanks with Holy on the
// three nearest Kefka; then each Surprise goes off, Flare Diffusion around its holder and Chaotic
// Holy on its own.
internal static class UmadP5Orchestra
{
    // From the Maddening Orchestra cast.
    internal const float FirstNotesAt = 5.88f;
    internal const float SurpriseAt = 6.46f;
    internal const float SecondCastAt = 8.15f;
    internal const float SecondNotesAt = 9.05f;
    internal const float FinaleAt = 12.53f;
    internal const float SurpriseDuration = 6f;
    private const int SecondHolyCount = 3;

    private static readonly Placement Centre = new(Vector3.Zero, MathF.PI);

    public static void Play(SimWorld world, UmadP5Cast cast, UmadP5Enmity enmity, IReadOnlyList<PartyRole> firstHoly)
    {
        world.Events.Add(0f, () => cast.Kefka?.Cast(ActionId.MaddeningOrchestra, animationLock: 3.1f));
        world.Events.Add(FirstNotesAt, () => FirstNotes(world, cast, firstHoly));
        world.Events.Add(SurpriseAt, () => Surprise(world, cast, enmity));
        world.Events.Add(SecondCastAt, () => cast.Kefka?.Cast(ActionId.MaddeningOrchestra2, animationLock: 1.1f));
        world.Events.Add(SecondNotesAt, () => SecondNotes(world, cast, enmity));
    }

    private static void FirstNotes(SimWorld world, UmadP5Cast cast, IReadOnlyList<PartyRole> firstHoly)
    {
        foreach (var tank in (PartyRole[])[PartyRole.MainTank, PartyRole.OffTank])
            if (world.Party.Get(tank) is { } member && member.IsAlive())
                cast.Kefka?.CastFromHelper(UmadActions.Flare, member, at: Centre);
        foreach (var role in firstHoly)
            if (world.Party.Get(role) is { } member && member.IsAlive())
                cast.Kefka?.CastFromHelper(UmadActions.Holy, member, at: Centre);
    }

    // Each goes off as it runs out; nothing takes one off.
    private static void Surprise(SimWorld world, UmadP5Cast cast, UmadP5Enmity enmity)
    {
        var flare = world.Party.Get(enmity.Holder);
        var holy = world.Party.Get(enmity.Second);
        if (flare.IsAlive()) flare!.AddStatus(StatusId.SurpriseFlare, SurpriseDuration);
        if (holy.IsAlive()) holy!.AddStatus(StatusId.SurpriseHoly, SurpriseDuration);
        world.Events.Add(FinaleAt - SurpriseAt, () => Finale(cast, flare, holy));
    }

    private static void SecondNotes(SimWorld world, UmadP5Cast cast, UmadP5Enmity enmity)
    {
        if (world.Party.Get(enmity.Holder) is { } holder && holder.IsAlive())
            cast.Kefka?.CastFromHelper(UmadActions.ChaoticFlare, holder, at: Centre);
        var kefka = cast.Kefka?.Position ?? Vector3.Zero;
        foreach (var nearest in world.Party.ActiveMembers().OrderBy(m => Vector3.DistanceSquared(m.Position with { Y = 0f }, kefka with { Y = 0f })).Take(SecondHolyCount).ToList())
            cast.Kefka?.CastFromHelper(UmadActions.Holy, nearest, at: Centre);
    }

    private static void Finale(UmadP5Cast cast, SimCharacter? flare, SimCharacter? holy)
    {
        if (flare.IsAlive()) cast.Kefka?.CastFromHelper(UmadActions.FlareDiffusion, flare!, at: Centre);
        if (holy.IsAlive()) cast.Kefka?.CastFromHelper(UmadActions.ChaoticHoly, holy!, at: Centre);
    }
}

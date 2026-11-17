using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using static AnoMech.Windows.GroundDraw;

namespace AnoMech.Windows;

// The death recap drawn onto the game world: a red X where you died, a green ring on the strat's
// spot, a line between them, and the outline of the AoE that killed you. A click-through window
// spanning the viewport, open only while there's a recap.
public sealed class DeathRecapOverlay : Window
{
    // Packed by hand: these initialise outside an ImGui frame, where GetColorU32 has no context.
    private static readonly uint DeathColor = Rgba(1f, 0.25f, 0.25f, 1f);
    private static readonly uint SpotColor = Rgba(0.3f, 1f, 0.4f, 1f);
    private static readonly uint LineColor = Rgba(1f, 1f, 1f, 0.85f);
    private static readonly uint AoeColor = Rgba(1f, 0.6f, 0.15f, 0.9f);
    private const float SpotRingRadius = 1f;

    private readonly Game game;

    public DeathRecapOverlay(Game game) : base($"##{PluginIdentity.InternalName}DeathRecapOverlay")
    {
        this.game = game;
        Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs
                | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus
                | ImGuiWindowFlags.NoSavedSettings;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    public override bool DrawConditions() => game.Recap != null;

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
    }

    public override void Draw()
    {
        if (game.Recap is not { } recap) return;
        var draw = ImGui.GetWindowDrawList();
        if (recap.Aoe is { } aoe) DrawAoe(draw, aoe);
        if (recap.Spot is { } spot)
        {
            DrawGroundLine(draw, recap.DiedAt, spot, LineColor, 2f);
            DrawGroundCircle(draw, spot, SpotRingRadius, SpotColor, 3f);
            Label(draw, spot, recap.Strat?.Mechanic ?? "Strat spot", SpotColor);
            if (recap.MissedBy is { } missed && missed > DeathRecap.OnSpotTolerance)
                Label(draw, (recap.DiedAt + spot) / 2f, $"{missed:F1}y", LineColor);
        }
        DrawCross(draw, recap.DiedAt, 0.6f, DeathColor, 3f);
        Label(draw, recap.DiedAt, "You died", DeathColor);
    }

    private static void DrawAoe(ImDrawListPtr draw, DeathAoe aoe)
    {
        var forward = new Vector3(MathF.Sin(aoe.Rotation), 0f, MathF.Cos(aoe.Rotation));
        var right = new Vector3(MathF.Cos(aoe.Rotation), 0f, -MathF.Sin(aoe.Rotation));
        switch (aoe.CastType)
        {
            case 2 or 5 or 6:
                DrawGroundCircle(draw, aoe.Origin, aoe.Range, AoeColor, 2f);
                break;
            case 3 or 13:
                DrawCone(draw, aoe.Origin, aoe.Rotation, aoe.Size ?? MathF.PI / 6f, aoe.Range);
                break;
            case 4 or 12:
                DrawRect(draw, aoe.Origin, forward, right, aoe.HalfWidth, aoe.Range);
                break;
            case 8:
                DrawRect(draw, aoe.Origin, forward, right, aoe.HalfWidth, aoe.Size ?? 100f);
                break;
            case 10:
                DrawGroundCircle(draw, aoe.Origin, aoe.Range, AoeColor, 2f);
                if (aoe.Size is { } inner and > 0f) DrawGroundCircle(draw, aoe.Origin, inner, AoeColor, 2f);
                break;
            case 11:
                DrawRect(draw, aoe.Origin - forward * aoe.Range, forward, right, aoe.HalfWidth, aoe.Range * 2f);
                DrawRect(draw, aoe.Origin - right * aoe.Range, right, -forward, aoe.HalfWidth, aoe.Range * 2f);
                break;
            default:
                return;
        }
        Label(draw, aoe.Origin, aoe.Name, AoeColor);
    }

    // A cone wider than a half plane is drawn as one, clipped to its arc.
    private static void DrawCone(ImDrawListPtr draw, Vector3 origin, float rotation, float halfAngle, float range)
    {
        var points = new List<Vector3> { origin };
        for (var i = 0; i <= CircleSegments; i++)
        {
            var angle = rotation - halfAngle + 2f * halfAngle * i / CircleSegments;
            points.Add(origin + new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * range);
        }
        points.Add(origin);
        DrawGroundPolyline(draw, points, AoeColor, 2f);
    }

    private static void DrawRect(ImDrawListPtr draw, Vector3 back, Vector3 forward, Vector3 right, float halfWidth, float length)
        => DrawGroundPolyline(draw,
        [
            back - right * halfWidth, back + forward * length - right * halfWidth,
            back + forward * length + right * halfWidth, back + right * halfWidth, back - right * halfWidth,
        ], AoeColor, 2f);

}

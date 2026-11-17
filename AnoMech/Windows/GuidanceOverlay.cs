using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using static AnoMech.Windows.GroundDraw;

namespace AnoMech.Windows;

public sealed class GuidanceOverlay : Window
{
    private readonly Game game;
    private static readonly uint Green = Rgba(0.3f, 1f, 0.4f, 1f);
    private static readonly uint DimGreen = Rgba(0.3f, 1f, 0.4f, 0.35f);
    private static readonly uint Red = Rgba(1f, 0.2f, 0.2f, 1f);
    private static readonly uint Orange = Rgba(1f, 0.6f, 0.15f, 1f);
    private static readonly uint White = Rgba(1f, 1f, 1f, 0.8f);

    public GuidanceOverlay(Game game) : base($"##{PluginIdentity.InternalName}GuidanceOverlay")
    {
        this.game = game;
        Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs
                | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus
                | ImGuiWindowFlags.NoSavedSettings;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    public override bool DrawConditions() => Plugin.Config.PathGuidance != PathGuidance.Off && game.Zone is not null
        && (game.Guidance.Debrief is not null || game.Guidance.Recording && Plugin.Config.PathGuidance >= PathGuidance.SpotOnly);

    public override void PreDraw()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
    }

    public override void Draw()
    {
        var draw = ImGui.GetWindowDrawList();
        if (game.Guidance.Debrief is { } debrief) DrawDebrief(draw, debrief);
        else if (game.Guidance.Live() is { } frame && game.Player is { } player)
            DrawLive(draw, frame, player.Position);
    }

    private void DrawLive(ImDrawListPtr draw, GuidanceFrame frame, Vector3 position)
    {
        var full = Plugin.Config.PathGuidance == PathGuidance.FullGuide;
        var here = game.World.Coordinates.ToGlobal(position);
        if (frame.Next is { } next)
        {
            var spot = game.World.Coordinates.ToGlobal(next.Spot);
            DrawGroundCircle(draw, spot, 0.75f, Green, 3f);
            var left = (next.Deadline ?? next.IssuedAt) - game.Events.Elapsed;
            Label(draw, spot, $"{MathF.Max(0, left):F1}s", Green);
            if (next.Why is { Length: > 0 } why) CueLabel(draw, spot, why);
            if (full)
            {
                DrawGroundLine(draw, here, spot, Green, 2f);
                if (frame.Following is { } following)
                {
                    var after = game.World.Coordinates.ToGlobal(following.Spot);
                    DrawGroundLine(draw, spot, after, DimGreen, 2f);
                    DrawGroundCircle(draw, after, 0.75f, DimGreen, 2f);
                }
                foreach (var segment in frame.DangerSegments)
                    DrawGroundLine(draw, Global(segment.From), Global(segment.To), Red, 3f);
                if (frame.ShowGo && !frame.InDangerNow) Label(draw, here, "GO", Green);
            }
        }
        if (frame.InDangerNow)
        {
            DrawGroundCircle(draw, here, 0.75f, Red, 3f);
            Label(draw, here, "MOVE", Red);
        }
    }

    // The strat's reason for the next spot, as a short line beside its ring.
    private static void CueLabel(ImDrawListPtr draw, Vector3 spot, string text)
    {
        if (text.Length > MaxCueLabel) text = text[..(MaxCueLabel - 1)] + "…";
        if (Project(spot) is not { } screen) return;
        var at = screen + new Vector2(CueLabelOffset, -ImGui.CalcTextSize(text).Y / 2f);
        draw.AddText(at + new Vector2(1f, 1f), Rgba(0f, 0f, 0f, 0.8f), text);
        draw.AddText(at, Green, text);
    }

    private const int MaxCueLabel = 60;
    private const float CueLabelOffset = 36f;

    private Vector3 Global(Vector2 p) => game.World.Coordinates.ToGlobal(new Vector3(p.X, 0, p.Y));

    private static void DrawDebrief(ImDrawListPtr draw, GuidanceDebrief result)
    {
        Vector3 At(Vector2 p) => result.Origin + new Vector3(p.X, 0, p.Y);
        DrawGroundPolyline(draw, result.Spots.Select(s => result.Origin + s.Spot).ToArray(), White, 2f);
        for (var i = 1; i < result.Path.Count; i++)
        {
            var safety = (PathSafety)Math.Max((int)result.Colors[i - 1], (int)result.Colors[i]);
            var color = safety == PathSafety.Clip ? Red : safety == PathSafety.Near ? Orange : Green;
            DrawGroundLine(draw, At(result.Path[i - 1].p), At(result.Path[i].p), color, 3f);
        }
        foreach (var finding in result.Findings)
        {
            var color = finding is PathFinding.Clip ? Red : Orange;
            DrawGroundCircle(draw, At(finding.Position), 0.35f, color, 2f);
            Label(draw, At(finding.Position), finding.Text, color);
        }
    }
}

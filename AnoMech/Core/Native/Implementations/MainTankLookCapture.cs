using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AnoMech.Core.Game.Party;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace AnoMech.Core.Native.Implementations;

internal static unsafe class MainTankLookCapture
{
    private static string ConfigDirectory => Plugin.PluginInterface.ConfigDirectory.FullName;
    private static string LocalDirectory => Path.Combine(ConfigDirectory, BotLookLibrary.FolderName);
    private static string? ShippedDirectory => Plugin.PluginInterface.AssemblyLocation.DirectoryName is { } dir ? Path.Combine(dir, BotLookLibrary.FolderName) : null;

    // Shipped plus locally captured looks, without repeats. Never throws.
    public static IReadOnlyList<BotLook> LoadAll()
    {
        try
        {
            BotLookLibrary.ImportLegacy(ConfigDirectory, LocalDirectory);
            return BotLookLibrary.Pool(BotLookLibrary.LoadFolder(ShippedDirectory), BotLookLibrary.LoadFolder(LocalDirectory));
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[MainTankLook] Could not load looks: {e.Message}");
            return [];
        }
    }

    // The chat line for `message`, behind the plugin's prefix.
    internal static string Format(string message) => $"{PluginIdentity.ChatPrefix} {message}";

    private static void Say(string message)
        => Plugin.ChatGui.Print(new XivChatEntry { Type = XivChatType.SystemMessage, Message = Format(message) });

    // /frup looks: the shipped looks then the local ones, numbered; only local ones can be removed.
    public static void List()
    {
        BotLookLibrary.ImportLegacy(ConfigDirectory, LocalDirectory);
        var shipped = BotLookLibrary.LoadFolder(ShippedDirectory);
        var local = BotLookLibrary.LoadFolder(LocalDirectory);
        if (shipped.Count + local.Count == 0) { Say("No looks. /frup capture adds your character's."); return; }
        for (var i = 0; i < shipped.Count; i++) Say($"S{i + 1}. {BotLookLibrary.Describe(shipped[i])} (shipped)");
        for (var i = 0; i < local.Count; i++) Say($"{i + 1}. {BotLookLibrary.Describe(local[i])}");
    }

    // /frup looks remove N: N counts the captured looks as /frup looks numbers them.
    public static void Remove(string indexText)
    {
        if (!int.TryParse(indexText, out var index)) { Say("Usage: /frup looks remove <number>"); return; }
        BotLookLibrary.ImportLegacy(ConfigDirectory, LocalDirectory);
        var removed = BotLookLibrary.Remove(LocalDirectory, index);
        Say(removed == null ? $"No captured look {index}." : $"Removed look {index} \"{BotLookLibrary.LabelOf(removed)}\" - {BotLookLibrary.LoadFolder(LocalDirectory).Count} captured looks");
    }

    public static void Capture(string? name = null)
    {
        if (Plugin.ObjectTable.LocalPlayer is not { } player)
        {
            Say("No local player to capture.");
            return;
        }
        try
        {
            var character = (Character*)player.Address;
            var customize = new byte[26];
            var source = (byte*)&character->DrawData.CustomizeData;
            for (var i = 0; i < customize.Length; i++) customize[i] = source[i];
            var equipment = new EquipmentLook[10];
            for (var i = 0; i < equipment.Length; i++)
            {
                var model = character->DrawData.EquipmentModelIds[i];
                equipment[i] = new EquipmentLook(model.Id, model.Variant, model.Stain0, model.Stain1);
            }
            WeaponLook Weapon(int slot)
            {
                var model = character->DrawData.WeaponData[slot].ModelId;
                return new WeaponLook(model.Id, model.Type, model.Variant, model.Stain0, model.Stain1);
            }
            var look = new BotLook(player.Name.TextValue, (byte)player.ClassJob.RowId, customize, equipment, Weapon(0), Weapon(1));
            BotLookLibrary.ImportLegacy(ConfigDirectory, LocalDirectory);
            Say(BotLookLibrary.CaptureMessage(BotLookLibrary.Add(LocalDirectory, look, name)));
        }
        catch (Exception e)
        {
            DiagnosticLog.Warn($"[MainTankLook] Capture failed: {e}");
            Say($"Capture failed: {e.Message}");
        }
    }
}

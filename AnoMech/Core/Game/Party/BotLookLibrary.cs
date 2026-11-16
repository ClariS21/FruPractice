using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AnoMech.Core.UserActions;

namespace AnoMech.Core.Game.Party;

// The looks the main-tank bot can wear: the ones the build ships plus the ones captured locally
// (one JSON file per look in a folder). Pure file work, so the folders are parameters.
public static class BotLookLibrary
{
    public const string FolderName = "main-tank-looks";
    public const string LegacyFileName = BotLooks.FileName;
    private const string ImportedSuffix = ".imported";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private static readonly string[] Races = ["Hyur", "Elezen", "Lalafell", "Miqo'te", "Roegadyn", "Au Ra", "Hrothgar", "Viera"];

    public static string JobAbbreviation(byte job) => (JobId)job switch
    {
        JobId.Paladin => "PLD", JobId.Monk => "MNK", JobId.Warrior => "WAR", JobId.Dragoon => "DRG", JobId.Bard => "BRD",
        JobId.WhiteMage => "WHM", JobId.BlackMage => "BLM", JobId.Summoner => "SMN", JobId.Scholar => "SCH", JobId.Ninja => "NIN",
        JobId.Machinist => "MCH", JobId.DarkKnight => "DRK", JobId.Astrologian => "AST", JobId.Samurai => "SAM", JobId.RedMage => "RDM",
        JobId.BlueMage => "BLU", JobId.Gunbreaker => "GNB", JobId.Dancer => "DNC", JobId.Reaper => "RPR", JobId.Sage => "SGE",
        JobId.Viper => "VPR", JobId.Pictomancer => "PCT", JobId.Gladiator => "GLA", JobId.Pugilist => "PGL", JobId.Marauder => "MRD",
        JobId.Lancer => "LNC", JobId.Archer => "ARC", JobId.Conjurer => "CNJ", JobId.Thaumaturge => "THM", JobId.Arcanist => "ACN",
        JobId.Rogue => "ROG",
        _ => "JOB" + job,
    };

    // What the look is called in listings: its label, else its character name.
    public static string LabelOf(BotLook look) => string.IsNullOrWhiteSpace(look.Label) ? look.Name : look.Label;

    // The same look: job, customize, equipment and weapons. Names and labels don't count.
    public static bool SameLook(BotLook a, BotLook b)
        => a.ClassJob == b.ClassJob
           && a.Customize.AsSpan().SequenceEqual(b.Customize)
           && a.Equipment.AsSpan().SequenceEqual(b.Equipment)
           && Equals(a.MainHand, b.MainHand)
           && Equals(a.OffHand, b.OffHand);

    public static string Describe(BotLook look)
    {
        var race = look.Customize.Length > 1 && look.Customize[0] is >= 1 and <= 8 ? Races[look.Customize[0] - 1] : "?";
        var sex = look.Customize.Length > 1 ? (look.Customize[1] == 0 ? "male" : "female") : "?";
        var weapon = BotLooks.IsTank(look.ClassJob) && look.MainHand is { Id: not 0 } ? "weapon kept" : "Edenmorn weapon";
        return $"{LabelOf(look)}: {race} {sex}, {JobAbbreviation(look.ClassJob)}, {weapon}";
    }

    // Every readable look in the folder, in file-name order. Never throws.
    public static IReadOnlyList<BotLook> LoadFolder(string? directory) => Files(directory).Select(f => TryRead(f)).OfType<BotLook>().ToList();

    private static IEnumerable<string> Files(string? directory)
    {
        try
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return [];
            return Directory.GetFiles(directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception) { return []; }
    }

    private static BotLook? TryRead(string path)
    {
        try
        {
            var look = JsonSerializer.Deserialize<BotLook>(File.ReadAllText(path));
            return look is { Customize.Length: 26, Equipment.Length: 10 } && !string.IsNullOrWhiteSpace(look.Name) ? look : null;
        }
        catch (Exception) { return null; }
    }

    // The shipped looks then the local ones, without repeats.
    public static IReadOnlyList<BotLook> Pool(IEnumerable<BotLook> shipped, IEnumerable<BotLook> local)
    {
        var pool = new List<BotLook>();
        foreach (var look in shipped.Concat(local))
            if (!pool.Any(p => SameLook(p, look))) pool.Add(look);
        return pool;
    }

    // Adds `look` to the local folder unless it is already there. Index is 1-based.
    public static (bool Added, int Index, BotLook Look, int Count) Add(string directory, BotLook look, string? label = null)
    {
        Directory.CreateDirectory(directory);
        var existing = Files(directory).Select(f => (File: f, Look: TryRead(f))).Where(x => x.Look != null).ToList();
        var duplicate = existing.FindIndex(x => SameLook(x.Look!, look));
        if (duplicate >= 0) return (false, duplicate + 1, existing[duplicate].Look!, existing.Count);

        var abbreviation = JobAbbreviation(look.ClassJob);
        var counter = 1;
        if (string.IsNullOrWhiteSpace(label))
        {
            var taken = existing.Select(x => LabelOf(x.Look!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            while (taken.Contains($"{abbreviation} {counter}")) counter++;
            label = $"{abbreviation} {counter}";
        }
        var named = look with { Label = label.Trim() };
        var next = existing.Select(x => int.TryParse(Path.GetFileName(x.File).Split('-')[0], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        var safe = string.Concat(named.Label!.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        File.WriteAllText(Path.Combine(directory, $"{next:D3}-{safe}.json"), JsonSerializer.Serialize(named, Pretty));
        return (true, existing.Count + 1, named, existing.Count + 1);
    }

    // Removes the look at the 1-based index; null when there is none.
    public static BotLook? Remove(string directory, int index)
    {
        var files = Files(directory).Where(f => TryRead(f) != null).ToList();
        if (index < 1 || index > files.Count) return null;
        var look = TryRead(files[index - 1]);
        File.Delete(files[index - 1]);
        return look;
    }

    // The single look file older builds wrote: moved into the collection once, then renamed so a
    // later removal sticks.
    public static void ImportLegacy(string configDirectory, string collectionDirectory)
    {
        try
        {
            var legacy = Path.Combine(configDirectory, LegacyFileName);
            if (!File.Exists(legacy)) return;
            if (TryRead(legacy) is { } look) Add(collectionDirectory, look);
            File.Move(legacy, legacy + ImportedSuffix, overwrite: true);
        }
        catch (Exception) { }
    }

    public static string CaptureMessage((bool Added, int Index, BotLook Look, int Count) result)
        => result.Added
            ? $"Captured look {result.Index} \"{LabelOf(result.Look)}\" ({JobAbbreviation(result.Look.ClassJob)}) - {Looks(result.Count)}"
            : $"Look already captured as look {result.Index} \"{LabelOf(result.Look)}\" - {Looks(result.Count)}";

    private static string Looks(int count) => count == 1 ? "1 look" : $"{count} looks";
}

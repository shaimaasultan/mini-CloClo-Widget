using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MiniCloClo;

// Learned mishearings (#37): words the speech model keeps writing for this
// person's way of saying a command ("Paul dates" for "pull dates"), kept as
// another way to say it. Only text is stored - what was heard, what worked,
// and the command's name - never audio, in
// %LOCALAPPDATA%\MiniCloClo\learned-phrases.json, on this PC only.
internal sealed class LearnedPhrase
{
    // What was misheard, as written, and in matching spelling.
    public string Heard { get; set; } = "";
    public string Key { get; set; } = "";
    // What worked right after it: said again in its place.
    public string Replay { get; set; } = "";
    // The command's name, as Keeper says it ("the time", "call Mom").
    public string Name { get; set; } = "";
    public string? OtherName { get; set; }
    public DateTime Learned { get; set; }
    public DateTime LastUsed { get; set; }
    public int Uses { get; set; }
}

internal static class LearnedPhrases
{
    public const int MaxCount = 200;
    public const int MaxWords = 6;

    // Debug tests point this at a throwaway folder.
    internal static string? FolderOverride;
    private static string FilePath => Path.Combine(FolderOverride ?? WidgetSettings.FolderPath, "learned-phrases.json");

    public static string KeyOf(string heard) => heard.Length > 160 ? "" : ApprovedVoiceAppLauncher.CommandPhrase(heard);

    public static List<LearnedPhrase> Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 1_000_000) return new();
            var phrases = JsonSerializer.Deserialize<List<LearnedPhrase>>(File.ReadAllText(FilePath)) ?? new();
            return phrases.Where(p => p != null && IsKey(p.Key) && p.Replay is { Length: > 0 and <= 160 } &&
                                      p.Name is { Length: > 0 and <= 100 } && p.Heard.Length <= 160)
                .GroupBy(p => p.Key).Select(g => g.First())
                .Take(MaxCount).ToList();
        }
        catch { return new(); }
    }

    private static void Save(List<LearnedPhrase> phrases)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(phrases, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Learning is a help, not worth failing over. */ }
    }

    private static bool IsKey(string? key) =>
        key is { Length: > 0 and <= 80 } && key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxWords;

    // Whether what failed may be learned as another way to say what worked
    // next: short, not the same words, and never an answer word - "cancel",
    // "stop", "yes", "no" - since a mistake there would change what confirms
    // or cancels.
    public static bool CanLearn(string failed, string worked)
    {
        var key = KeyOf(failed);
        if (!IsKey(key) || key == KeyOf(worked)) return false;
        return !ApprovedVoiceAppLauncher.IsCancelCommand(failed) && !CallVoiceCommand.IsYes(failed) &&
               !QuranVoiceAnswers.IsBack(failed) && !ApprovedVoiceAppLauncher.IsShutdownCommand(failed);
    }

    // "Forget that" / "انسى ده": the learned phrase just used is removed.
    private static readonly string[] ForgetPhrases =
        ["forget that", "forget it", "forget this", "forget that one", "انسي ده", "انسي دي", "انساه", "انساها", "انسي"];

    public static bool IsForget(string heard) => Array.IndexOf(ForgetPhrases, KeyOf(heard)) >= 0;

    public static LearnedPhrase? Find(string heard)
    {
        var key = KeyOf(heard);
        return key.Length == 0 ? null : Load().FirstOrDefault(p => p.Key == key);
    }

    // Learns it (or learns it again for another command); the least recently
    // used go first beyond MaxCount.
    public static LearnedPhrase Add(string failed, string worked, string name, string? otherName, DateTime now)
    {
        var phrase = new LearnedPhrase
        {
            Heard = failed.Trim(), Key = KeyOf(failed), Replay = worked.Trim(), Name = name, OtherName = otherName,
            Learned = now, LastUsed = now,
        };
        var phrases = Load().Where(p => p.Key != phrase.Key).ToList();
        phrases.Add(phrase);
        Save(phrases.OrderByDescending(p => p.LastUsed).Take(MaxCount).OrderBy(p => p.Learned).ToList());
        return phrase;
    }

    public static void Used(LearnedPhrase used, DateTime now)
    {
        var phrases = Load();
        if (phrases.FirstOrDefault(p => p.Key == used.Key) is not { } phrase) return;
        phrase.LastUsed = now;
        phrase.Uses++;
        Save(phrases);
    }

    public static bool Remove(string key)
    {
        var phrases = Load();
        var removed = phrases.RemoveAll(p => p.Key == key) > 0;
        if (removed) Save(phrases);
        return removed;
    }

    public static void RemoveAll() => Save(new());
}

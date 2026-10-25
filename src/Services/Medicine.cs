using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MiniCloClo;

// Medicine reminders that wait for an answer (#29). A custom reminder with
// "Wait for an answer" on asks "Did you take it?" at its time and again
// every few minutes until "yes" - "later" asks again after a while - and
// after the last try the dose is logged as missed. Doses due together are
// asked about together; "yes" answers all that were asked.
internal sealed class MedicineTracker
{
    public sealed class Dose
    {
        public required string Message { get; init; }
        public required DateTime Due { get; init; }
        public required TimeSpan AskAgain { get; init; }
        public required DateTime AskUntil { get; init; }
        public DateTime NextAsk { get; set; }
        public int Asked { get; set; }
    }

    // A later ask after "later" is never sooner than this.
    public static readonly TimeSpan Snooze = TimeSpan.FromMinutes(10);
    private readonly List<Dose> _doses = new();

    public IReadOnlyList<Dose> Doses => _doses;

    // Waiting for "yes": a dose has been asked about.
    public bool Waiting => _doses.Any(d => d.Asked > 0);

    // A reminder's time came: a new dose, asked about now.
    public void Due(CustomReminderSettings reminder, DateTime slot, DateTime now)
    {
        if (_doses.Any(d => d.Message == reminder.Message && d.Due == slot)) return;
        _doses.Add(new Dose
        {
            Message = reminder.Message,
            Due = slot,
            AskAgain = TimeSpan.FromMinutes(reminder.AskAgainMinutes),
            AskUntil = slot.AddMinutes(reminder.AskForMinutes),
            NextAsk = now,
        });
    }

    // What to do now: doses to ask about (again), and doses whose time to
    // answer ran out (missed).
    public (List<Dose> Ask, List<Dose> Missed) Tick(DateTime now)
    {
        var missed = _doses.Where(d => now > d.AskUntil).ToList();
        foreach (var dose in missed) _doses.Remove(dose);
        var ask = _doses.Where(d => now >= d.NextAsk).ToList();
        foreach (var dose in ask)
        {
            dose.Asked++;
            dose.NextAsk = now + dose.AskAgain;
        }
        return (ask, missed);
    }

    // "Yes": every dose that was asked about is taken.
    public List<Dose> Taken()
    {
        var taken = _doses.Where(d => d.Asked > 0).ToList();
        foreach (var dose in taken) _doses.Remove(dose);
        return taken;
    }

    // "Later": asked again after a while.
    public void Later(DateTime now)
    {
        foreach (var dose in _doses.Where(d => d.Asked > 0))
            dose.NextAsk = now + (dose.AskAgain > Snooze ? dose.AskAgain : Snooze);
    }

    // "Cancel": no more asking about these doses.
    public List<Dose> Skip()
    {
        var skipped = _doses.Where(d => d.Asked > 0).ToList();
        foreach (var dose in skipped) _doses.Remove(dose);
        return skipped;
    }

    // The question, for one or more doses.
    public static (string English, string Arabic) Question(IReadOnlyList<Dose> doses) => doses.Count == 1
        ? ($"{doses[0].Message}. Did you take it? Say yes, or later.", $"{doses[0].Message}. خدته؟ قول نعم، أو بعدين.")
        : ($"{string.Join(". ", doses.Select(d => d.Message))}. Did you take them? Say yes, or later.",
           $"{string.Join(". ", doses.Select(d => d.Message))}. خدتهم؟ قول نعم، أو بعدين.");

    // Answers to the question, besides "yes" (CallVoiceCommand.IsYes).
    private static readonly string[] LaterPhrases =
    [
        "later", "not yet", "not now", "in a while", "later please", "ask me later", "no", "not yet no", "remind me later",
        "بعدين", "لسه", "مش دلوقتي", "شويه", "بعد شويه", "لا", "لا لسه", "لا بعدين", "فكرني بعدين",
    ];

    internal static bool IsLater(string transcript) =>
        Array.IndexOf(LaterPhrases, ApprovedVoiceAppLauncher.CommandPhrase(transcript.Length > 160 ? "" : transcript)) >= 0;
}

// The medicine log (#29): each dose taken (with the time), skipped or
// missed, kept 30 days on this PC only.
internal static class MedicineLog
{
    public sealed record Entry(string Message, DateTime Due, string Status, DateTime At);
    public const string Taken = "taken", Missed = "missed", Skipped = "skipped";
    private static readonly TimeSpan Keep = TimeSpan.FromDays(30);

    // Debug tests point this at a throwaway folder.
    internal static string? FolderOverride;
    private static string FilePath => Path.Combine(FolderOverride ?? WidgetSettings.FolderPath, "medicine-log.json");

    public static List<Entry> Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 1_000_000) return new();
            var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new();
            return entries.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Message) && e.Message.Length <= 200 &&
                                      e.Status is Taken or Missed or Skipped).ToList();
        }
        catch { return new(); }
    }

    public static void Record(string message, DateTime due, string status, DateTime at)
    {
        var entries = Load().Where(e => at - e.At <= Keep).ToList();
        entries.Add(new Entry(message, due, status, at));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries.TakeLast(1000).ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* The log is a help, not worth failing over. */ }
    }

    // "Did I take my medicine (today / this morning)?" and "When did I last
    // take it?", for each medicine reminder.
    public static (string English, string Arabic) Answer(IReadOnlyList<string> medicines, string question, DateTime now)
    {
        if (medicines.Count == 0)
            return ("You have no medicine reminders. In Custom reminders, turn on Wait for an answer for one.",
                    "مفيش تذكيرات دوا. في التذكيرات، شغل انتظار الرد لتذكير.");
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(question);
        var entries = Load();
        var english = new List<string>();
        var arabic = new List<string>();
        if (phrase.Contains("last") || phrase.Contains("اخر") || phrase.Contains("امتي"))
        {
            foreach (var medicine in medicines)
            {
                var last = entries.Where(e => e.Message == medicine && e.Status == Taken).OrderBy(e => e.At).LastOrDefault();
                english.Add(last == null ? $"{medicine}: not taken in the last 30 days." : $"{medicine}: last taken {When(last.At, now)}.");
                arabic.Add(last == null ? $"{medicine}: ما اتاخدش آخر 30 يوم." : $"{medicine}: آخر مرة {WhenArabic(last.At, now)}.");
            }
            return (string.Join(" ", english), string.Join(" ", arabic));
        }
        var morning = phrase.Contains("morning") || phrase.Contains("الصبح");
        foreach (var medicine in medicines)
        {
            var today = entries.Where(e => e.Message == medicine && e.Due.Date == now.Date && (!morning || e.Due.Hour < 12))
                .OrderBy(e => e.Due).ToList();
            if (today.Count == 0)
            {
                english.Add($"{medicine}: nothing yet {(morning ? "this morning" : "today")}.");
                arabic.Add($"{medicine}: مفيش لسه {(morning ? "الصبح ده" : "النهارده")}.");
                continue;
            }
            english.Add($"{medicine}: " + string.Join(", ", today.Select(e => e.Status switch
            {
                Taken => $"the {SpokenTime.English(e.Due)} dose taken at {SpokenTime.English(e.At)}",
                Missed => $"the {SpokenTime.English(e.Due)} dose missed",
                _ => $"the {SpokenTime.English(e.Due)} dose skipped",
            })) + ".");
            arabic.Add($"{medicine}: " + string.Join("، ", today.Select(e => e.Status switch
            {
                Taken => $"جرعة {SpokenTime.Arabic(e.Due)} اتاخدت الساعة {SpokenTime.Arabic(e.At)}",
                Missed => $"جرعة {SpokenTime.Arabic(e.Due)} فاتت",
                _ => $"جرعة {SpokenTime.Arabic(e.Due)} اتلغت",
            })) + ".");
        }
        return (string.Join(" ", english), string.Join(" ", arabic));
    }

    private static string When(DateTime at, DateTime now) =>
        at.Date == now.Date ? $"today at {SpokenTime.English(at)}"
        : at.Date == now.Date.AddDays(-1) ? $"yesterday at {SpokenTime.English(at)}"
        : $"on {at.ToString("dddd, MMMM d", System.Globalization.CultureInfo.GetCultureInfo("en-US"))} at {SpokenTime.English(at)}";

    private static string WhenArabic(DateTime at, DateTime now) =>
        at.Date == now.Date ? $"النهارده الساعة {SpokenTime.Arabic(at)}"
        : at.Date == now.Date.AddDays(-1) ? $"إمبارح الساعة {SpokenTime.Arabic(at)}"
        : $"يوم {at.Day}/{at.Month} الساعة {SpokenTime.Arabic(at)}";
}

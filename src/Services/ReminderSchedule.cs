using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniCloClo;

// When custom reminders fire (#39): on the clock, at the reminder's start
// hour plus whole intervals (every 60 minutes from 9 -> 9:00, 10:00...;
// every 90 -> 9:00, 10:30, 12:00...), within its active hours - also across
// midnight. The last time each fired is kept in its settings, so a restart
// or reinstall doesn't start the count again; after the PC was asleep a
// missed reminder is said once, and only if it was due in the last few
// minutes.
internal static class ReminderSchedule
{
    // A slot missed by longer than this (the PC was asleep or off) is marked
    // done without being said.
    public static readonly TimeSpan Late = TimeSpan.FromMinutes(15);

    public static bool IsWithinHours(CustomReminderSettings reminder, DateTime now) =>
        reminder.StartHour < reminder.EndHour
            ? now.Hour >= reminder.StartHour && now.Hour < reminder.EndHour
            : now.Hour >= reminder.StartHour || now.Hour < reminder.EndHour;

    // The latest scheduled time at or before now, or null outside the hours.
    public static DateTime? LatestSlot(CustomReminderSettings reminder, DateTime now)
    {
        if (!IsWithinHours(reminder, now)) return null;
        var start = now.Date.AddHours(reminder.StartHour);
        if (start > now) start = start.AddDays(-1); // hours across midnight
        var steps = (int)Math.Floor((now - start).TotalMinutes / reminder.IntervalMinutes);
        return start.AddMinutes(steps * reminder.IntervalMinutes);
    }

    // A new or newly turned-on reminder starts from the current slot, so it
    // doesn't fire at once - only at its next time.
    public static void Start(CustomReminderSettings reminder, DateTime now) =>
        reminder.LastFired ??= LatestSlot(reminder, now) ?? now;

    // The next time it will fire after now, or null when it is off. Each
    // day's slots start again at the start hour and stop at the end hour.
    public static DateTime? Next(CustomReminderSettings reminder, DateTime now)
    {
        if (!reminder.Enabled) return null;
        var hours = (reminder.EndHour - reminder.StartHour + 24) % 24;
        var span = TimeSpan.FromHours(hours == 0 ? 24 : hours);
        for (var day = now.Date.AddDays(-1); day <= now.Date.AddDays(2); day = day.AddDays(1))
        {
            var start = day.AddHours(reminder.StartHour);
            for (var slot = start; slot < start + span; slot = slot.AddMinutes(reminder.IntervalMinutes))
                if (slot > now && (reminder.LastFired is not { } last || slot > last)) return slot;
        }
        return null;
    }

    // "Next: today at 3:00 PM", "Next: tomorrow at 8:00 AM", "Off".
    public static string NextLabel(CustomReminderSettings reminder, DateTime now)
    {
        if (Next(reminder, now) is not { } next) return "Off";
        var day = next.Date == now.Date ? "today" : next.Date == now.Date.AddDays(1) ? "tomorrow" : next.ToString("dddd");
        return $"Next: {day} at {SpokenTime.English(next)}";
    }

    // Whether a slot is due now (not fired yet), and whether to say it (it
    // isn't too late). A due slot is recorded as fired either way.
    public static (bool Due, bool Say, DateTime Slot) Check(CustomReminderSettings reminder, DateTime now)
    {
        if (!reminder.Enabled || LatestSlot(reminder, now) is not { } slot) return (false, false, default);
        if (reminder.LastFired is { } last && slot <= last) return (false, false, slot);
        return (true, now - slot <= Late, slot);
    }
}

// What Mini CloClo says on its own, waiting its turn (#39): the hourly time,
// custom reminders and the Quran review reminder.
internal sealed record Announcement(Announcement.Kinds Kind, string English, string Arabic, DateTime Created,
    string? AudioPath = null)
{
    public enum Kinds { Hour, Reminder, Medicine, Quran }

    // How long an item may wait (held by the azan, a long prayer look...)
    // before it is dropped, so there is never a backlog: the time of day is
    // soon wrong ("It's 6 PM" at 6:20); a reminder - medicine - still
    // matters an hour later.
    public TimeSpan Expiry => Kind switch
    {
        Kinds.Hour => TimeSpan.FromMinutes(5),
        Kinds.Quran => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromMinutes(60),
    };

    // Everything due together as one announcement, in a fixed order: the
    // time, the reminders, then the Quran review. "It's 3 PM. Reminders:
    // drink water. Take your medicine."
    public static (string English, string Arabic) Combine(IReadOnlyList<Announcement> items)
    {
        var english = new List<string>();
        var arabic = new List<string>();
        foreach (var hour in items.Where(i => i.Kind == Kinds.Hour).Take(1))
        {
            english.Add(hour.English);
            arabic.Add(hour.Arabic);
        }
        var reminders = items.Where(i => i.Kind == Kinds.Reminder).ToList();
        if (reminders.Count == 1)
        {
            english.Add("Reminder: " + reminders[0].English);
            arabic.Add("تذكير: " + reminders[0].Arabic);
        }
        else if (reminders.Count > 1)
        {
            english.Add("Reminders: " + string.Join(" ", reminders.Select(r => Sentence(r.English))));
            arabic.Add("تذكيرات: " + string.Join(" ", reminders.Select(r => Sentence(r.Arabic))));
        }
        // Medicine (#29): the question, already worded (MedicineTracker.Question).
        foreach (var medicine in items.Where(i => i.Kind == Kinds.Medicine).Take(1))
        {
            english.Add((reminders.Count == 0 ? "Reminder: " : "") + medicine.English);
            arabic.Add((reminders.Count == 0 ? "تذكير: " : "") + medicine.Arabic);
        }
        foreach (var quran in items.Where(i => i.Kind == Kinds.Quran).Take(1))
        {
            english.Add(quran.English);
            arabic.Add(quran.Arabic);
        }
        return (string.Join(" ", english.Select(Sentence)), string.Join(" ", arabic.Select(Sentence)));
    }

    private static string Sentence(string text)
    {
        text = text.Trim();
        return text.Length == 0 || ".!?؟".Contains(text[^1]) ? text : text + ".";
    }
}

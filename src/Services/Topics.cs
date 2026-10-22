using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MiniCloClo;

// The person's own questions and answers (#30), by topic: a topic
// ("Islamic dates", "Vacations") holds actions - a short word that is said
// ("Ramadan", "Civic") - and each action answers with its details:
//   - text, read exactly as entered;
//   - calendar dates (one or more a year): the next one, its weekday and how
//     far away it is;
//   - a Hijri month and day: the next time it comes, worked out on this PC.
// Keeper only ever says what was entered or a date it worked out.
public sealed class TopicSettings
{
    public const int MaxTopics = 20;
    public const int MaxActions = 30;

    public string Name { get; set; } = "";
    public string? OtherName { get; set; }
    public List<TopicAction> Actions { get; set; } = new();

    public TopicSettings Copy() => new() { Name = Name, OtherName = OtherName, Actions = Actions.Select(a => a.Copy()).ToList() };

    internal TopicSettings? ValidateAndNormalize()
    {
        if (!TopicAction.IsShortText(Name, 40)) return null;
        Name = Name.Trim();
        OtherName = TopicAction.IsShortText(OtherName, 40) ? OtherName!.Trim() : null;
        Actions = (Actions ?? new List<TopicAction>())
            .Take(MaxActions)
            .Select(a => a?.ValidateAndNormalize())
            .Where(a => a != null)
            .Cast<TopicAction>()
            .ToList();
        return this;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(OtherName) ? Name : $"{Name} — {OtherName}";
}

public sealed class TopicAction
{
    public const string TextKind = "text", CalendarKind = "calendar", HijriKind = "hijri";
    public const int MaxDates = 30;

    // What is said: "Ramadan", and another way to say it, e.g. "رمضان".
    public string Word { get; set; } = "";
    public string? OtherWord { get; set; }
    public string Kind { get; set; } = TextKind;
    // Text: the English and/or Arabic answer, read as entered.
    public string? Text { get; set; }
    public string? OtherText { get; set; }
    // Calendar: dates as yyyy-MM-dd.
    public List<string> Dates { get; set; } = new();
    // Hijri: month 1-12 and day 1-30.
    public int HijriMonth { get; set; } = 9;
    public int HijriDay { get; set; } = 1;
    // Calendar dates of a Hijri event (from tools\Get-CalendarTopics.ps1):
    // HijriMonth and HijriDay say which, so the answer names the Hijri date
    // the dates were published for, not the PC's own reckoning.
    public bool FromHijri { get; set; }

    public TopicAction Copy() => new()
    {
        Word = Word, OtherWord = OtherWord, Kind = Kind, Text = Text, OtherText = OtherText,
        Dates = Dates.ToList(), HijriMonth = HijriMonth, HijriDay = HijriDay, FromHijri = FromHijri,
    };

    internal static bool IsShortText(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max && !value.Any(char.IsControl);

    internal TopicAction? ValidateAndNormalize()
    {
        if (!IsShortText(Word, 40)) return null;
        Word = Word.Trim();
        OtherWord = IsShortText(OtherWord, 40) ? OtherWord!.Trim() : null;
        Kind = Kind is CalendarKind or HijriKind ? Kind : TextKind;
        Text = IsLongText(Text) ? Text!.Trim() : null;
        OtherText = IsLongText(OtherText) ? OtherText!.Trim() : null;
        Dates = (Dates ?? new List<string>())
            .Select(d => ParseDate(d) is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null)
            .Where(d => d != null).Cast<string>()
            .Distinct().OrderBy(d => d, StringComparer.Ordinal).Take(MaxDates).ToList();
        HijriMonth = HijriMonth is >= 1 and <= 12 ? HijriMonth : 9;
        HijriDay = HijriDay is >= 1 and <= 30 ? HijriDay : 1;
        FromHijri = FromHijri && Kind == CalendarKind;
        if (Kind == TextKind && Text == null && OtherText == null) return null;
        return this;
    }

    // Line breaks are allowed in an answer; other control characters aren't.
    private static bool IsLongText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 500 && !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r'));

    // "2027-08-02" (also with / or . between the parts).
    internal static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var cleaned = text.Trim().Replace('/', '-').Replace('.', '-');
        return DateTime.TryParseExact(cleaned, ["yyyy-M-d", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
               && date.Year is >= 2000 and <= 2100 ? date : null;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => (string.IsNullOrWhiteSpace(OtherWord) ? Word : $"{Word} — {OtherWord}") + Kind switch
    {
        CalendarKind => $"  ({Dates.Count} date{(Dates.Count == 1 ? "" : "s")})",
        HijriKind => $"  ({HijriDay} {SpokenTime.HijriMonthName(HijriMonth)})",
        _ => "",
    };

    // The ready-made Islamic dates, editable like any other topic (the key
    // dates Islamic Relief Canada lists in its Islamic calendar).
    internal static List<TopicSettings> IslamicDates() =>
    [
        new()
        {
            Name = "Islamic dates", OtherName = "المناسبات الإسلامية",
            Actions =
            [
                Hijri("Isra and Miraj", "الإسراء والمعراج", 7, 27),
                Hijri("Shab-e-Barat", "نص شعبان", 8, 15),
                Hijri("Ramadan", "رمضان", 9, 1),
                Hijri("Laylat al-Qadr", "ليلة القدر", 9, 27),
                Hijri("Eid al-Fitr", "عيد الفطر", 10, 1),
                Hijri("Dhul Hijjah", "ذو الحجة", 12, 1),
                Hijri("Hajj", "الحج", 12, 8),
                Hijri("Arafah", "عرفة", 12, 9),
                Hijri("Eid al-Adha", "عيد الأضحى", 12, 10),
                Hijri("Islamic new year", "رأس السنة الهجرية", 1, 1),
                Hijri("Ashura", "عاشوراء", 1, 10),
            ],
        },
    ];

    private static TopicAction Hijri(string word, string other, int month, int day) =>
        new() { Word = word, OtherWord = other, Kind = HijriKind, HijriMonth = month, HijriDay = day };
}

// What Keeper says for an action, in English and Arabic.
internal static class TopicAnswers
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly string[] ArabicDays = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];
    private static readonly string[] ArabicMonths =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    internal static (string English, string Arabic) Answer(TopicAction action, DateTime today)
    {
        var name = action.Word;
        var arabicName = action.OtherWord ?? action.Word;
        switch (action.Kind)
        {
            case TopicAction.CalendarKind:
            {
                var next = action.Dates.Select(TopicAction.ParseDate).Where(d => d >= today.Date).Min();
                if (next is not { } date)
                    return action.Dates.Count == 0
                        ? ($"{name} has no dates yet.", $"{arabicName} مالهوش تواريخ لسه.")
                        : ($"{name} has no date after today. Add the next date to it.", $"{arabicName} مالهوش تاريخ جاي. ضيف التاريخ الجاي.");
                return When(name, arabicName, date, today, HijriFor(date, action.FromHijri ? (action.HijriMonth, action.HijriDay) : null));
            }
            case TopicAction.HijriKind:
            {
                if (SpokenTime.NextHijriDate(action.HijriMonth, action.HijriDay, today.Date) is not { } date)
                    return ($"I can't work out the date for {name}.", $"مش قادر أحسب تاريخ {arabicName}.");
                return When(name, arabicName, date, today, HijriFor(date, (action.HijriMonth, action.HijriDay)));
            }
            default:
                return (action.Text ?? action.OtherText ?? "", action.OtherText ?? action.Text ?? "");
        }
    }

    // The Hijri date to say with a day: the event's own day and month when
    // known (1 Ramadan), with the year of the PC's Hijri date nearest that
    // day; otherwise the PC's Hijri date for it.
    private static (string English, string Arabic)? HijriFor(DateTime date, (int Month, int Day)? hijriEvent)
    {
        var (month, day, year) = (0, 0, 0);
        if (hijriEvent is { } e)
        {
            // A published date may be a day or two from the PC's reckoning,
            // even across the new year (1 Muharram), so the year comes from
            // the nearest day in that month.
            foreach (var offset in new[] { 0, 1, -1, 2, -2 })
                if (SpokenTime.HijriDate(date.AddDays(offset)) is { } near && near.Month == e.Month)
                {
                    year = near.Year;
                    break;
                }
            (month, day) = (e.Month, e.Day);
        }
        else if (SpokenTime.HijriDate(date) is { } h) (month, day, year) = (h.Month, h.Day, h.Year);
        if (year == 0) return null;
        return ($"{day} {SpokenTime.HijriMonthName(month)} {year}", $"{day} {SpokenTime.HijriMonthNameArabic(month)} {year}");
    }

    // The name first, then when: "Ramadan. In 109 days, on Monday,
    // February 8, 2027, 1 Ramadan 1448."
    private static (string English, string Arabic) When(string name, string arabicName, DateTime date, DateTime today,
        (string English, string Arabic)? hijri)
    {
        var days = (date.Date - today.Date).Days;
        var day = date.ToString(date.Year == today.Year ? "dddd, MMMM d" : "dddd, MMMM d, yyyy", English);
        var arabicDay = $"{ArabicDays[(int)date.DayOfWeek]} {date.Day} {ArabicMonths[date.Month - 1]}" + (date.Year == today.Year ? "" : $" {date.Year}");
        var hijriText = hijri is { } h ? $", {h.English}" : "";
        var hijriArabic = hijri is { } ha ? $"، {ha.Arabic}" : "";
        return days switch
        {
            0 => ($"{name}. Today, {day}{hijriText}.", $"{arabicName}. النهارده، {arabicDay}{hijriArabic}."),
            1 => ($"{name}. Tomorrow, {day}{hijriText}.", $"{arabicName}. بكرة، {arabicDay}{hijriArabic}."),
            _ => ($"{name}. In {days} days, on {day}{hijriText}.", $"{arabicName}. فاضل {days} يوم، يوم {arabicDay}{hijriArabic}."),
        };
    }

    // "Vacations: Civic, Holidays."
    internal static (string English, string Arabic) TopicContents(TopicSettings topic) =>
        topic.Actions.Count == 0
            ? ($"{topic.Name} has nothing in it yet.", $"{topic.OtherName ?? topic.Name} فاضي لسه.")
            : ($"{topic.Name}: {string.Join(", ", topic.Actions.Select(a => a.Word))}.",
               $"{topic.OtherName ?? topic.Name}: {string.Join("، ", topic.Actions.Select(a => a.OtherWord ?? a.Word))}.");

    internal static (string English, string Arabic) TopicList(IReadOnlyList<TopicSettings> topics) =>
        topics.Count == 0
            ? ("You have no topics yet. Open Questions and answers from the menu to add them.", "مفيش مواضيع لسه. افتح الأسئلة والأجوبة من القائمة عشان تضيف.")
            : ($"Your topics: {string.Join(", ", topics.Select(t => t.Name))}. Say a topic's name to hear what's in it.",
               $"مواضيعك: {string.Join("، ", topics.Select(t => t.OtherName ?? t.Name))}. قول اسم الموضوع عشان تسمع اللي فيه.");
}

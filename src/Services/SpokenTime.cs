using System;
using System.Globalization;

namespace MiniCloClo;

// Times, dates and durations as they're best spoken, in English or Arabic,
// plus the Hijri date - worked out on this PC with Windows' Umm al-Qura
// calendar (it may differ by a day from a local moon sighting).
public static class SpokenTime
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly UmAlQuraCalendar Hijri = new();

    private static readonly string[] HijriMonths =
    [
        "Muharram", "Safar", "Rabi al-Awwal", "Rabi al-Thani", "Jumada al-Ula", "Jumada al-Akhirah",
        "Rajab", "Shaban", "Ramadan", "Shawwal", "Dhu al-Qadah", "Dhu al-Hijjah",
    ];
    private static readonly string[] HijriMonthsArabic =
    [
        "محرم", "صفر", "ربيع الأول", "ربيع الآخر", "جمادى الأولى", "جمادى الآخرة",
        "رجب", "شعبان", "رمضان", "شوال", "ذو القعدة", "ذو الحجة",
    ];
    private static readonly string[] ArabicDays = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];
    private static readonly string[] ArabicMonths =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    // "3:05 PM"
    public static string English(DateTime time) => time.ToString("h:mm tt", EnglishCulture);

    // "3:05 مساءً"
    public static string Arabic(DateTime time)
    {
        var hour = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        return $"{hour}:{time.Minute:00} {(time.Hour < 12 ? "صباحًا" : "مساءً")}";
    }

    public static string ArabicPrayerName(string prayer) => prayer switch
    {
        "Fajr" => "الفجر",
        "Dhuhr" => "الظهر",
        "Asr" => "العصر",
        "Maghrib" => "المغرب",
        "Isha" => "العشاء",
        _ => prayer,
    };

    // The Hijri date, or null outside the calendar's range (1900-2077).
    public static (int Day, int Month, int Year)? HijriDate(DateTime date)
    {
        if (date < Hijri.MinSupportedDateTime || date > Hijri.MaxSupportedDateTime) return null;
        return (Hijri.GetDayOfMonth(date), Hijri.GetMonth(date), Hijri.GetYear(date));
    }

    // "8 Jumada al-Ula 1448 AH" for the widget's date line.
    public static string HijriText(DateTime date) =>
        HijriDate(date) is { } h ? $"{h.Day} {HijriMonths[h.Month - 1]} {h.Year} AH" : "";

    public static string HijriTextArabic(DateTime date) =>
        HijriDate(date) is { } h ? $"{h.Day} {HijriMonthsArabic[h.Month - 1]} {h.Year} هـ" : "";

    // "Tuesday, October 20, 2026. In the Hijri calendar, 8 Jumada al-Ula 1448."
    public static string DateEnglish(DateTime date)
    {
        var text = date.ToString("dddd, MMMM d, yyyy", EnglishCulture) + ".";
        if (HijriDate(date) is { } h) text += $" In the Hijri calendar, {h.Day} {HijriMonths[h.Month - 1]} {h.Year}.";
        return text;
    }

    public static string DateArabic(DateTime date)
    {
        var text = $"النهارده {ArabicDays[(int)date.DayOfWeek]} {date.Day} {ArabicMonths[date.Month - 1]} {date.Year}.";
        if (HijriDate(date) is { } h) text += $" وبالهجري {h.Day} {HijriMonthsArabic[h.Month - 1]} {h.Year}.";
        return text;
    }

    // "1 hour and 5 minutes", "12 minutes", "less than a minute"
    public static string DurationEnglish(TimeSpan span)
    {
        var minutes = (int)Math.Ceiling(span.TotalMinutes);
        if (minutes <= 0) return "less than a minute";
        var hours = minutes / 60;
        minutes %= 60;
        static string Unit(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";
        return hours == 0 ? Unit(minutes, "minute")
            : minutes == 0 ? Unit(hours, "hour")
            : $"{Unit(hours, "hour")} and {Unit(minutes, "minute")}";
    }

    public static string DurationArabic(TimeSpan span)
    {
        var minutes = (int)Math.Ceiling(span.TotalMinutes);
        if (minutes <= 0) return "أقل من دقيقة";
        var hours = minutes / 60;
        minutes %= 60;
        static string Hours(int n) => n == 1 ? "ساعة" : n == 2 ? "ساعتين" : $"{n} ساعات";
        static string Minutes(int n) => n == 1 ? "دقيقة" : n == 2 ? "دقيقتين" : n <= 10 ? $"{n} دقايق" : $"{n} دقيقة";
        return hours == 0 ? Minutes(minutes)
            : minutes == 0 ? Hours(hours)
            : $"{Hours(hours)} و{Minutes(minutes)}";
    }
}

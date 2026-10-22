using System;
using System.Collections.Generic;

namespace MiniCloClo;

// The widget's own spoken commands, answered by CloClo itself rather than by
// opening an app: the time, the date, prayer times, the Quran window, the
// reminders, help, stopping a sound and "I've prayed". Like the app names,
// only these fixed phrases count (compared after the same cleanup, with the
// wake name, "please" and "now" tolerated); the transcript is never used as
// anything but a key into this table.
internal enum WidgetVoiceCommand { Time, Date, NextPrayer, PrayerTimes, Quran, ChooseSurah, QuranNext, QuranPrevious, Reminders, Help, StopSound, Prayed, SayAgain }

internal static class WidgetVoiceCommands
{
    // Phrases use TranscriptCleanup.MatchingKey's spelling: lowercase, no
    // punctuation ("what's" is "what s"), and Arabic with ة→ه, أ/إ/آ→ا, ى→ي.
    private static readonly Dictionary<string, WidgetVoiceCommand> Phrases = Build(
        (WidgetVoiceCommand.Time, [
            "time", "the time", "what time is it", "what s the time", "what is the time", "tell me the time",
            "time please", "clock", "what time", "say the time",
            "الساعه", "الساعه كام", "كام الساعه", "الساعه كم", "كم الساعه", "الوقت", "الوقت كام", "قول الساعه",
        ]),
        (WidgetVoiceCommand.Date, [
            "date", "the date", "what s the date", "what is the date", "today s date", "what day is it",
            "what day is today", "today", "what s today", "hijri date", "the hijri date",
            "التاريخ", "تاريخ النهارده", "النهارده كام", "النهارده ايه", "اليوم ايه", "تاريخ اليوم",
            "التاريخ الهجري", "النهارده كام في الشهر", "ايه تاريخ النهارده",
        ]),
        (WidgetVoiceCommand.NextPrayer, [
            "next prayer", "the next prayer", "when is the next prayer", "prayer", "prayer time",
            "what s the next prayer", "when is prayer", "next salah", "salah",
            "الصلاه", "الصلاه الجايه", "الصلاه الجاية", "امتي الصلاه", "معاد الصلاه", "ميعاد الصلاه",
            "الصلاه امتي", "فاضل قد ايه علي الصلاه", "الاذان امتي", "امتي الاذان",
        ]),
        (WidgetVoiceCommand.PrayerTimes, [
            "prayer times", "all prayer times", "today s prayer times", "the prayer times", "read the prayer times",
            "مواقيت الصلاه", "مواعيد الصلاه", "مواعيد الصلاه النهارده", "مواقيت الصلاه النهارده", "اوقات الصلاه",
        ]),
        (WidgetVoiceCommand.Quran, [
            "quran", "the quran", "koran", "the koran", "open quran", "open the quran", "quran memorization",
            "memorize", "recite", "play quran", "play the quran", "repeat",
            "القران", "افتح القران", "المصحف", "افتح المصحف", "الحفظ", "حفظ القران", "شغل القران", "سمعني",
        ]),
        (WidgetVoiceCommand.ChooseSurah, [
            "choose a surah", "choose surah", "choose a sura", "pick a surah", "select a surah", "open a surah",
            "new surah", "a new surah", "change the surah", "change surah", "another surah", "surah", "a surah",
            "quran surah", "quran sura", "quran a surah", "the quran surah", "koran surah", "surah quran",
            "القران سوره", "قران سوره", "سوره القران", "المصحف سوره",
            "اختار سوره", "اختر سوره", "اختار السوره", "سوره جديده", "غير السوره", "سوره تانيه", "افتح سوره", "سوره",
        ]),
        (WidgetVoiceCommand.QuranNext, [
            "next ayah", "next verse", "memorized", "i memorized it", "next",
            "الايه الجايه", "اللي بعده", "حفظتها", "حفظت", "بعده", "التالي",
        ]),
        (WidgetVoiceCommand.QuranPrevious, [
            "previous ayah", "previous verse", "previous", "go back", "back",
            "الايه اللي فاتت", "اللي قبله", "قبله", "السابق", "ارجع",
        ]),
        (WidgetVoiceCommand.Reminders, [
            "reminders", "my reminders", "open reminders", "custom reminders", "reminder",
            "التذكيرات", "التذكير", "المنبهات", "افتح التذكيرات", "تذكيراتي",
        ]),
        (WidgetVoiceCommand.Help, [
            "help", "what can i say", "commands", "what can you do", "list commands", "the commands",
            "مساعده", "الاوامر", "اقول ايه", "بتعمل ايه", "ساعدني",
        ]),
        (WidgetVoiceCommand.StopSound, [
            "stop sound", "stop the sound", "quiet", "be quiet", "silence", "stop playing", "stop the azan",
            "stop the recitation", "mute", "pause",
            "اسكت", "وقف الصوت", "اسكت شويه", "وقف", "وقف الاذان", "وقف القران", "كفايه",
        ]),
        // The last answer to one of the person's own questions, again.
        (WidgetVoiceCommand.SayAgain, [
            "again", "say it again", "say that again", "say again", "date again", "the date again", "one more time",
            "repeat that", "repeat it", "repeat the date", "tell me again", "what was that", "pardon",
            "تاني", "قولها تاني", "قوله تاني", "قول تاني", "قولي تاني", "كمان مره", "مره كمان", "التاريخ تاني", "عيدها", "عيدها تاني",
        ]),
        (WidgetVoiceCommand.Prayed, [
            "i prayed", "i ve prayed", "i have prayed", "done praying", "prayed", "i finished praying",
            "صليت", "خلاص صليت", "انا صليت", "صليت خلاص",
        ]));

    private static Dictionary<string, WidgetVoiceCommand> Build(params (WidgetVoiceCommand Command, string[] Phrases)[] groups)
    {
        var table = new Dictionary<string, WidgetVoiceCommand>(StringComparer.Ordinal);
        foreach (var (command, phrases) in groups)
            foreach (var phrase in phrases)
                table[TranscriptCleanup.MatchingKey(phrase)] = command;
        return table;
    }

    internal static bool TryMatch(string transcript, out WidgetVoiceCommand command)
    {
        command = default;
        if (transcript.Length > 160) return false;
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript);
        if (Phrases.TryGetValue(phrase, out command)) return true;
        // "CloClo, what time is it now" and the like: also with a leading
        // "tell me" / "قولي".
        foreach (var lead in new[] { "tell me ", "say ", "قولي ", "قول " })
            if (phrase.StartsWith(lead, StringComparison.Ordinal) &&
                Phrases.TryGetValue(phrase[lead.Length..], out command)) return true;
        return false;
    }

    // Tried only after the exact phrases and the app names have not matched.
    internal static bool TryMatchLoosely(string transcript, out WidgetVoiceCommand command)
    {
        command = default;
        return transcript.Length <= 160 && TryMatchKeyword(ApprovedVoiceAppLauncher.CommandPhrase(transcript), out command);
    }

    // A short phrase with one of these words in it, spelled exactly or
    // sounding one sound away ("السعات" for "الساعة"), still counts - but
    // only for commands that just say something or open a window, never for
    // stopping, shutting down or marking a prayer. Two commands sounding
    // equally close match neither.
    private static readonly (WidgetVoiceCommand Command, string Word)[] Keywords =
    [
        (WidgetVoiceCommand.Time, "time"), (WidgetVoiceCommand.Time, "clock"),
        (WidgetVoiceCommand.Time, "الساعه"), (WidgetVoiceCommand.Time, "الوقت"),
        (WidgetVoiceCommand.Date, "date"), (WidgetVoiceCommand.Date, "التاريخ"),
        (WidgetVoiceCommand.NextPrayer, "prayer"), (WidgetVoiceCommand.NextPrayer, "salah"),
        (WidgetVoiceCommand.NextPrayer, "الصلاه"), (WidgetVoiceCommand.NextPrayer, "الاذان"),
        (WidgetVoiceCommand.Quran, "quran"), (WidgetVoiceCommand.Quran, "koran"),
        (WidgetVoiceCommand.ChooseSurah, "surah"), (WidgetVoiceCommand.ChooseSurah, "sura"),
        (WidgetVoiceCommand.ChooseSurah, "surat"), (WidgetVoiceCommand.ChooseSurah, "سوره"),
        (WidgetVoiceCommand.ChooseSurah, "صوره"),
        (WidgetVoiceCommand.Quran, "القران"), (WidgetVoiceCommand.Quran, "المصحف"),
        (WidgetVoiceCommand.Reminders, "reminders"), (WidgetVoiceCommand.Reminders, "التذكيرات"),
        (WidgetVoiceCommand.Help, "help"), (WidgetVoiceCommand.Help, "مساعده"),
    ];

    private static bool TryMatchKeyword(string phrase, out WidgetVoiceCommand command)
    {
        command = default;
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 4) return false;
        // "prayer times" names its own command; a plain keyword would make it
        // the next prayer. "Quran" with "surah" anywhere in it ("Quran,
        // surah please") asks the questions; plain "Quran" never does.
        // An "و" run into it counts too: "القرآن سورة" is often heard as
        // "القرآن وصورة".
        if (Array.Exists(words, w => Array.Exists(Keywords, k => k.Command == WidgetVoiceCommand.ChooseSurah &&
                (k.Word == w || (w.Length > 2 && w[0] == 'و' && k.Word == w[1..])))))
        {
            command = WidgetVoiceCommand.ChooseSurah;
            return true;
        }
        foreach (var word in words)
            foreach (var (candidate, keyword) in Keywords)
                if (word == keyword) { command = candidate; return true; }

        var best = int.MaxValue;
        var tied = false;
        foreach (var word in words)
        {
            var key = ApprovedVoiceAppLauncher.SoundKey(word);
            if (key.Length < 2) continue;
            foreach (var (candidate, keyword) in Keywords)
            {
                var target = ApprovedVoiceAppLauncher.SoundKey(keyword);
                if (target.Length < 2) continue;
                var distance = ApprovedVoiceAppLauncher.EditDistance(key, target);
                // One sound off, and only for words of about the same length.
                if (distance > 1 || Math.Abs(key.Length - target.Length) > 1 || distance > best) continue;
                if (distance == best && candidate != command) tied = true;
                else if (distance < best) tied = false;
                best = distance;
                command = candidate;
            }
        }
        return best != int.MaxValue && !tied;
    }
}

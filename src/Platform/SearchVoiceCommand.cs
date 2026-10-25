using System;
using System.Linq;

namespace MiniCloClo;

internal enum SearchApp { YouTube, Spotify }

// "Search" / "ابحث" (#38): asks which app - YouTube or Spotify - and what
// to search for; "search YouTube" / "ابحث في يوتيوب" skips the app
// question, and "search YouTube for Amr Diab" says the words too. While a
// song or video plays, "pause the song", "resume", "next song" and
// "previous song" control it. Phrases use TranscriptCleanup.MatchingKey's
// spelling (lowercase, no punctuation, ة→ه, أ→ا, ى→ي).
internal static class SearchVoiceCommand
{
    // The app (null: ask) and the words (null: ask).
    internal sealed record Request(SearchApp? App, string? Words);

    private static readonly string[] Leads =
    [
        "search for ", "search on ", "search in ", "search ", "look up ", "find me ", "find ",
        "ابحث عن ", "ابحث في ", "ابحث علي ", "ابحث ", "ابحثلي عن ", "ابحثلي ", "دور علي ", "دور في ", "دور ", "دورلي علي ", "دورلي ",
    ];
    private static readonly string[] Alone = ["search", "search something", "do a search", "a search", "ابحث", "ابحثلي", "دور", "دورلي", "بحث"];

    // Between the app and the words: "search YouTube for ...", "ابحث في يوتيوب عن ...".
    private static readonly string[] ForWords = ["for ", "عن ", "علي "];
    private static readonly string[] AppPrefixes = ["on ", "in ", "the ", "في ", "علي ", "ع "];

    private static readonly (SearchApp App, string[] Names)[] AppNames =
    [
        (SearchApp.YouTube, ["youtube", "you tube", "the youtube", "يوتيوب", "اليوتيوب", "يوتوب", "اليوتوب", "يو تيوب"]),
        (SearchApp.Spotify, ["spotify", "spot ify", "spotty fi", "spotifi", "سبوتيفاي", "سبوتيفي", "سبوتفاي", "اسبوتيفاي", "سبوتيفاى"]),
    ];

    internal static Request? TryParse(string transcript)
    {
        if (transcript.Length > 160) return null;
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript);
        if (Array.IndexOf(Alone, phrase) >= 0) return new Request(null, null);
        foreach (var lead in Leads)
        {
            if (!phrase.StartsWith(lead, StringComparison.Ordinal)) continue;
            var rest = phrase[lead.Length..].Trim();
            if (rest.Length == 0) return new Request(null, null);
            if (LeadingApp(rest) is { } found)
            {
                var words = found.Remainder;
                foreach (var word in ForWords)
                    if (words.StartsWith(word, StringComparison.Ordinal)) words = words[word.Length..].Trim();
                return new Request(found.App, words.Length > 0 ? AsHeard(transcript, words) : null);
            }
            // "Look up" and "find" only with an app ("find me a song" is
            // not a search).
            if (lead.StartsWith("look", StringComparison.Ordinal) || lead.StartsWith("find", StringComparison.Ordinal)) return null;
            // "Search for Amr Diab" / "ابحث عن ...": the words, and which app is asked.
            if (lead is "search for " or "ابحث عن " or "ابحثلي عن " or "دور علي " or "دورلي علي ") return new Request(null, AsHeard(transcript, rest));
            // Anything else after "search" is the app misheard ("Search us
            // put it for you" for "Search Spotify"), never the words: an app
            // name that sounds close, or both questions are asked.
            return new Request(SoundsLikeApp(rest), null);
        }
        return null;
    }

    // The words at the end of what was heard, as they were written ("Amr
    // Diab", "مدرسة"), rather than in matching spelling ("amr diab", "مدرسه").
    private static string AsHeard(string transcript, string words)
    {
        var count = words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var heard = transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (heard.Length < count) return words;
        var tail = string.Join(' ', heard[^count..]);
        return TranscriptCleanup.MatchingKey(tail) == words ? tail : words;
    }

    // An answer to "Which app?": "YouTube", "on Spotify", "سبوتيفاي".
    internal static SearchApp? App(string transcript)
    {
        if (transcript.Length > 80) return null;
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript);
        return LeadingApp(phrase) is { Remainder.Length: 0 } found ? found.App : SoundsLikeApp(phrase);
    }

    private static (SearchApp App, string Remainder)? LeadingApp(string phrase)
    {
        foreach (var prefix in AppPrefixes)
            if (phrase.StartsWith(prefix, StringComparison.Ordinal) && LeadingApp(phrase[prefix.Length..]) is { } after) return after;
        foreach (var (app, names) in AppNames)
            foreach (var name in names.OrderByDescending(n => n.Length))
                if (phrase == name || phrase.StartsWith(name + " ", StringComparison.Ordinal))
                    return (app, phrase[name.Length..].Trim());
        return null;
    }

    // One word one or two sounds off ("spotted fly" isn't, "spotifai" is).
    private static SearchApp? SoundsLikeApp(string phrase)
    {
        var heard = ApprovedVoiceAppLauncher.SoundKey(phrase);
        if (heard.Length < 3) return null;
        SearchApp? best = null;
        foreach (var (app, names) in AppNames)
            foreach (var name in names)
            {
                var target = ApprovedVoiceAppLauncher.SoundKey(name);
                if (ApprovedVoiceAppLauncher.EditDistance(heard, target) <= (target.Length >= 6 ? 2 : 1))
                {
                    if (best != null && best != app) return null;
                    best = app;
                }
            }
        return best;
    }

    // Listening hints while Keeper asks "Which app?".
    internal const string AppPrompt = "YouTube. Spotify.";
    internal const string ArabicAppPrompt = "يوتيوب. سبوتيفاي. YouTube. Spotify.";

    internal static string Name(SearchApp app) => app == SearchApp.YouTube ? "YouTube" : "Spotify";
    internal static string ArabicName(SearchApp app) => app == SearchApp.YouTube ? "يوتيوب" : "سبوتيفاي";

    // While a song or video plays: what to do with it.
    internal enum Control { Pause, Resume, Next, Previous }

    private static readonly (Control Control, string[] Phrases)[] Controls =
    [
        (Control.Pause, [
            "pause the song", "pause song", "pause the music", "pause music", "pause the video", "pause video",
            "stop the song", "stop the music", "stop the video", "stop music",
            "وقف الاغنيه", "وقف المزيكا", "وقف الموسيقي", "وقف الفيديو", "اسكت الاغنيه",
        ]),
        (Control.Resume, [
            "resume", "resume the song", "resume the music", "resume the video", "continue the song", "continue the music",
            "continue the video", "play the song again", "unpause", "play the music",
            "كمل الاغنيه", "كمل المزيكا", "كمل الفيديو", "شغل الاغنيه", "شغل المزيكا", "رجع الاغنيه",
        ]),
        (Control.Next, [
            "next song", "the next song", "next track", "skip the song", "skip song", "skip this song", "next video", "the next video",
            "الاغنيه الجايه", "الاغنيه اللي بعدها", "اللي بعدها", "الفيديو الجاي", "غير الاغنيه",
        ]),
        (Control.Previous, [
            "previous song", "the previous song", "last song", "the last song", "previous track", "previous video", "the previous video",
            "الاغنيه اللي فاتت", "الاغنيه اللي قبلها", "اللي قبلها", "الفيديو اللي فات",
        ]),
    ];

    internal static Control? TryControl(string transcript)
    {
        if (transcript.Length > 80) return null;
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript);
        foreach (var (control, phrases) in Controls)
            if (Array.IndexOf(phrases, phrase) >= 0) return control;
        return null;
    }

    // After the results are read: "the next one" plays the one after the
    // last played; "again" reads the list again.
    // ("التاني" is "the second", so it isn't one of these.)
    private static readonly string[] NextOne = ["next", "next one", "the next one", "another one", "اللي بعده", "التالي", "واحد تاني", "غيره"];
    private static readonly string[] AgainPhrases =
        ["again", "say it again", "read them again", "repeat", "repeat the list", "the list", "تاني", "قولهم تاني", "اقراهم تاني", "عيد", "عيدهم"];

    internal static bool IsNextOne(string transcript) => Array.IndexOf(NextOne, ApprovedVoiceAppLauncher.CommandPhrase(transcript)) >= 0;
    internal static bool IsAgain(string transcript) => Array.IndexOf(AgainPhrases, ApprovedVoiceAppLauncher.CommandPhrase(transcript)) >= 0;

    // "Two", "number 2", "play the first one", "the second one", "شغل التاني".
    private static readonly string[] ChoicePrefixes = ["play number ", "play the ", "play ", "number ", "the ", "شغل رقم ", "شغل ", "رقم "];
    private static readonly string[] ChoiceSuffixes = [" one", " video", " song", " واحد"];

    internal static int? Choice(string transcript)
    {
        if (transcript.Length > 80) return null;
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript);
        foreach (var prefix in ChoicePrefixes)
            if (phrase.StartsWith(prefix, StringComparison.Ordinal) && phrase.Length > prefix.Length) phrase = phrase[prefix.Length..];
        foreach (var suffix in ChoiceSuffixes)
            if (phrase.EndsWith(suffix, StringComparison.Ordinal) && phrase.Length > suffix.Length) phrase = phrase[..^suffix.Length];
        return QuranVoiceAnswers.NumberAlone(phrase);
    }

    // The words as heard, for the search: trimmed, without the full stop
    // Whisper adds, at most 100 characters. Never run as anything else.
    internal static string? Words(string transcript)
    {
        var words = string.Join(' ', transcript.Replace('+', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.', '。', '؟', '?', '!', '،', ',');
        if (words.Length == 0 || words.Any(char.IsControl)) return null;
        return words.Length > 100 ? words[..100] : words;
    }
}

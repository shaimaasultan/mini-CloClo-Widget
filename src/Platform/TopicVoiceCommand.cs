using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniCloClo;

// Matches what was said to the person's own topics and actions (#30):
// "Ramadan", "when is Ramadan", "how many days until Eid", "رمضان امتى",
// "فاضل قد ايه على العيد" - an action; "what topics do I have" - the list
// of topics; a topic's name, or "what's in Vacations" - its actions. Only
// the words that were entered can match, and the built-in commands come
// first (an action word that clashes with one can't be saved).
internal static class TopicVoiceCommand
{
    internal abstract record Match;
    internal sealed record ActionMatch(TopicSettings Topic, TopicAction Action) : Match;
    internal sealed record TopicMatch(TopicSettings Topic) : Match;
    internal sealed record ListMatch : Match;
    // "Eid": every action whose name starts with the word said (Eid al-Fitr
    // and Eid al-Adha).
    internal sealed record ActionsMatch(List<TopicAction> Actions) : Match;

    // In MatchingKey spelling (ى→ي, ة→ه, أ→ا), longest first.
    private static readonly string[] Leads =
    [
        "how many days are left until ", "how many days until ", "how many days till ", "how many days to ", "how long until ",
        "how long till ", "days until ", "days till ", "when is ", "when s ", "when does ", "what s the date of ", "what is the date of ",
        "what s the date for ", "the date of ", "the date for ", "date of ", "date for ", "tell me about ",
        "فاضل قد ايه علي ", "فاضل قد اه علي ", "فاضل كام يوم علي ", "كام يوم علي ", "فاضل اد ايه علي ", "امتي ", "ميعاد ", "معاد ", "تاريخ ",
    ];

    // "Ramadan date", "Ashura s date", "رمضان تاريخه": the date asked for after the word.
    private static readonly string[] Tails =
    [
        " امتي", " starts", " start", " begins", " begin", " s date", " date", " dates", " day",
        " تاريخه", " تاريخها", " ميعاده", " ميعادها", " معاده", " معادها", " التاريخ",
    ];
    private static readonly string WhenSound = ApprovedVoiceAppLauncher.SoundKey("امتي");

    private static readonly string[] ListPhrases =
    [
        "what topics do i have", "my topics", "topics", "list my topics", "what are my topics", "questions and answers",
        "مواضيعي", "المواضيع", "ايه المواضيع", "عندي مواضيع ايه", "الاسئله والاجوبه",
    ];

    private static readonly string[] TopicLeads = ["what s in ", "what is in ", "open ", "ايه في ", "ايه اللي في "];

    // sameSound: when loose, only words that sound exactly the same count
    // (used against a built-in command: "A surah" is Ashura, "Surah" isn't).
    internal static Match? TryMatch(string transcript, IReadOnlyList<TopicSettings> topics, bool loose, bool sameSound = false)
    {
        if (transcript.Length > 160 || topics.Count == 0) return null;
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript);
        if (phrase.Length == 0) return null;
        if (Array.IndexOf(ListPhrases, phrase) >= 0) return new ListMatch();

        var asked = Strip(phrase, Leads);
        // Before an ending is taken off: "Canada Day" is a holiday's name.
        var askedWhole = asked;
        foreach (var tail in Tails)
            if (asked.EndsWith(tail, StringComparison.Ordinal)) asked = asked[..^tail.Length].Trim();
        // "امتى" at the end, heard a sound off ("رمضان أمجي").
        var lastSpace = asked.LastIndexOf(' ');
        if (lastSpace > 0 && ApprovedVoiceAppLauncher.EditDistance(ApprovedVoiceAppLauncher.SoundKey(asked[(lastSpace + 1)..]), WhenSound) <= 1)
            asked = asked[..lastSpace];
        // One word that sounds exactly like a one-word list phrase ("موضعي").
        if (!phrase.Contains(' ') && ApprovedVoiceAppLauncher.SoundKey(phrase) is { Length: >= 2 } phraseSound &&
            ListPhrases.Any(p => !p.Contains(' ') && ApprovedVoiceAppLauncher.SoundKey(p) == phraseSound))
            return new ListMatch();
        var topicAsked = Strip(phrase, TopicLeads);

        // Exact words first: actions, then topic names.
        foreach (var topic in topics)
            foreach (var action in topic.Actions)
                if (Keys(action.Word, action.OtherWord) is var keys && (keys.Contains(askedWhole) || keys.Contains(asked)))
                    return new ActionMatch(topic, action);
        foreach (var topic in topics)
            if (Keys(topic.Name, topic.OtherName).Contains(topicAsked)) return new TopicMatch(topic);
        // One word that starts actions' names - exactly, or sounding the same
        // ("العيدي" for "عيد").
        var askedSound = ApprovedVoiceAppLauncher.SoundKey(asked);
        var starting = topics.SelectMany(t => t.Actions)
            .Where(a => Keys(a.Word, a.OtherWord).Any(k => k.StartsWith(asked + " ", StringComparison.Ordinal) ||
                                                          (asked.StartsWith("ال", StringComparison.Ordinal) && k.StartsWith(asked[2..] + " ", StringComparison.Ordinal)) ||
                                                          (!asked.Contains(' ') && k.Contains(' ') && askedSound.Length >= 2 &&
                                                           ApprovedVoiceAppLauncher.SoundKey(k[..k.IndexOf(' ')]) == askedSound)))
            .ToList();
        if (asked.Length >= 3 && starting.Count > 0) return starting.Count == 1 ? new ActionMatch(topics.First(t => t.Actions.Contains(starting[0])), starting[0]) : new ActionsMatch(starting);
        if (!loose) return null;

        // Then one or two sounds away ("Ramadhan"), when only one is that close.
        Match? best = null;
        var bestDistance = int.MaxValue;
        var tied = false;
        void Consider(string heard, IEnumerable<string> keys, Match candidate)
        {
            var heardSound = ApprovedVoiceAppLauncher.SoundKey(heard);
            if (heardSound.Length < 2) return;
            var heardWords = heard.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            foreach (var key in keys)
            {
                var target = ApprovedVoiceAppLauncher.SoundKey(key);
                // The same number of words ("Hello there" is not "Holidays"),
                // unless it sounds exactly the same split differently ("A
                // surah" is "Ashura").
                if (key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != heardWords && heardSound != target) continue;
                var allowed = target.Length >= 6 ? 2 : target.Length >= 3 ? 1 : 0;
                var distance = ApprovedVoiceAppLauncher.EditDistance(heardSound, target);
                if (distance > (sameSound ? 0 : allowed) || distance > bestDistance) continue;
                if (distance == bestDistance && !Same(best, candidate)) tied = true;
                else if (distance < bestDistance) tied = false;
                bestDistance = distance;
                best = candidate;
            }
        }
        foreach (var topic in topics)
        {
            foreach (var action in topic.Actions)
                Consider(asked, Keys(action.Word, action.OtherWord), new ActionMatch(topic, action));
            Consider(topicAsked, Keys(topic.Name, topic.OtherName), new TopicMatch(topic));
        }
        return tied ? null : best;
    }

    private static bool Same(Match? a, Match b) => (a, b) switch
    {
        (ActionMatch x, ActionMatch y) => ReferenceEquals(x.Action, y.Action),
        (TopicMatch x, TopicMatch y) => ReferenceEquals(x.Topic, y.Topic),
        _ => false,
    };

    private static string Strip(string phrase, string[] leads)
    {
        foreach (var lead in leads)
            if (phrase.StartsWith(lead, StringComparison.Ordinal)) return phrase[lead.Length..].Trim();
        return phrase;
    }

    private static List<string> Keys(string first, string? second)
    {
        var keys = new List<string>();
        foreach (var text in new[] { first, second })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var key = TranscriptCleanup.MatchingKey(text);
            if (key.Length == 0) continue;
            keys.Add(key);
            // "the Hajj" / "الحج": also without the article.
            if (key.StartsWith("the ", StringComparison.Ordinal)) keys.Add(key[4..]);
            if (key.StartsWith("ال", StringComparison.Ordinal) && key.Length > 3) keys.Add(key[2..]);
        }
        return keys;
    }

    // Why a word can't be an action or topic name: it is already a built-in
    // command, an app name or a call. Null when it is free.
    internal static string? Clash(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        if (WidgetVoiceCommands.TryMatch(word, out var command)) return $"\"{word.Trim()}\" is already a command ({command}).";
        if (Array.IndexOf(ListPhrases, ApprovedVoiceAppLauncher.CommandPhrase(word)) >= 0) return $"\"{word.Trim()}\" is already a command (topics).";
        if (ApprovedVoiceAppLauncher.TryMatchHeldCommand(word, out var app)) return $"\"{word.Trim()}\" already opens an app ({app}).";
        if (ApprovedVoiceAppLauncher.IsCancelCommand(word) || ApprovedVoiceAppLauncher.IsShutdownCommand(word)) return $"\"{word.Trim()}\" is already a command.";
        // "Call ..." itself; a word that only sounds like a call verb
        // ("Dhul Hijjah") is fine, since such a call needs a saved contact.
        if (CallVoiceCommand.TryParse(word, out var joined) != null && !joined) return $"\"{word.Trim()}\" sounds like a call command.";
        return null;
    }

    // The action words are not added to the recognizer's hint: with them
    // (the Islamic dates especially) "اتصل بماما" was heard as a religious
    // phrase and "اسكت" as "أسكوط".
}

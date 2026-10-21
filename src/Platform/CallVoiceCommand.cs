using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniCloClo;

// "Call Mom" / "اتصل بماما": who to call, matched only against the saved
// contacts - a number is never taken from speech. The match is then read
// back and dialed only after "yes" (MainWindow.Calls.cs).
internal static class CallVoiceCommand
{
    // Longest first, so "call to" wins over "call". Arabic in MatchingKey
    // spelling (ى→ي, so "على" is "علي"); "ب" is often written joined to the
    // name ("بماما"), which TryParse handles.
    private static readonly string[] Leads =
    [
        "make a call to ", "place a call to ", "give a call to ", "call up ", "call to ", "phone ", "call ", "dial ", "ring ",
        "اعمل مكالمه ل", "اتصل علي ", "اتصل ب ", "اتصل ب", "اتصل ", "كلملي ", "كلم ", "رن علي ", "اطلب ",
    ];

    private static readonly string[] YesPhrases =
    [
        "yes", "yeah", "yep", "yes please", "yes call", "yes call him", "yes call her", "call", "call them", "ok", "okay", "sure", "confirm", "go ahead", "do it",
        "ايوه", "ايوا", "اه", "نعم", "اكيد", "تمام", "ماشي", "اتصل", "كلمه", "كلمها", "يلا", "موافق",
    ];

    // Words that turn a reply into a no, whatever else it says.
    private static readonly string[] NoWords =
        ["no", "not", "don", "dont", "cancel", "stop", "wait", "لا", "لأ", "الغاء", "الغي", "بلاش", "استنى", "وقف"];

    // Used only while a call waits for confirmation. Whisper rarely writes a
    // bare "yes": it gives "Yes, yes.", "Yes, call him." or echoes the end of
    // its hints ("Yes. Call sho."). So a short reply (at most six words)
    // that starts with a yes word and has no "no" word in it is a yes. A
    // near miss of a yes word ("بايوو" for "ايوه") counts too.
    internal static bool IsYes(string transcript)
    {
        if (transcript.Length > 160) return false;
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript);
        if (Array.IndexOf(YesPhrases, phrase) >= 0) return true;
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 6 || words.Any(w => Array.IndexOf(NoWords, w) >= 0)) return false;
        return IsYesWord(words[0]);
    }

    private static readonly string[] YesWords =
        ["yes", "yeah", "yep", "yup", "ok", "okay", "sure", "confirm", "ايوه", "ايوا", "اه", "نعم", "اكيد", "تمام", "ماشي", "موافق", "يلا"];

    private static bool IsYesWord(string word)
    {
        if (Array.IndexOf(YesWords, word) >= 0) return true;
        foreach (var candidate in new[] { word, word.Length > 3 && word[0] is 'ب' or 'و' ? word[1..] : word })
        {
            var key = ApprovedVoiceAppLauncher.SoundKey(candidate);
            foreach (var yes in YesWords)
            {
                var target = ApprovedVoiceAppLauncher.SoundKey(yes);
                if (target.Length >= 3 && ApprovedVoiceAppLauncher.EditDistance(key, target) <= 1) return true;
            }
        }
        return false;
    }

    private static readonly string[] Verbs = ["call", "phone", "dial", "ring", "اتصل", "كلم", "كلملي", "رن"];

    // "Call ..." with the verb misheard by a sound ("اتصلر بأحمد"): the
    // first word sounds like a call verb. Returns the words after it.
    private static string? LooseLead(string phrase)
    {
        var space = phrase.IndexOf(' ');
        if (space <= 0) return null;
        var first = ApprovedVoiceAppLauncher.SoundKey(phrase[..space]);
        foreach (var verb in Verbs)
        {
            var target = ApprovedVoiceAppLauncher.SoundKey(verb);
            if (target.Length >= 2 && ApprovedVoiceAppLauncher.EditDistance(first, target) <= (target.Length >= 4 ? 1 : 0))
                return phrase[(space + 1)..].Trim();
        }
        return null;
    }

    // The name after "call ...", or null when it isn't a call command.
    internal static string? TryParse(string transcript)
    {
        if (transcript.Length > 160) return null;
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript);
        foreach (var lead in Leads)
        {
            if (!phrase.StartsWith(lead, StringComparison.Ordinal)) continue;
            var name = phrase[lead.Length..].Trim();
            foreach (var filler in new[] { "the number of ", "my ", "رقم " })
                if (name.StartsWith(filler, StringComparison.Ordinal)) name = name[filler.Length..];
            return name.Length > 0 ? name : null;
        }
        var loose = LooseLead(phrase);
        if (!string.IsNullOrEmpty(loose)) return loose;
        // A short name run into the verb: "Call sho." heard as "Calcia".
        // What follows "call"/"cal" is then matched against the contacts
        // like any name, and still has to be confirmed with "yes".
        if (!phrase.Contains(' '))
            foreach (var verb in new[] { "call", "cal", "kol", "col" })
                if (phrase.Length > verb.Length + 1 && phrase.StartsWith(verb, StringComparison.Ordinal))
                    return phrase[verb.Length..];
        return null;
    }

    // The saved contact with this name (or other name): the same spelling,
    // or - when only one contact is that close - one sound away for short
    // names and two for long ones.
    internal static ContactSettings? Match(string spokenName, IReadOnlyList<ContactSettings> contacts)
    {
        // The whole of what followed "call", then its last three, two and
        // one words: "like the mom" still finds Mom.
        if (MatchName(spokenName, contacts) is { } whole) return whole;
        var words = spokenName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var take = Math.Min(words.Length - 1, 3); take >= 1; take--)
            if (MatchName(string.Join(' ', words[^take..]), contacts) is { } part) return part;
        return null;
    }

    private static ContactSettings? MatchName(string spokenName, IReadOnlyList<ContactSettings> contacts)
    {
        var candidates = new List<string> { spokenName };
        // "اتصل بماما" leaves "ماما" after the "اتصل ب" lead, but a name that
        // itself starts with ب is also tried with it.
        if (spokenName.StartsWith('ب') && spokenName.Length > 2) candidates.Add(spokenName[1..]);

        foreach (var candidate in candidates)
            foreach (var contact in contacts)
                if (Keys(contact).Contains(candidate)) return contact;

        // The end of a name cut off ("مام" for "ماما", "Ahm" for "Ahmed"):
        // at least three letters, and only one contact starting that way.
        foreach (var candidate in candidates.Where(c => c.Length >= 3))
        {
            var starting = contacts.Where(c => Keys(c).Any(k => k.StartsWith(candidate, StringComparison.Ordinal))).ToList();
            if (starting.Count == 1) return starting[0];
        }

        ContactSettings? best = null;
        var bestDistance = int.MaxValue;
        var tied = false;
        foreach (var candidate in candidates)
        {
            var heard = ApprovedVoiceAppLauncher.SoundKey(candidate);
            if (heard.Length == 0) continue;
            foreach (var contact in contacts)
                foreach (var key in Keys(contact))
                {
                    var target = ApprovedVoiceAppLauncher.SoundKey(key);
                    var allowed = target.Length >= 6 ? 2 : target.Length >= 3 ? 1 : 0;
                    var distance = ApprovedVoiceAppLauncher.EditDistance(heard, target);
                    if (distance > allowed || distance > bestDistance) continue;
                    if (distance == bestDistance && !ReferenceEquals(best, contact)) tied = true;
                    else if (distance < bestDistance) tied = false;
                    bestDistance = distance;
                    best = contact;
                }
        }
        return tied ? null : best;
    }

    private static IEnumerable<string> Keys(ContactSettings contact)
    {
        yield return TranscriptCleanup.MatchingKey(contact.Name);
        if (!string.IsNullOrWhiteSpace(contact.OtherName)) yield return TranscriptCleanup.MatchingKey(contact.OtherName);
    }

    // Hints for the recognizer: the saved names, so they are written the way
    // they were saved, with "Call" once for English ("Call Mom, Ahmed.").
    // Without it "Call Mom" came out as "Calma"; with "call" per name it
    // heard "call" in other commands.
    internal static string RecognitionHint(IReadOnlyList<ContactSettings> contacts, bool arabic)
    {
        if (contacts.Count == 0) return "";
        var names = contacts.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Take(12).ToList();
        var others = contacts.Select(c => c.OtherName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Take(12).ToList();
        var english = names.Count > 0 ? $" Call {string.Join(", ", names)}." : "";
        // Arabic: names only - even one "اتصل" made "الصلاة" heard as "اتصل".
        var arabicNames = others.Count > 0 ? $" {string.Join("، ", others)}." : "";
        return arabic ? arabicNames : english + arabicNames;
    }
}

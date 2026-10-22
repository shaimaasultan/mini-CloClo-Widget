using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniCloClo;

// Understands the answers to the choose-a-surah questions (MainWindow
// .QuranDialog.cs): a surah by number or name, an ayah number, "all" (the
// whole surah) and "to the end". Works on TranscriptCleanup.MatchingKey text,
// so Arabic is unified (ة→ه, أ→ا, ى→ي) and Arabic-Indic digits are 0-9.
// Like every voice command, nothing heard is run - it only picks a number.
internal static class QuranVoiceAnswers
{
    // Words around an answer that aren't part of it: "surah 2", "ayah 3",
    // "from 3", "to 5", "number 4", "سورة البقرة", "من الآية 3", "لحد 5".
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "surah", "sura", "surat", "soura", "sourah", "ayah", "aya", "ayat", "verse", "verses", "number", "no",
        "from", "to", "until", "till", "up", "the", "of", "please", "starting", "at", "on", "and", "let", "s", "go",
        "سوره", "سورت", "صوره", "صورت", "الايه", "ايه", "اية", "الايات", "ايات", "رقم", "من", "ل", "لحد", "الي", "حتي", "عند", "لغايه",
        "يعني", "بقي", "خلاص", "و", "في",
    };

    private static readonly Dictionary<string, int> Units = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["oh"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
        ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
        ["first"] = 1, ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5, ["sixth"] = 6, ["seventh"] = 7,
        ["eighth"] = 8, ["ninth"] = 9, ["tenth"] = 10, ["won"] = 1, ["too"] = 2, ["for"] = 4, ["ate"] = 8,
        ["start"] = 1, ["beginning"] = 1, ["begining"] = 1,
        // Arabic, standard and Egyptian, cardinal and ordinal (MatchingKey spelling).
        ["صفر"] = 0, ["زيرو"] = 0, ["واحد"] = 1, ["واحده"] = 1, ["اتنين"] = 2, ["اثنين"] = 2, ["اثنان"] = 2, ["تلاته"] = 3, ["ثلاثه"] = 3, ["ثلاث"] = 3, ["تلات"] = 3,
        ["اربعه"] = 4, ["اربع"] = 4, ["خمسه"] = 5, ["خمس"] = 5, ["سته"] = 6, ["ست"] = 6, ["سبعه"] = 7, ["سبع"] = 7,
        ["تمانيه"] = 8, ["ثمانيه"] = 8, ["تمن"] = 8, ["ثماني"] = 8, ["تسعه"] = 9, ["تسع"] = 9, ["عشره"] = 10, ["عشر"] = 10,
        ["حداشر"] = 11, ["احدعشر"] = 11, ["اتناشر"] = 12, ["اثناعشر"] = 12, ["تلتاشر"] = 13, ["اربعتاشر"] = 14, ["خمستاشر"] = 15,
        ["ستاشر"] = 16, ["سبعتاشر"] = 17, ["تمنتاشر"] = 18, ["تسعتاشر"] = 19,
        ["الاول"] = 1, ["الاولي"] = 1, ["اول"] = 1, ["التاني"] = 2, ["التانيه"] = 2, ["الثاني"] = 2, ["الثانيه"] = 2,
        ["التالت"] = 3, ["التالته"] = 3, ["الثالث"] = 3, ["الثالثه"] = 3, ["الرابع"] = 4, ["الرابعه"] = 4,
        ["الخامس"] = 5, ["الخامسه"] = 5, ["السادس"] = 6, ["السادسه"] = 6, ["السابع"] = 7, ["السابعه"] = 7,
        ["التامن"] = 8, ["التامنه"] = 8, ["الثامن"] = 8, ["الثامنه"] = 8, ["التاسع"] = 9, ["التاسعه"] = 9,
        ["العاشر"] = 10, ["العاشره"] = 10,
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.Ordinal)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
        ["عشرين"] = 20, ["تلاتين"] = 30, ["ثلاثين"] = 30, ["اربعين"] = 40, ["خمسين"] = 50, ["ستين"] = 60, ["سبعين"] = 70,
        ["تمانين"] = 80, ["ثمانين"] = 80, ["تسعين"] = 90,
    };

    private static readonly Dictionary<string, int> Hundreds = new(StringComparer.Ordinal)
    {
        ["hundred"] = 100, ["ميه"] = 100, ["مايه"] = 100, ["مائه"] = 100, ["ميت"] = 100, ["ميتين"] = 200, ["مئتين"] = 200, ["مئتان"] = 200,
    };

    private static readonly string[] AllPhrases =
    [
        "all", "all of it", "the whole surah", "whole surah", "the whole thing", "whole", "everything", "the full surah",
        "full surah", "full", "entire surah", "the entire surah", "all the surah", "all of the surah", "all ayat", "all the ayat",
        "كلها", "كله", "الكل", "السوره كلها", "كل السوره", "السوره كامله", "كامله", "كلهم", "الايات كلها", "من الاول للاخر",
    ];

    private static readonly string[] EndPhrases =
    [
        "the end", "end", "the last", "last", "the last ayah", "the end of the surah", "end of the surah", "to the end",
        "الاخر", "للاخر", "لاخرها", "اخرها", "اخر السوره", "لاخر السوره", "اخر ايه", "النهايه",
    ];

    internal static bool IsAll(string transcript)
    {
        var phrase = Phrase(transcript);
        if (Array.IndexOf(AllPhrases, phrase) >= 0) return true;
        // One word that sounds exactly like a one-word "all" ("قوليها" for "كلها").
        if (phrase.Length == 0 || phrase.Contains(' ')) return false;
        var key = ApprovedVoiceAppLauncher.SoundKey(phrase);
        return key.Length >= 3 && AllPhrases.Where(a => !a.Contains(' '))
            .Any(a => ApprovedVoiceAppLauncher.SoundKey(a) == key);
    }

    // "No" to what Keeper read back: go back one question.
    private static readonly string[] BackPhrases =
    [
        "no", "nope", "no no", "back", "go back", "wrong", "that s wrong", "no that s wrong", "no wrong", "not that", "no go back",
        "لا", "لأ", "لا لا", "ارجع", "رجوع", "غلط", "لا غلط", "مش كده", "لا مش كده", "لا ارجع",
    ];

    private static readonly string[] BackWords = ["no", "nope", "back", "wrong", "لا", "ارجع", "رجوع", "غلط"];

    // Whisper often repeats a short "لا" and adds a stray word after it
    // ("لا. لا. لديك."): a short reply that starts with a back word counts
    // too - unless it also says a number, so "no, five" answers 5.
    internal static bool IsBack(string transcript)
    {
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript.Length > 160 ? "" : transcript);
        if (Array.IndexOf(BackPhrases, phrase) >= 0) return true;
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 2 and <= 5 && Array.IndexOf(BackWords, words[0]) >= 0 &&
               !words.Any(w => w.Any(char.IsAsciiDigit)) && Number(string.Join(' ', words.Skip(1))) == null &&
               !IsAll(string.Join(' ', words.Skip(1)));
    }

    internal static bool IsEnd(string transcript)
    {
        var phrase = Phrase(transcript);
        if (Array.IndexOf(EndPhrases, phrase) >= 0) return true;
        var words = Words(phrase);
        return words.Length > 0 && Array.IndexOf(EndPhrases, string.Join(' ', words)) >= 0;
    }

    // A number said digit by digit - the way Keeper asks for it: "one four"
    // is 14, "two three four" 234, "واحد سبعة" 17 - or as digits ("3", "٣"),
    // whole words ("fourteen", "خمسة وعشرين") or an ordinal ("التالتة"), with
    // filler words around it ignored. Only the ten digit words are matched
    // by sound ("tree" for "three", "سلاسة" for "ثلاثة"): fewer, more
    // different words, so a sound-alike can't be read as the wrong number.
    internal static int? Number(string transcript) => Number(transcript, out _);

    // In Arabic voice mode a number may be said in Arabic or in English: the
    // phrase is read both ways (english is null otherwise). A reading that is
    // only a number wins over one with other words around it, and Arabic
    // over English when both are - or neither is - clean.
    internal static int? Number(string arabic, string? english)
    {
        var fromArabic = Number(arabic, out var arabicClean);
        if (english == null || (fromArabic != null && arabicClean)) return fromArabic;
        var fromEnglish = Number(english, out var englishClean);
        if (fromEnglish != null && englishClean) return fromEnglish;
        return fromArabic ?? fromEnglish;
    }

    // clean: nothing but the number was said (besides filler words).
    private static int? Number(string transcript, out bool clean)
    {
        clean = false;
        var phrase = Phrase(transcript);
        var words = Words(phrase);
        if (words.Length == 0) return null;
        // Digit by digit, as numerals or words ("1 4", "واحد 4"): only when
        // nothing else was said, since "from 3 to 5" is not 35.
        if (words.Length >= 2 && words.Length == phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length &&
            words.All(w => Digit(w) != null))
        {
            clean = true;
            return int.Parse(string.Concat(words.Select(w => Digit(w))));
        }
        // Digits anywhere: Whisper usually writes numbers that way.
        foreach (var word in words)
            if (word.All(char.IsAsciiDigit) && word.Length <= 4)
            {
                clean = words.Length == 1;
                return int.Parse(word);
            }

        var total = 0;
        var current = 0;
        var any = false;
        var digits = new List<int>(); // the single digits said, for "one four"
        var onlyDigits = true;
        foreach (var word in words)
        {
            if (Hundreds.TryGetValue(word, out var hundred))
            {
                // "two hundred" multiplies; "ميه" / "ميتين" stand alone.
                total += word == "hundred" ? Math.Max(current, 1) * hundred : hundred + current;
                current = 0;
                any = true;
                onlyDigits = false;
                continue;
            }
            var bare = word.Length > 1 && word[0] == 'و' && !Units.ContainsKey(word) ? word[1..] : word;
            var value = Units.TryGetValue(bare, out var known) || Tens.TryGetValue(bare, out known) ? known : SoundAlike(bare, near: true);
            if (value == null) return Lenient(words); // some other word: try the lenient reading
            current += value.Value;
            any = true;
            if (value <= 9 && bare == word && !Ordinal(bare)) digits.Add(value.Value);
            else onlyDigits = false;
        }
        clean = any;
        if (onlyDigits && digits.Count >= 2) return int.Parse(string.Concat(digits));
        return any ? total + current : null;
    }

    private static readonly HashSet<string> Ordinals = new(StringComparer.Ordinal)
    {
        "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "start", "beginning", "begining",
    };

    // A single digit said as a numeral or a cardinal word ("4", "four", "اربعه").
    private static int? Digit(string word) =>
        word.Length == 1 && char.IsAsciiDigit(word[0]) ? word[0] - '0'
        : !Ordinal(word) && Units.TryGetValue(word, out var unit) && unit <= 9 ? unit : null;

    private static bool Ordinal(string word) => Ordinals.Contains(word) || word.StartsWith("ال", StringComparison.Ordinal) || word == "اول";

    // The digit word (zero to nine) in the same script that sounds exactly
    // like this one - "tree" like "three" - when only one digit sounds that
    // way. near: also one sound off, for an answer that is all digits.
    private static int? SoundAlike(string word, bool near = false)
    {
        var key = ApprovedVoiceAppLauncher.SoundKey(word);
        if (key.Length == 0) return null;
        var arabic = TranscriptCleanup.ContainsArabic(word);
        var matches = Units
            .Where(n => n.Value <= 9 && !Ordinal(n.Key) && TranscriptCleanup.ContainsArabic(n.Key) == arabic && ApprovedVoiceAppLauncher.SoundKey(n.Key) == key)
            .Select(n => n.Value).Distinct().ToList();
        // Arabic digits are often one sound off ("سبات" for "سبعة"): taken
        // when only one digit is that close, and only when every word of the
        // answer is a digit - other speech is asked again, not guessed at.
        if (matches.Count == 0 && near && arabic && key.Length >= 3)
            matches = Units
                .Where(n => n.Value <= 9 && !Ordinal(n.Key) && TranscriptCleanup.ContainsArabic(n.Key) &&
                            ApprovedVoiceAppLauncher.SoundKey(n.Key) is { Length: >= 3 } target &&
                            ApprovedVoiceAppLauncher.EditDistance(key, target) <= 1)
                .Select(n => n.Value).Distinct().ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    // Whisper often writes an Arabic digit as a sound-alike ("سلاسة" for
    // "ثلاثة") with a stray syllable after it. Number words and digits that
    // sound exactly like one are taken, other words ignored: one number, or
    // digits read in order ("واحد سلاسة جن" is 13). Keeper says the number
    // back, so a wrong one is heard.
    private static int? Lenient(string[] words)
    {
        var found = new List<int>();
        foreach (var word in words)
        {
            if (Units.TryGetValue(word, out var unit)) { found.Add(unit); continue; }
            if (Tens.TryGetValue(word, out var ten)) { found.Add(ten); continue; }
            if (ApprovedVoiceAppLauncher.SoundKey(word).Length >= 3 && SoundAlike(word) is { } alike) found.Add(alike);
        }
        if (found.Count >= 2 && found.All(n => n <= 9)) return int.Parse(string.Concat(found));
        return found.Count == 1 ? found[0] : null;
    }

    // A surah: its number (1-114) or its name in English or Arabic -
    // "Al-Baqara", "Baqara", "the Cow", "البقرة", "سورة البقرة" - tolerating
    // a small mishearing when only one surah is that close.
    internal static int? Surah(string transcript)
    {
        var spoken = string.Join(' ', Words(Phrase(transcript)));
        if (spoken.Length == 0) return null;
        var heard = NameKey(spoken);

        // A name spelled exactly comes first: "سبأ" sounds like "سبع".
        foreach (var surah in QuranText.Surahs)
            if (Keys(surah).Contains(heard)) return surah.Number;
        // A name with a stray word next to it ("الفاتحة. حدوه."): one word
        // that is exactly one surah's name - before the numbers, which match
        // digits by sound. Not "جن" (Al-Jinn): Whisper often adds it as a
        // stray syllable ("سلاسة جن" is ثلاثة).
        var named = spoken.Split(' ').Select(NameKey).Where(w => w != "جن").Distinct()
            .SelectMany(w => QuranText.Surahs.Where(surah => Keys(surah).Contains(w)).Select(surah => surah.Number)).Distinct().ToList();
        if (named.Count == 1) return named[0];
        if (Number(transcript) is { } number) return number is >= 1 and <= 114 ? number : null;

        // Closest by sound; two surahs that sound equally close ("Fadiha" is
        // as near Al-Fatiha as Al-Fath) are told apart by their letters.
        var best = (Sound: int.MaxValue, Letters: int.MaxValue);
        int? found = null;
        var tied = false;
        var heardSound = ApprovedVoiceAppLauncher.SoundKey(heard);
        if (heardSound.Length < 2) return null;
        foreach (var surah in QuranText.Surahs)
            foreach (var key in Keys(surah))
            {
                var target = ApprovedVoiceAppLauncher.SoundKey(key);
                var allowed = target.Length >= 6 ? 2 : target.Length >= 3 ? 1 : 0;
                var sound = ApprovedVoiceAppLauncher.EditDistance(heardSound, target);
                if (sound > allowed) continue;
                var score = (Sound: sound, Letters: ApprovedVoiceAppLauncher.EditDistance(heard, key));
                if (score.CompareTo(best) > 0) continue;
                if (score.CompareTo(best) == 0) { if (found != surah.Number) tied = true; continue; }
                tied = false;
                best = score;
                found = surah.Number;
            }
        return tied ? null : found;
    }

    // A surah's names as said: its English name with the article dropped
    // and spelling evened out ("al faatiha" → "fatiha"), its Arabic name
    // without "ال", and its meaning ("the cow" → "cow").
    private static IEnumerable<string> Keys(Surah surah)
    {
        yield return NameKey(TranscriptCleanup.MatchingKey(surah.Name));
        yield return NameKey(TranscriptCleanup.MatchingKey(surah.ArabicName));
        yield return NameKey(TranscriptCleanup.MatchingKey(surah.Meaning));
    }

    private static readonly HashSet<string> Articles = new(StringComparer.Ordinal)
    {
        "al", "an", "ar", "as", "at", "ad", "adh", "az", "ash", "aal", "el", "the", "a",
    };

    private static string NameKey(string key)
    {
        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Articles.Contains(w)).ToList();
        var joined = string.Concat(words.Select(w => w.Length > 3 && w.StartsWith("ال", StringComparison.Ordinal) ? w[2..] : w));
        // Tanzil doubles long vowels ("Faatiha", "Imraan"); people don't.
        var even = new System.Text.StringBuilder(joined.Length);
        foreach (var c in joined)
            if (even.Length == 0 || even[^1] != c || !"aeiou".Contains(c)) even.Append(c);
        return even.ToString();
    }

    // The recognizer's hint while a question waits: numbers written as
    // digits steer Whisper to write "14" rather than a sound-alike word.
    internal const string RecognitionPrompt =
        "Surah 2. Al-Fatiha. Al-Baqara. Yasin. From ayah 1, 4. One, seven. 2, 3, 4. 10. 25. 286. All. To the end. No. Go back. Cancel.";
    internal const string ArabicRecognitionPrompt =
        "سورة 2. الفاتحة. البقرة. يس. من الآية واحد، أربعة. 1، 7. 2، 3، 4. 10. 25. 286. كلها. للآخر. لا. ارجع. إلغاء.";

    private static string Phrase(string transcript) =>
        transcript.Length > 160 ? "" : ApprovedVoiceAppLauncher.CommandPhrase(transcript);

    private static string[] Words(string phrase) =>
        phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Filler.Contains(w)).ToArray();
}

using System;
using System.Linq;
using System.Threading.Tasks;

namespace MiniCloClo;

// Everything Mini CloClo says goes through here, so it's all in one language
// and at one speed: Arabic (Hoda, or any Arabic voice) when "Voice commands
// in Arabic" is on and an Arabic voice is installed, otherwise English
// (David, or Windows' default voice). Speaking is never optional in this
// build: if the preferred voice is missing, another one is used rather than
// staying silent.
public static class Voice
{
    private static Func<bool> _arabic = () => false;

    // Silences everything said through here - for tools that open the app's
    // windows to take screenshots (tools\promo-video). The app never sets it.
    public static bool Muted { get; set; }
    // What would have been said while muted (debug tests).
    internal static Action<string>? Said;

    public static void Configure(Func<bool> preferArabic, double rate)
    {
        _arabic = preferArabic;
        SpeechReader.Rate = rate;
    }

    public static bool UsesArabic => _arabic() && SpeechReader.IsVoiceAvailable(ReadAloudVoice.ArabicHoda);

    // The English or Arabic phrase, whichever will be spoken.
    public static string Pick(string english, string? arabic) => arabic != null && UsesArabic ? arabic : english;

    public static void Say(string english, string? arabic = null, Action? onStarted = null)
    {
        if (Muted)
        {
            Said?.Invoke(Pick(english, arabic));
            return;
        }
        var text = Pick(english, arabic);
        Start(Sentences(text), onStarted);
    }

    // ---- Pausing for a reminder (#39) ----
    // What Say is reading: the text in sentences, and the one being spoken.
    // A long text (the news) is read one sentence at a time, so a reminder
    // can pause it and it carries on from the sentence that was cut.
    private sealed class Reading(string[] sentences)
    {
        public readonly string[] Sentences = sentences;
        public int Index;
    }

    private static readonly object ReadingLock = new();
    private static Reading? _reading;
    private const int SentenceSplitLength = 160;

    static Voice()
    {
        SpeechReader.Finished += OnSentenceFinished;
        // Stopped on purpose (the azan starting, "stop", a new command): the
        // rest isn't read.
        SpeechReader.Stopping += Forget;
    }

    internal static string[] Sentences(string text)
    {
        if (text.Length <= SentenceSplitLength) return [text];
        // After . ! ? ؟ followed by a space ("1: ... 2: ..." news items end
        // with their own full stop), and at line breaks - text recognized in
        // a picture often has lines without full stops.
        var parts = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?؟])\s+|\s*\n\s*")
            .Where(p => p.Trim().Length > 0).ToArray();
        return parts.Length == 0 ? [text] : parts;
    }

    private static void Start(string[] sentences, Action? onStarted = null)
    {
        var reading = new Reading(sentences);
        lock (ReadingLock) _reading = reading;
        SpeechReader.Speak(sentences[0], onStarted, selectedVoice: PreferredVoice(sentences[0]));
    }

    // The next sentence, when the one being spoken has finished - unless the
    // reading was replaced, paused or stopped meanwhile.
    private static void OnSentenceFinished()
    {
        string? next;
        lock (ReadingLock)
        {
            if (_reading is not { } reading) return;
            if (++reading.Index >= reading.Sentences.Length)
            {
                _reading = null;
                return;
            }
            next = reading.Sentences[reading.Index];
        }
        SpeechReader.Speak(next, selectedVoice: PreferredVoice(next));
    }

    // Stops what Say is reading and returns what is left (from the sentence
    // being spoken), to be carried on with Resume; null when nothing is.
    public static string[]? Pause()
    {
        string[]? rest = null;
        lock (ReadingLock)
        {
            if (_reading is { } reading && SpeechReader.IsSpeaking && reading.Index < reading.Sentences.Length)
                rest = reading.Sentences[reading.Index..];
            _reading = null;
        }
        SpeechReader.Stop();
        return rest;
    }

    public static void Resume(string[] rest)
    {
        if (Muted || rest.Length == 0) return;
        Start(rest);
    }

    // "Stop": nothing carries on afterwards.
    public static void Forget()
    {
        lock (ReadingLock) _reading = null;
    }

    // Says it and waits until it has been said (at most `limit`).
    public static async Task SayAndWaitAsync(string english, string? arabic = null, TimeSpan? limit = null)
    {
        if (Muted) return;
        lock (ReadingLock) _reading = null;
        var text = Pick(english, arabic);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished() => finished.TrySetResult();
        SpeechReader.Stop();
        SpeechReader.Finished += OnFinished;
        try
        {
            SpeechReader.Speak(text, onFailed: OnFinished, selectedVoice: PreferredVoice(text));
            try { await finished.Task.WaitAsync(limit ?? TimeSpan.FromSeconds(20)); }
            catch (TimeoutException) { SpeechReader.Stop(); }
        }
        finally
        {
            SpeechReader.Finished -= OnFinished;
        }
    }

    // A fixed voice only when it's installed and suits the text; otherwise
    // null, which lets SpeechReader pick a voice per script (Arabic words
    // with an Arabic voice, the rest with the default one).
    private static ReadAloudVoice? PreferredVoice(string text)
    {
        var arabicText = TranscriptCleanup.ContainsArabic(text);
        // Both scripts: each run gets its own voice (an Arabic voice skips
        // Latin words, and an English one Arabic words).
        if (arabicText && System.Linq.Enumerable.Any(text, c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')) return null;
        if (arabicText && SpeechReader.IsVoiceAvailable(ReadAloudVoice.ArabicHoda)) return ReadAloudVoice.ArabicHoda;
        if (!arabicText && SpeechReader.IsVoiceAvailable(ReadAloudVoice.EnglishDavid)) return ReadAloudVoice.EnglishDavid;
        return null;
    }
}

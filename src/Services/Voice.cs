using System;
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
    // windows to take screenshots (tools\PromoVideo). The app never sets it.
    public static bool Muted { get; set; }

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
        if (Muted) return;
        var text = Pick(english, arabic);
        SpeechReader.Speak(text, onStarted, selectedVoice: PreferredVoice(text));
    }

    // Says it and waits until it has been said (at most `limit`).
    public static async Task SayAndWaitAsync(string english, string? arabic = null, TimeSpan? limit = null)
    {
        if (Muted) return;
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

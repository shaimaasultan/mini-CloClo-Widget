using System;
using System.Threading.Tasks;

namespace MiniCloClo;

// Learn your mishearings (#37): when a command isn't understood ("Not a
// command I know") and, within 30 seconds, the next one works, Keeper offers
// to remember the misheard words as another way to say it - naming only the
// command ("Shall I remember what you said before as another way to say the
// time?"), never the misheard words. "Yes" keeps it (LearnedPhrases, text
// only); next time those words do that command. "Forget that" right after
// removes it; the Learned phrases window lists them all.
public partial class MainWindow
{
    private static readonly TimeSpan LearnWithin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LearnAnswerTime = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ForgetWithin = TimeSpan.FromMinutes(2);

    private sealed record LearnOffer(string Failed, string Worked, string Name, string OtherName, DateTime At);

    // What the command being handled did (null: nothing learnable), and
    // whether it wasn't understood at all.
    private (string Name, string OtherName)? _succeeded;
    private bool _commandFailed;
    private (string Heard, DateTime At)? _lastFailure;
    // Waiting for a moment with no question open (a call to confirm...).
    private LearnOffer? _learnCandidate;
    private LearnOffer? _pendingLearn;
    private (LearnedPhrase Phrase, DateTime At)? _lastLearnedUse;

    private void Succeeded(string name, string otherName) => _succeeded = (name, otherName);

    private void SucceededWith(WidgetVoiceCommand command)
    {
        var (name, otherName) = command switch
        {
            WidgetVoiceCommand.Time => ("the time", "الساعة"),
            WidgetVoiceCommand.Date => ("the date", "التاريخ"),
            WidgetVoiceCommand.NextPrayer => ("the next prayer", "الصلاة الجاية"),
            WidgetVoiceCommand.PrayerTimes => ("the prayer times", "مواقيت الصلاة"),
            WidgetVoiceCommand.Quran => ("the Quran", "القرآن"),
            WidgetVoiceCommand.ChooseSurah => ("Quran surah", "القرآن سورة"),
            WidgetVoiceCommand.QuranNext => ("next", "اللي بعده"),
            WidgetVoiceCommand.QuranPrevious => ("previous", "اللي قبله"),
            WidgetVoiceCommand.Reminders => ("reminders", "التذكيرات"),
            WidgetVoiceCommand.Help => ("help", "مساعدة"),
            WidgetVoiceCommand.StopSound => ("stop the sound", "وقف الصوت"),
            WidgetVoiceCommand.Prayed => ("I prayed", "صليت"),
            WidgetVoiceCommand.SayAgain => ("say it again", "قولها تاني"),
            WidgetVoiceCommand.MedicineLog => ("did I take my medicine", "خدت الدوا"),
            WidgetVoiceCommand.ReadSelection => ("read", "اقرا"),
            WidgetVoiceCommand.ReadScreen => ("read the screen", "اقرا الشاشة"),
            _ => (command.ToString(), command.ToString()),
        };
        Succeeded(name, otherName);
    }

    // After each spoken command (not the learned one replayed).
    private void AfterCommand(string heard)
    {
        var now = DateTime.UtcNow;
        if (_commandFailed)
        {
            _lastFailure = (heard, now);
            return;
        }
        if (_succeeded is { } done)
        {
            if (_lastFailure is { } failure && now - failure.At <= LearnWithin && LearnedPhrases.CanLearn(failure.Heard, heard))
                _learnCandidate = new LearnOffer(failure.Heard, heard, done.Name, done.OtherName, now);
            _lastFailure = null;
        }
        _ = OfferToLearnWhenFreeAsync();
    }

    // Asked once the reply has been said and no other question is open (a
    // call or a pull waiting for "yes" asks first; this comes after it).
    private async Task OfferToLearnWhenFreeAsync()
    {
        if (_learnCandidate is not { } offer) return;
        if (DateTime.UtcNow - offer.At > TimeSpan.FromMinutes(2))
        {
            _learnCandidate = null;
            return;
        }
        if (_pendingCall != null || PullPending || QuranDialogActive || _medicine.Waiting ||
            ActiveSearch is { Step: not SearchStep.Played }) return;
        _learnCandidate = null;
        await Task.Delay(400);
        for (var waited = 0; waited < 120 && SpeechReader.IsSpeaking; waited++) await Task.Delay(250);
        if (_closing || _pushToTalk != null) return; // talking again: not asked
        _pendingLearn = offer with { At = DateTime.UtcNow };
        // The label shows what was heard; Keeper says only the command.
        ShowLabel("Remember this?", $"Heard: {offer.Failed} → {offer.Name} — say yes, or no", "🧠", seconds: (int)LearnAnswerTime.TotalSeconds);
        Voice.Say($"Shall I remember what you said before as another way to say {offer.Name}? Say yes, or no.",
            $"أفتكر اللي قلته قبل كده على إنه طريقة تانية تقول {offer.OtherName}؟ قول نعم، أو لا.");
    }

    // "Yes" / "no" to "Shall I remember ...?"; anything else drops the
    // question and is handled as usual.
    private bool TryAnswerLearnQuestion(string heard)
    {
        if (_pendingLearn is not { } offer) return false;
        _pendingLearn = null;
        if (DateTime.UtcNow - offer.At > LearnAnswerTime) return false;
        if (CallVoiceCommand.IsYes(heard))
        {
            LearnedPhrases.Add(offer.Failed, offer.Worked, offer.Name, offer.OtherName, DateTime.Now);
            Keeper.SetVoiceStatus("complete");
            ShowLabel("Learned", $"{offer.Failed} → {offer.Name}", "🧠", seconds: 15);
            Voice.Say($"OK. Next time, that means {offer.Name}.", $"تمام. المرة الجاية، ده معناه {offer.OtherName}.");
            return true;
        }
        if (QuranVoiceAnswers.IsBack(heard) || ApprovedVoiceAppLauncher.IsCancelCommand(heard))
        {
            Keeper.SetVoiceStatus("complete");
            ShowLabel("Not learned", "", "🧠");
            Voice.Say("OK, I won't.", "ماشي، مش هفتكره.");
            return true;
        }
        return false;
    }

    private void UsedLearned(LearnedPhrase learned)
    {
        LearnedPhrases.Used(learned, DateTime.Now);
        _lastLearnedUse = (learned, DateTime.UtcNow);
    }

    // "Forget that" / "انسى ده", right after a learned phrase was used.
    private void ForgetLastLearned()
    {
        Keeper.SetVoiceStatus("complete");
        if (_lastLearnedUse is not { } used || DateTime.UtcNow - used.At > ForgetWithin || !LearnedPhrases.Remove(used.Phrase.Key))
        {
            ShowLabel("Nothing to forget", "Say it right after a learned phrase was used", "🧠");
            Voice.Say("There's nothing to forget. Say forget that right after I've used something I learned.",
                "مفيش حاجة أنساها. قول انسى ده بعد ما أستخدم حاجة اتعلمتها على طول.");
            return;
        }
        _lastLearnedUse = null;
        ShowLabel("Forgotten", $"{used.Phrase.Heard} → {used.Phrase.Name}", "🧠");
        Voice.Say($"OK, forgotten. That won't mean {used.Phrase.Name} any more.", $"ماشي، نسيته. مش هيبقى معناه {used.Phrase.OtherName ?? used.Phrase.Name} تاني.");
    }

    // A spoken command, as if heard (debug test).
    internal async Task HeardForTest(string heard)
    {
        _succeeded = null;
        _commandFailed = false;
        await HandleHeardAsync(heard, null);
        AfterCommand(heard);
    }

    internal void OpenLearnedPhrases()
    {
        if (FocusOpenDialog()) return;
        ShowDialogWindow(new LearnedPhrasesWindow());
    }
}

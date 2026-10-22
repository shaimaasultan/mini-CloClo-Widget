using System;
using System.Threading.Tasks;

namespace MiniCloClo;

// "Choose a surah": three spoken questions, then the Quran window opens on
// the answer and recites it. (1) Which surah? - a number or a name. (2) From
// which ayah? - a number, or "all" for the whole surah, recited from the
// first ayah to the last and again. (3) To which ayah? - a number, or "the
// end". Each answer is its own hold-to-talk (or Ctrl+Alt+V), like confirming
// a call: an unclear answer asks again, "no" goes back one question (to
// change what Keeper read back), "cancel" stops, and after a minute without
// an answer the questions end. Plain "Quran" still just opens the window
// where you left off.
public partial class MainWindow
{
    private enum QuranQuestion { None, Surah, From, To }

    private static readonly TimeSpan QuranAnswerTime = TimeSpan.FromSeconds(60);
    private QuranQuestion _quranQuestion;
    private DateTime _quranQuestionUntil;
    private int _quranDialogSurah;
    private int _quranDialogFrom;

    private bool QuranDialogActive
    {
        get
        {
            if (_quranQuestion != QuranQuestion.None && DateTime.UtcNow > _quranQuestionUntil) _quranQuestion = QuranQuestion.None;
            return _quranQuestion != QuranQuestion.None;
        }
    }

    internal void StartChooseSurah()
    {
        _pendingCall = null;
        _ = AskQuranAsync(QuranQuestion.Surah, null);
    }

    // Asks one of the three questions (after an optional note, such as
    // "Al-Fatiha, 7 ayat.") and waits for its answer.
    private async Task AskQuranAsync(QuranQuestion question, (string English, string Arabic)? before, string? heard = null)
    {
        _quranQuestion = question;
        _quranQuestionUntil = DateTime.UtcNow + QuranAnswerTime;
        var (english, arabic, label) = question switch
        {
            QuranQuestion.Surah => ("Which surah? Say its name, or its number digit by digit.", "أنهي سورة؟ قول اسمها، أو رقمها رقم رقم.", "Which surah?"),
            QuranQuestion.From => ("From which ayah? Say it digit by digit, like one four for 14. Or say all, for the whole surah.",
                "من أنهي آية؟ قولها رقم رقم، بالعربي أو بالإنجليزي، زي واحد أربعة يعني أربعتاشر. أو قول كلها، للسورة كلها.", "From which ayah? (or \"all\")"),
            _ => ("To which ayah? Digit by digit. Or say no, to change the start.",
                "لحد أنهي آية؟ رقم رقم. أو قول لا، عشان تغير البداية.", "To which ayah?"),
        };
        var surahName = _quranDialogSurah > 0 ? QuranText.GetSurah(_quranDialogSurah).Name : "Quran";
        // What was heard is shown, never spoken: only fixed phrases are said.
        var subtitle = heard != null ? $"Heard: {heard}"
            : question == QuranQuestion.Surah ? "Say a name, or a number digit by digit — or cancel"
            : $"{surahName} · digit by digit: \"one four\" is 14 · \"no\" goes back";
        ShowLabel(label, subtitle, "📗", seconds: (int)QuranAnswerTime.TotalSeconds);
        if (before is { } note) (english, arabic) = ($"{note.English} {english}", $"{note.Arabic} {arabic}");
        await SpeakPushToTalkStepAsync(english, arabic);
    }

    // An answer while a question waits: true when it was taken (or asked
    // again); false lets other commands - and "cancel" - through.
    // english: in Arabic mode, the same answer read as English - used only
    // for numbers ("one four"), never for names or other words.
    private async Task<bool> TryHandleQuranAnswerAsync(string heard, string? english = null)
    {
        if (!QuranDialogActive) return false;
        Keeper.SetVoiceStatus("complete");
        switch (_quranQuestion)
        {
            case QuranQuestion.Surah:
                if ((QuranVoiceAnswers.Surah(heard) ?? EnglishSurahNumber(english)) is not { } chosen)
                {
                    Keeper.SetVoiceStatus("failed");
                    await AskQuranAsync(QuranQuestion.Surah, ("I didn't catch the surah.", "ما فهمتش السورة."), heard);
                    return true;
                }
                _quranDialogSurah = chosen;
                var info = QuranText.GetSurah(chosen);
                await AskQuranAsync(QuranQuestion.From,
                    ($"Surah {info.Name}, {info.AyahCount} ayat.", $"سورة {info.ArabicName}، {info.AyahCount} آية."));
                return true;

            case QuranQuestion.From:
            {
                var surah = _quranDialogSurah;
                var count = QuranText.GetSurah(surah).AyahCount;
                if (QuranVoiceAnswers.IsAll(heard))
                {
                    _quranQuestion = QuranQuestion.None;
                    OpenQuranWith(window => window.PlayWholeSurah(surah));
                    return true;
                }
                if (QuranVoiceAnswers.Number(heard, english) is not { } from || from < 1)
                {
                    Keeper.SetVoiceStatus("failed");
                    await AskQuranAsync(QuranQuestion.From, ("I didn't catch the ayah.", "ما فهمتش رقم الآية."), heard);
                    return true;
                }
                if (from > count)
                {
                    Keeper.SetVoiceStatus("failed");
                    await AskQuranAsync(QuranQuestion.From, ($"This surah has only {count} ayat.", $"السورة دي {count} آية بس."));
                    return true;
                }
                _quranDialogFrom = from;
                if (from == count)
                {
                    // The last ayah: there's nothing to ask about the end.
                    _quranQuestion = QuranQuestion.None;
                    OpenQuranWith(window => window.OpenRange(surah, from, from));
                    return true;
                }
                await AskQuranAsync(QuranQuestion.To, ($"From ayah {from}.", $"من الآية {from}."));
                return true;
            }

            default:
            {
                var surah = _quranDialogSurah;
                var count = QuranText.GetSurah(surah).AyahCount;
                var from = _quranDialogFrom;
                int to;
                if (QuranVoiceAnswers.IsEnd(heard)) to = count;
                else if (QuranVoiceAnswers.Number(heard, english) is { } number) to = number;
                else
                {
                    Keeper.SetVoiceStatus("failed");
                    await AskQuranAsync(QuranQuestion.To, ("I didn't catch the ayah.", "ما فهمتش رقم الآية."), heard);
                    return true;
                }
                if (to < from)
                {
                    Keeper.SetVoiceStatus("failed");
                    await AskQuranAsync(QuranQuestion.To, ($"That's before ayah {from}.", $"دي قبل الآية {from}."));
                    return true;
                }
                if (to > count)
                {
                    Keeper.SetVoiceStatus("failed");
                    await AskQuranAsync(QuranQuestion.To, ($"This surah has only {count} ayat.", $"السورة دي {count} آية بس."));
                    return true;
                }
                _quranQuestion = QuranQuestion.None;
                if (to - from + 1 > QuranStore.MaxRange)
                {
                    // Memorization takes at most 20 ayat at a time.
                    to = from + QuranStore.MaxRange - 1;
                    await SpeakPushToTalkStepAsync($"I can open {QuranStore.MaxRange} ayat at a time, so ayat {from} to {to}.",
                        $"أقدر أفتح {QuranStore.MaxRange} آية بس مرة واحدة، فهفتح من {from} لحد {to}.");
                }
                var last = to;
                OpenQuranWith(window => window.OpenRange(surah, from, last));
                return true;
            }
        }
    }

    // "No" (or "back", "ارجع") while the second or third question waits:
    // asks the one before again, so a misheard surah or start ayah can be
    // changed without starting over. At the first question there is
    // nothing to go back to, so "no" cancels as before.
    private async Task<bool> TryGoBackInQuranDialogAsync(string heard)
    {
        if (!QuranDialogActive || _quranQuestion == QuranQuestion.Surah || !QuranVoiceAnswers.IsBack(heard)) return false;
        Keeper.SetVoiceStatus("complete");
        if (_quranQuestion == QuranQuestion.To)
            await AskQuranAsync(QuranQuestion.From, ("Going back.", "نرجع."));
        else
        {
            _quranDialogSurah = 0;
            await AskQuranAsync(QuranQuestion.Surah, ("Going back.", "نرجع."));
        }
        return true;
    }

    // A surah number said in English in Arabic mode ("one eight" is Al-Kahf).
    private static int? EnglishSurahNumber(string? english) =>
        english != null && QuranVoiceAnswers.Number(english) is { } number and >= 1 and <= 114 ? number : null;

    // "Cancel" while a question waits: true when there was one.
    private bool CancelQuranDialog()
    {
        if (!QuranDialogActive) return false;
        _quranQuestion = QuranQuestion.None;
        ShowLabel("Cancelled", "", "📗");
        Voice.Say("Cancelled.", "اتلغى.");
        return true;
    }

    // Opens the Quran window (or uses the open one) and does something in it
    // once it's ready; its own spoken introduction is skipped, since the
    // action says what was opened.
    private void OpenQuranWith(Action<QuranWindow> action)
    {
        HideLabel();
        if (_quranWindow is { IsLoaded: true } open)
        {
            if (open.WindowState == System.Windows.WindowState.Minimized) open.WindowState = System.Windows.WindowState.Normal;
            open.Activate();
            action(open);
            return;
        }
        OpenQuran(skipIntro: true);
        if (_quranWindow is not { } window) return;
        // Show() usually raises Loaded before it returns, so a handler added
        // now would never run and the window would stay where it was left.
        if (window.IsLoaded) action(window);
        else window.Loaded += (_, _) => action(window);
    }
}

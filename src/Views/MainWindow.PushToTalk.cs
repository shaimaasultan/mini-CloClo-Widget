using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MiniCloClo;

// Voice commands. Holding the right mouse button anywhere on screen for a
// second (or pressing Ctrl+Alt+V, the 🎤 icon or V on the widget) opens the
// microphone; releasing it (or pressing again) runs what was said: one of the
// widget's own commands (the time, the date, prayer times, the Quran,
// reminders, stop, "I prayed", help - WidgetVoiceCommands), "call" and a
// saved contact (MainWindow.Calls.cs), an approved app, or "shut down",
// which starts a full shutdown (see StartShutdownAsync).
// Every step is spoken, so it all works without seeing the screen. Shorter
// right-clicks reach the app under the cursor as usual (RightButtonHoldHook).
// The microphone is open only while listening, and audio is never saved.
public partial class MainWindow
{
    private const double PushToTalkHoldSeconds = 1;
    // A command started from the keyboard listens at most this long.
    private const int KeyboardListenSeconds = 7;
    private RightButtonHoldHook? _pushToTalkHook;
    private LocalAudioTranscriber? _pushToTalk;
    // Listening and not yet released.
    private bool _pushToTalkHeld;
    private readonly List<string> _pushToTalkText = [];
    // The English reading of an Arabic answer to a surah question.
    private readonly List<string> _pushToTalkEnglish = [];
    private DispatcherTimer? _keyboardListenTimer;
    // Time to say "cancel" after the shutdown announcement ends.
    private const int ShutdownCountdownSeconds = 10;
    private CancellationTokenSource? _shutdownCountdown;

    // Called at startup and when the setting changes.
    private void UpdatePushToTalkHook()
    {
        if (_settings.PushToTalkEnabled && !_closing)
        {
            if (_pushToTalkHook != null) return;
            try
            {
                _pushToTalkHook = new RightButtonHoldHook(TimeSpan.FromSeconds(PushToTalkHoldSeconds));
                _pushToTalkHook.HoldStarted += OnPushToTalkHoldStarted;
                _pushToTalkHook.HoldEnded += OnPushToTalkHoldEnded;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                ShowLabel("Hold to talk could not start", $"Windows refused the mouse watcher ({ex.NativeErrorCode}).");
                Voice.Say("Hold right-click to talk could not start. Press Control Alt V to talk instead.");
            }
            return;
        }
        AbortPushToTalk();
        _pushToTalkHook?.Dispose();
        _pushToTalkHook = null;
    }

    // From the keyboard, the 🎤 icon or the menu: the first press starts
    // listening, the next one (or KeyboardListenSeconds) ends it.
    internal void ToggleKeyboardVoiceCommand()
    {
        if (_pushToTalkHeld)
        {
            OnPushToTalkHoldEnded();
            return;
        }
        if (_pushToTalk != null) return; // the previous command is still finishing
        _keyboardListenTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(KeyboardListenSeconds) };
        _keyboardListenTimer.Stop();
        _keyboardListenTimer.Tick -= KeyboardListenTimer_Tick;
        _keyboardListenTimer.Tick += KeyboardListenTimer_Tick;
        OnPushToTalkHoldStarted(fromKeyboard: true);
    }

    private void KeyboardListenTimer_Tick(object? sender, EventArgs e)
    {
        _keyboardListenTimer?.Stop();
        OnPushToTalkHoldEnded();
    }

    private void OnPushToTalkHoldEnded()
    {
        _keyboardListenTimer?.Stop();
        if (!_pushToTalkHeld) return;
        _pushToTalkHeld = false;
        if (_pushToTalk is { } session) _ = FinishPushToTalkAsync(session);
    }

    private void OnPushToTalkHoldStarted() => OnPushToTalkHoldStarted(fromKeyboard: false);

    private async void OnPushToTalkHoldStarted(bool fromKeyboard)
    {
        if (_pushToTalk != null) return; // the previous command is still finishing
        if (_closing) return;
        _pushToTalkHeld = true;
        // Talking to CloClo is always on purpose, so it ends a recitation,
        // the azan or speech in progress rather than waiting for it (the
        // microphone would hear it otherwise). "Quran" starts the recitation
        // again; an answer to a medicine question carries it on by itself
        // (#29), from the ayah it was on.
        _recitationBeforeTalk = _quranWindow is { IsReciting: true } reciting && reciting.PauseRecitation() is { } ayah
            ? (reciting, ayah) : null;
        if (CloCloSoundPlaying || _voicePausedForAudio || SpeechReader.IsSpeaking) StopSounds();

        var arabic = _settings.VoiceCommandsInArabic;
        // While a choose-a-surah question waits, the hint is surah names and
        // numbers, so an ayah number comes out as digits - and in Arabic the
        // phrase is also read as English, for a number said in English.
        // The words to search for (#38): no command hints, and in whichever
        // language they are said.
        var searchWords = SearchWantsWords;
        var session = new LocalAudioTranscriber(AudioCaptureSource.Microphone,
            recognitionPrompt: searchWords ? "" : ActiveSearch is { Step: SearchStep.App } ? (arabic ? SearchVoiceCommand.ArabicAppPrompt : SearchVoiceCommand.AppPrompt) : QuranDialogActive
                ? (arabic ? QuranVoiceAnswers.ArabicRecognitionPrompt : QuranVoiceAnswers.RecognitionPrompt)
                : (arabic
                    ? ApprovedVoiceAppLauncher.ArabicRecognitionPrompt
                    : ApprovedVoiceAppLauncher.RecognitionPrompt) + CallVoiceCommand.RecognitionHint(_settings.Contacts, arabic),
            englishPrompt: QuranDialogActive && arabic && !searchWords ? QuranVoiceAnswers.RecognitionPrompt : null);
        _pushToTalk = session;
        lock (_pushToTalkText)
        {
            _pushToTalkText.Clear();
            _pushToTalkEnglish.Clear();
        }
        session.TextRecognized += text =>
        {
            lock (_pushToTalkText) _pushToTalkText.Add(text);
        };
        session.AlternateTextRecognized += text =>
        {
            lock (_pushToTalkText) _pushToTalkEnglish.Add(text);
        };
        session.Failed += message =>
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() =>
            {
                // After release, FinishPushToTalkAsync owns the session.
                if (_pushToTalk != session || !_pushToTalkHeld) return;
                AbortPushToTalk();
                Keeper.SetVoiceStatus("failed");
                ShowLabel("Listening stopped", message);
                Voice.Say("Listening stopped. Check the microphone.", "الاستماع وقف. اتأكد من الميكروفون.");
            }));
        };
        Keeper.SetVoiceStatus("processing");
        try
        {
            if (!System.Runtime.Intrinsics.X86.Avx2.IsSupported || !System.Runtime.Intrinsics.X86.Fma.IsSupported)
                throw new NotSupportedException("Voice commands need a processor with AVX2 and FMA.");
            await session.StartAsync(searchWords ? "auto" : arabic ? "ar-EG" : "en-US");
            if (_pushToTalk == session && _pushToTalkHeld)
            {
                ShowLabel("Listening", fromKeyboard
                    ? "Say a command, like \"what time is it\". Press again when done."
                    : "Say a command, like \"what time is it\", then release.", "🎤");
                // A short chime is the cue to speak (the blue badge can't be
                // seen by everyone). The microphone hears silence while it
                // plays, so the chime can't become part of the command.
                session.PauseVoiceRecognition();
                await Earcon.ListeningAsync();
                if (_pushToTalk == session && _pushToTalkHeld)
                {
                    session.ResumeVoiceRecognition();
                    Keeper.SetVoiceStatus("armed");
                    if (fromKeyboard) _keyboardListenTimer?.Start();
                }
            }
        }
        catch (Exception ex)
        {
            if (_pushToTalk != session || !_pushToTalkHeld) return; // released or aborted while starting
            AbortPushToTalk();
            Keeper.SetVoiceStatus("failed");
            ShowLabel("Voice commands could not start", ex is NotSupportedException ? ex.Message : ex.GetType().Name);
            await SpeakPushToTalkStepAsync(
                ex is NotSupportedException ? "Voice commands need a newer processor. The keyboard shortcuts still work." : "Voice commands could not start. Check the microphone.",
                "الأوامر الصوتية مش شغالة. اتأكد من الميكروفون.");
        }
    }

    private async Task FinishPushToTalkAsync(LocalAudioTranscriber session)
    {
        Keeper.SetVoiceStatus("processing");
        try
        {
            // The last syllable is often still in the audio buffer at release.
            await Task.Delay(250);
            // A graceful stop decodes the phrase still queued.
            await session.DisposeAsync();
        }
        catch { /* No text: reported below. */ }
        if (_pushToTalk != session) return; // aborted meanwhile
        _pushToTalk = null;
        string heard;
        string? english;
        lock (_pushToTalkText)
        {
            heard = string.Join(" ", _pushToTalkText);
            english = _pushToTalkEnglish.Count > 0 ? string.Join(" ", _pushToTalkEnglish) : null;
            _pushToTalkText.Clear();
            _pushToTalkEnglish.Clear();
        }
        // Nothing heard in Arabic but a number in English: answer with that.
        if (heard.Length == 0 && english != null && QuranDialogActive && QuranVoiceAnswers.Number(english) != null) heard = english;
        Keeper.SetVoiceStatus("off");
        if (_closing) return;

        if (heard.Length == 0)
        {
            Keeper.SetVoiceStatus("failed");
            ShowLabel("Didn't hear a command", "Try again, a little louder or closer to the microphone.", "🎤");
            await SpeakPushToTalkStepAsync("I didn't hear a command.", "ما سمعتش أمر.");
            return;
        }
        _succeeded = null;
        _commandFailed = false;
        await HandleHeardAsync(heard, english);
        AfterCommand(heard);
    }

    // What was heard, as a command or an answer. A learned mishearing (#37)
    // is handled once more as what it stands for ("replayed").
    private async Task HandleHeardAsync(string heard, string? english, bool replayed = false)
    {
        // "Yes" / "no" to "Shall I remember ...?" (#37).
        if (!replayed && TryAnswerLearnQuestion(heard)) return;
        // "Yes" to a "call Mom?" question.
        if (!_shutdownAwaitingClose && TryConfirmPendingCall(heard)) return;
        // "Yes" to a "pull dates?" question.
        if (!_shutdownAwaitingClose && TryConfirmPendingPull(heard)) return;
        // An answer during a search (#38): the app, the words, "yes", a number.
        if (!_shutdownAwaitingClose && _pendingCall == null && await TryHandleSearchAnswerAsync(heard)) return;
        // "No" / "back" to what Keeper read back during the surah questions -
        // before "no" is taken as cancel and "back" as "previous ayah".
        if (!_shutdownAwaitingClose && _pendingCall == null && await TryGoBackInQuranDialogAsync(heard)) return;
        // "Yes" / "later" to "Did you take your medicine?" (#29).
        if (!_shutdownAwaitingClose && _pendingCall == null && TryAnswerMedicine(heard)) return;
        if (ApprovedVoiceAppLauncher.IsCancelCommand(heard))
        {
            if (await CancelShutdownByUserAsync()) return;
            if (CancelPendingCall() || CancelPendingPull() || CancelQuranDialog() || CancelMedicine())
            {
                Keeper.SetVoiceStatus("complete");
                return;
            }
            // Nothing to cancel: "stop" ends whatever is playing.
            Keeper.SetVoiceStatus(StopSounds() ? "complete" : "failed");
            return;
        }
        if (_shutdownAwaitingClose && _offerForceClose && ApprovedVoiceAppLauncher.IsForceCloseCommand(heard))
        {
            await CloseAppsThenShutDownAsync(force: true);
            return;
        }
        if (_shutdownAwaitingClose && (ApprovedVoiceAppLauncher.IsCloseAppsCommand(heard) ||
                                       ApprovedVoiceAppLauncher.IsForceCloseCommand(heard)))
        {
            await CloseAppsThenShutDownAsync(force: false);
            return;
        }
        // An answer to "which surah?" / "from which ayah?" / "to which ayah?".
        if (await TryHandleQuranAnswerAsync(heard, english)) return;
        // A number after the news headlines: that story's details.
        if (TryReadPulledItem(heard)) return;
        // "Call Mom": read back, then dialed only after "yes".
        if (await TryHandleCallCommandAsync(heard)) return;
        if (ApprovedVoiceAppLauncher.IsShutdownCommand(heard))
        {
            await StartShutdownAsync();
            return;
        }
        // "Forget that": the learned phrase just used is forgotten (#37).
        if (LearnedPhrases.IsForget(heard))
        {
            ForgetLastLearned();
            return;
        }
        // The person's own questions first: their words can't be built-in
        // commands (saving refuses them), so nothing is taken from those.
        if (TryAnswerTopic(heard, loose: false)) return;
        // "Pull dates": runs a pull tool, after "yes".
        if (TryPullCommand(heard)) return;
        // "Search" / "search YouTube": asks what for, then opens the app.
        if (TrySearchCommand(heard)) return;
        // "Pause the song", "next song": what Spotify or a video plays now.
        if (await TrySearchControlAsync(heard)) return;
        if (WidgetVoiceCommands.TryMatch(heard, out var command))
        {
            // "A surah" may be "Ashura": an own word that sounds the same wins.
            if (command == WidgetVoiceCommand.ChooseSurah && TryAnswerTopic(heard, loose: true, sameSound: true)) return;
            Keeper.SetVoiceStatus("complete");
            SucceededWith(command);
            RunWidgetCommand(command, heard);
            return;
        }
        if (!ApprovedVoiceAppLauncher.TryMatchHeldCommand(heard, out var app))
        {
            // A call waiting for "yes" and an answer that wasn't understood:
            // ask again rather than treat it as some other command.
            if (await AskAgainForPendingCallAsync(heard)) return;
            if (AskAgainForPendingPull(heard)) return;
            // A mishearing learned before: what it stands for (#37) - after
            // every exact phrase, before the sound-alike guesses.
            if (!replayed && LearnedPhrases.Find(heard) is { } learned)
            {
                UsedLearned(learned);
                await HandleHeardAsync(learned.Replay, null, replayed: true);
                return;
            }
            // An action word one or two sounds off ("Ramadhan").
            if (TryAnswerTopic(heard, loose: true)) return;
            if (WidgetVoiceCommands.TryMatchLoosely(heard, out var looseCommand))
            {
                Keeper.SetVoiceStatus("complete");
                SucceededWith(looseCommand);
                RunWidgetCommand(looseCommand, heard);
                return;
            }
            Keeper.SetVoiceStatus("failed");
            _commandFailed = true;
            ShowLabel("Not a command I know", $"Heard: {heard}", "🎤");
            await SpeakPushToTalkStepAsync("Sorry, that is not a command I know. Say help to hear the commands.",
                "آسف، ده مش أمر أعرفه. قول مساعدة عشان تسمع الأوامر.");
            return;
        }
        var name = ApprovedVoiceAppLauncher.DisplayName(app);
        var arabicName = ApprovedVoiceAppLauncher.ArabicDisplayName(app);
        Keeper.SetVoiceStatus("processing");
        ShowLabel($"Opening {name}", "", "🎤");
        await SpeakPushToTalkStepAsync($"Opening {name} now.", $"بفتح {arabicName} دلوقتي.");
        if (_closing) return;
        var error = ApprovedVoiceAppLauncher.Launch(app);
        Keeper.SetVoiceStatus(error == null ? "complete" : "failed");
        if (error == null) Succeeded($"open {name}", $"افتح {arabicName}");
        ShowLabel(error ?? $"Opened {name}", "", "🎤");
        if (error != null)
            await SpeakPushToTalkStepAsync($"Could not open {name}.", $"تعذر فتح {arabicName}.");
    }

    private void RunWidgetCommand(WidgetVoiceCommand command, string heard = "")
    {
        switch (command)
        {
            case WidgetVoiceCommand.Time: SayTime(); break;
            case WidgetVoiceCommand.Date: SayDate(); break;
            case WidgetVoiceCommand.NextPrayer: SayNextPrayer(); break;
            case WidgetVoiceCommand.PrayerTimes: SayPrayerTimes(); break;
            case WidgetVoiceCommand.Quran or WidgetVoiceCommand.QuranNext or WidgetVoiceCommand.QuranPrevious:
                QuranVoiceCommand(command);
                break;
            case WidgetVoiceCommand.ChooseSurah: StartChooseSurah(); break;
            case WidgetVoiceCommand.Reminders: OpenCustomReminders(); break;
            case WidgetVoiceCommand.Help: SayHelp(); break;
            case WidgetVoiceCommand.StopSound:
                if (!StopSounds()) Voice.Say("Nothing is playing.", "مفيش حاجة شغالة.");
                break;
            case WidgetVoiceCommand.Prayed: MarkPrayed(); break;
            case WidgetVoiceCommand.SayAgain: SayLastAnswerAgain(); break;
            case WidgetVoiceCommand.MedicineLog: SayMedicineLog(heard); break;
            case WidgetVoiceCommand.ReadSelection: ReadSelection(); break;
            case WidgetVoiceCommand.ReadScreen: ReadScreen(); break;
        }
    }

    // "Shut down". With apps open, CloClo names them and waits for "close"
    // (or "cancel"), asks each to close as its X button does, and shuts down
    // once none is left. An app still open after that, usually asking to
    // save, is named again: "close" tries again after saving, and "force
    // close" ends it without saving. With no apps open, a spoken countdown
    // leaves time to say "cancel". Silence for ShutdownAnswerSeconds,
    // turning hold to talk off or closing CloClo cancels it. The shutdown
    // itself is a full one (FullShutdown). ShutdownConfirmWindow shows each
    // step too, and its buttons answer the same way.
    private const int ShutdownAnswerSeconds = 60;
    private const int AppsCloseWaitSeconds = 10;
    private List<OpenApps.App> _appsToClose = [];
    private bool _shutdownAwaitingClose;
    private bool _offerForceClose;
    private int _shutdownQuestion;
    private ShutdownConfirmWindow? _shutdownWindow;
    // "Shut down now" clicked during the no-apps countdown.
    private bool _shutdownSkipWait;

    private void ShowShutdownStep(string heading, string detail, IEnumerable<string> apps,
        string? confirmText, bool offerForceClose = false)
    {
        if (_shutdownWindow != null)
        {
            _shutdownWindow.ShowStep(heading, detail, apps, confirmText, offerForceClose);
            return;
        }
        var window = new ShutdownConfirmWindow();
        window.ShowStep(heading, detail, apps, confirmText, offerForceClose);
        window.ConfirmRequested += () =>
        {
            if (_shutdownAwaitingClose) _ = CloseAppsThenShutDownAsync(force: false);
            else if (_shutdownCountdown != null) _shutdownSkipWait = true;
        };
        window.ForceCloseRequested += () =>
        {
            if (_shutdownAwaitingClose && _offerForceClose) _ = CloseAppsThenShutDownAsync(force: true);
        };
        window.CancelRequested += () =>
        {
            _shutdownWindow = null;
            _ = CancelShutdownByUserAsync();
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_shutdownWindow, window)) _shutdownWindow = null;
        };
        _shutdownWindow = window;
        window.Show();
        window.Activate();
    }

    private void CloseShutdownWindow()
    {
        var window = _shutdownWindow;
        _shutdownWindow = null;
        window?.CloseFromCode();
    }

    // "Cancel" said, clicked, or the window closed. False when no shutdown
    // was in progress.
    private async Task<bool> CancelShutdownByUserAsync()
    {
        if (!CancelShutdownCountdown()) return false;
        Keeper.SetVoiceStatus("complete");
        ShowLabel("Shutdown cancelled", "");
        await SpeakPushToTalkStepAsync("Shutdown cancelled.", "تم إلغاء إيقاف التشغيل.");
        return true;
    }

    private async Task StartShutdownAsync()
    {
        if (_shutdownCountdown != null)
        {
            ShowLabel("Already shutting down", "Say cancel to stop.");
            await SpeakPushToTalkStepAsync("Already shutting down. Say cancel to stop.", "الجهاز بيقفل. قول إلغاء عشان توقفه.");
            return;
        }
        var apps = OpenApps.List();
        if (apps.Count == 0)
        {
            await CountDownToShutdownAsync();
            return;
        }
        _shutdownCountdown = new CancellationTokenSource();
        await AskToCloseAppsAsync(apps, stillOpen: false);
    }

    private async Task AskToCloseAppsAsync(List<OpenApps.App> apps, bool stillOpen)
    {
        if (_shutdownCountdown is not { } shutdown) return;
        _appsToClose = apps;
        _shutdownAwaitingClose = true;
        _offerForceClose = stillOpen;
        var question = ++_shutdownQuestion;
        _ = CancelShutdownIfUnansweredAsync(question, shutdown.Token);

        var appNames = apps.Select(a => a.Name).Distinct().ToList();
        var names = string.Join(", ", appNames);
        Keeper.SetVoiceStatus("processing");
        if (!stillOpen)
        {
            var count = apps.Count == 1 ? "1 app is open" : $"{apps.Count} apps are open";
            ShowShutdownStep($"Before shutting down: {count}",
                "Close these apps and shut down? Apps with unsaved work will ask you to save. " +
                "You can also talk to CloClo and say close, or cancel.",
                appNames, "Close apps and shut down");
            ShowLabel($"Before shutting down: {count}", $"{names}. Say close, or cancel.");
            await SpeakPushToTalkStepAsync(
                $"{count}: {names}. To close them and shut down, talk to me again and say close. Or say cancel.",
                $"فيه {apps.Count} برامج مفتوحة: {names}. عشان أقفلهم وأقفل الجهاز، كلمني تاني وقول اقفلهم. أو قول إلغاء.");
        }
        else
        {
            ShowShutdownStep("Still open",
                "It may be asking you to save. Save, then choose Try again. Force close closes it without saving.",
                appNames, "Try again", offerForceClose: true);
            ShowLabel("Still open", $"{names}. Save, then say close; or say force close, or cancel.");
            await SpeakPushToTalkStepAsync(
                $"Still open: {names}. It may be asking you to save. Say close to try again, force close to close without saving, or cancel.",
                $"لسه مفتوح: {names}. ممكن يكون عايز يحفظ. قول اقفلهم تاني، أو اقفلهم غصب من غير حفظ، أو إلغاء.");
        }
    }

    private async Task CancelShutdownIfUnansweredAsync(int question, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(ShutdownAnswerSeconds), token);
            while (_pushToTalk != null || _pushToTalkHeld) await Task.Delay(200, token);
        }
        catch (OperationCanceledException) { return; }
        if (question != _shutdownQuestion || !_shutdownAwaitingClose) return;
        CancelShutdownCountdown();
        Keeper.SetVoiceStatus("failed");
        ShowLabel("Shutdown cancelled", "No answer.");
        await SpeakPushToTalkStepAsync("No answer. Shutdown cancelled.", "مفيش رد. تم إلغاء إيقاف التشغيل.");
    }

    private async Task CloseAppsThenShutDownAsync(bool force)
    {
        if (_shutdownCountdown is not { } shutdown) return;
        _shutdownAwaitingClose = false;
        _shutdownQuestion++;
        var apps = _appsToClose;
        Keeper.SetVoiceStatus("processing");
        ShowShutdownStep(force ? "Closing without saving" : "Closing apps",
            "CloClo shuts down when they have closed.", apps.Select(a => a.Name).Distinct(), null);
        ShowLabel(force ? "Closing without saving" : "Closing apps", string.Join(", ", apps.Select(a => a.Name).Distinct()));
        await SpeakPushToTalkStepAsync(force ? "Closing them without saving." : "Closing them.",
            force ? "بقفلهم من غير حفظ." : "بقفلهم.");
        foreach (var app in apps)
        {
            if (force) OpenApps.ForceClose(app);
            else OpenApps.Close(app);
        }
        try
        {
            var until = DateTime.UtcNow.AddSeconds(AppsCloseWaitSeconds);
            while (DateTime.UtcNow < until && apps.Any(OpenApps.IsStillOpen)) await Task.Delay(500, shutdown.Token);
        }
        catch (OperationCanceledException) { return; }
        if (_closing || shutdown.IsCancellationRequested) return;

        // Listed again, so an app opened meanwhile is asked about too. After
        // "force close", what's left (an app running as administrator) gets
        // Windows' own shutdown notice instead.
        var remaining = OpenApps.List();
        if (remaining.Count > 0 && !force)
        {
            await AskToCloseAppsAsync(remaining, stillOpen: true);
            return;
        }
        ShowShutdownStep("Shutting down", "All apps are closed.", [], null);
        await SpeakPushToTalkStepAsync("All apps are closed. Shutting down now.", "قفلت كل البرامج. بقفل الجهاز دلوقتي.");
        if (shutdown.IsCancellationRequested || _closing) return;
        var error = FullShutdown.Start();
        CancelShutdownCountdown();
        if (error == null) return;
        Keeper.SetVoiceStatus("failed");
        ShowLabel("Could not shut down", error);
        await SpeakPushToTalkStepAsync("Could not shut down.", "معرفتش أقفل الجهاز.");
    }

    // No apps open: announce, wait ShutdownCountdownSeconds (longer while a
    // command is still being handled, so a "cancel" being said isn't cut
    // off), then shut down.
    private async Task CountDownToShutdownAsync()
    {
        using var countdown = new CancellationTokenSource();
        _shutdownCountdown = countdown;
        try
        {
            Keeper.SetVoiceStatus("processing");
            _shutdownSkipWait = false;
            ShowShutdownStep($"Shutting down in {ShutdownCountdownSeconds} seconds",
                "No apps are open. You can also talk to CloClo and say cancel.", [], "Shut down now");
            ShowLabel($"Shutting down in {ShutdownCountdownSeconds} seconds", "Say cancel to stop.");
            await SpeakPushToTalkStepAsync(
                $"No apps are open. Shutting down in {ShutdownCountdownSeconds} seconds. To cancel, talk to me and say cancel.",
                $"مفيش برامج مفتوحة. الجهاز هيقفل خلال {ShutdownCountdownSeconds} ثواني. عشان تلغي، كلمني وقول إلغاء.");
            var until = DateTime.UtcNow.AddSeconds(ShutdownCountdownSeconds);
            while (!_shutdownSkipWait && (DateTime.UtcNow < until || _pushToTalk != null || _pushToTalkHeld))
                await Task.Delay(200, countdown.Token);
            if (_closing) return;
            ShowShutdownStep("Shutting down", "", [], null);
            await SpeakPushToTalkStepAsync("Shutting down now.", "بقفل الجهاز دلوقتي.");
            countdown.Token.ThrowIfCancellationRequested();
            var error = FullShutdown.Start();
            CloseShutdownWindow();
            if (error == null) return;
            Keeper.SetVoiceStatus("failed");
            ShowLabel("Could not shut down", error);
            await SpeakPushToTalkStepAsync("Could not shut down.", "معرفتش أقفل الجهاز.");
        }
        catch (OperationCanceledException) { /* "cancel" was said */ }
        finally
        {
            if (ReferenceEquals(_shutdownCountdown, countdown)) _shutdownCountdown = null;
        }
    }

    // Ends whatever stage a voice shutdown is in. False when none was.
    private bool CancelShutdownCountdown()
    {
        _shutdownAwaitingClose = false;
        _appsToClose = [];
        _shutdownQuestion++;
        CloseShutdownWindow();
        if (_shutdownCountdown == null) return false;
        _shutdownCountdown.Cancel();
        _shutdownCountdown = null;
        return true;
    }

    // Each step is also spoken, for users who can't see the label. Only
    // fixed phrases and fixed app names are said, never the transcript.
    private async Task SpeakPushToTalkStepAsync(string english, string arabic)
    {
        if (_closing || _azanPlayer?.IsPlaying == true) return; // don't talk over the azan
        try { await Voice.SayAndWaitAsync(english, arabic, TimeSpan.FromSeconds(12)); }
        catch { /* The label shows the same. */ }
    }

    private void AbortPushToTalk()
    {
        _keyboardListenTimer?.Stop();
        var held = _pushToTalkHeld;
        _pushToTalkHeld = false;
        var session = _pushToTalk;
        _pushToTalk = null;
        if (session == null) return;
        // A released session is already stopping in FinishPushToTalkAsync,
        // which drops its result once it sees it was aborted.
        if (held) _ = session.AbortAsync().AsTask();
        Keeper.SetVoiceStatus("off");
    }
}

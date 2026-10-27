#if DEBUG
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiniCloClo;

// Debug builds only: "MiniCloClo.exe --snapshot <folder>" draws the widget in
// each of its looks to PNG files and exits, so the artwork can be checked
// without running the widget (no microphone, no speech, no settings).
internal static class Snapshot
{
    public static int Run(string folder)
    {
        var app = new System.Windows.Application();
        Directory.CreateDirectory(folder);
        void Save(string name, Action<KeeperView> setup)
        {
            var view = new KeeperView();
            view.SetSize(1.5);
            view.SetMotionMode("still");
            setup(view);
            var host = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2b, 0x4a, 0x6f)),
                Child = view,
            };
            host.Measure(new Size(view.Width, view.Height));
            host.Arrange(new Rect(0, 0, view.Width, view.Height));
            host.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.Width), (int)Math.Ceiling(view.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, name + ".png"));
            encoder.Save(file);
        }
        Save("awake", v => v.SetNextPrayer("Next: Asr 3:12 PM", null));
        Save("lady", v => { v.SetAvatar("lady"); v.SetNextPrayer("Next: Maghrib 5:41 PM", null); v.SetNight(true); });
        Save("prayer", v => { v.SetActivity("prayer"); v.SetNowPlaying("Dhuhr prayer time", "Cairo", "🕌"); });
        Save("prayer-lady", v => { v.SetAvatar("lady"); v.SetActivity("prayer"); });
        Save("asleep", v => { v.SetActivity("asleep"); v.SetNight(true); });
        Save("reminder", v => { v.SetCustomReminderActive(true); v.SetNowPlaying("Drink a glass of water.", "Reminder", "⏰"); });
        Save("quran", v => { v.SetReading(true); v.SetNowPlaying("Quran · Al-Baqara 2:255", "اللَّهُ لَا إِلَٰهَ إِلَّا هُوَ", "📗"); });
        Save("listening", v => { v.SetVoiceStatus("armed"); v.SetNowPlaying("Listening", "Say a command, like \"what time is it\"", "🎤"); });
        Save("arabic", v => { v.SetArabicDates(true); v.SetNextPrayer("Next: Isha 7:02 PM", null); });
        app.Shutdown();
        return 0;
    }

    // "MiniCloClo.exe --load-trace <report file>": runs each part of the app
    // in turn and records which step loads a networking library - the .NET
    // ones with the call stack that loaded them.
    public static int LoadTrace(string reportPath)
    {
        var lines = new System.Collections.Generic.List<string>();
        static bool IsNet(string name) => name.Contains("System.Net", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("winhttp", StringComparison.OrdinalIgnoreCase) || name.StartsWith("msquic", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("wininet", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ws2_32", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("webio", StringComparison.OrdinalIgnoreCase);
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
        {
            var name = e.LoadedAssembly.GetName().Name ?? "";
            if (!IsNet(name)) return;
            lines.Add($"  .NET loaded {name}, from:");
            foreach (var frame in new System.Diagnostics.StackTrace().ToString().Split('\n').Skip(1).Take(14))
                lines.Add("      " + frame.Trim());
        };
        var known = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Step(string name, Action run)
        {
            lines.Add($"== {name}");
            try { run(); } catch (Exception ex) { lines.Add($"  ({ex.GetType().Name}: {ex.Message})"); }
            System.Threading.Thread.Sleep(300);
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            foreach (System.Diagnostics.ProcessModule module in self.Modules)
                if (known.Add(module.ModuleName) && IsNet(module.ModuleName)) lines.Add($"  native/module now loaded: {module.ModuleName}");
        }
        void Pump(int ms)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        Step("start (before WPF)", () => { });
        System.Windows.Application? app = null;
        Step("WPF application", () => app = new System.Windows.Application());
        Step("a WPF window with the avatar", () =>
        {
            var window = new Window { Content = new KeeperView(), Width = 200, Height = 300, ShowInTaskbar = false, Left = -2000 };
            window.Show();
            Pump(500);
            window.Close();
        });
        Step("WinRT speech: list the voices", () => _ = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.Count);
        Step("speak a word", () =>
        {
            SpeechReader.Speak("Test.");
            Pump(2500);
        });
        Step("play the azan for a second", () =>
        {
            using var player = new LocalAudioPlayer();
            player.TryPlay(AzanLibrary.Default);
            Pump(1000);
            player.Stop();
        });
        Step("a file dialog (Choose azan audio)", () => _ = new Microsoft.Win32.OpenFileDialog());
        Step("Whisper: load the model", () =>
            LocalAudioTranscriber.TranscribeCommandForTestAsync(new float[16000], arabic: false).GetAwaiter().GetResult());
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        app?.Shutdown();
        return 0;
    }

    // Sample topics for the questions-and-answers checks: the ready-made
    // Islamic dates, plus a calendar and a text answer.
    private static System.Collections.Generic.List<TopicSettings> SampleTopics(DateTime today)
    {
        var topics = TopicAction.IslamicDates();
        topics.Add(new TopicSettings
        {
            Name = "Vacations", OtherName = "الإجازات",
            Actions =
            [
                new() { Word = "Civic", OtherWord = "الإجازة المدنية", Kind = TopicAction.CalendarKind,
                        Dates = [today.AddDays(-3).ToString("yyyy-MM-dd"), today.AddDays(12).ToString("yyyy-MM-dd"), today.AddYears(1).ToString("yyyy-MM-dd")] },
                new() { Word = "Holidays", Kind = TopicAction.CalendarKind, Dates = [today.ToString("yyyy-MM-dd")] },
                new() { Word = "Long weekend", Kind = TopicAction.CalendarKind, Dates = [today.AddDays(1).ToString("yyyy-MM-dd")] },
                new() { Word = "Old trip", Kind = TopicAction.CalendarKind, Dates = ["2020-01-01"] },
                new() { Word = "Canada Day", Kind = TopicAction.CalendarKind, Dates = [today.AddDays(30).ToString("yyyy-MM-dd")] },
                new() { Word = "Labour Day", Kind = TopicAction.CalendarKind, Dates = [today.AddDays(40).ToString("yyyy-MM-dd")] },
                new() { Word = "Family Day", Kind = TopicAction.CalendarKind, Dates = [today.AddDays(50).ToString("yyyy-MM-dd")] },
            ],
        });
        topics.Add(new TopicSettings
        {
            Name = "Doctor", OtherName = "الدكتور",
            Actions = [new() { Word = "Clinic", OtherWord = "العيادة", Text = "Dr. Sami's clinic is open Sunday to Thursday, 9 to 5.", OtherText = "عيادة دكتور سامي فاتحة من الحد للخميس، من ٩ لـ ٥." }],
        });
        return topics.Select(t => t.ValidateAndNormalize()!).ToList();
    }

    // "MiniCloClo.exe --medicine-test <report file>": medicine reminders
    // (#29) on a fake clock - asked until "yes", "later" snoozes, unanswered
    // is missed - and the log answers, in a throwaway folder.
    // "MiniCloClo.exe --selection-test <report file> [copy]": reads the text
    // selected in the active app (UI Automation, or with "copy" by copying
    // it alone) and checks the clipboard is put back.
    public static int SelectionTest(string reportPath, bool copyOnly)
    {
        var lines = new System.Collections.Generic.List<string>();
        SelectedText.Trace = note => lines.Add($"  {note}");
        SelectedText.SkipAutomation = copyOnly;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        dispatcher.InvokeAsync(async () =>
        {
            try
            {
                System.Windows.Clipboard.SetText("clipboard before the test");
                var window = SelectedText.TargetWindow(out var active);
                lines.Add($"window found: {window != IntPtr.Zero}, active: {active}");
                var text = await SelectedText.ReadAsync(window, active);
                lines.Add($"text: [{text ?? "-"}]");
                lines.Add($"clipboard after: [{(System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "-")}]");
            }
            catch (Exception ex)
            {
                lines.Add($"{ex.GetType().Name}: {ex.Message}");
            }
            File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
            dispatcher.InvokeShutdown();
        });
        System.Windows.Threading.Dispatcher.Run();
        return 0;
    }

    public static int SearchTest(string reportPath)
    {
        var lines = new System.Collections.Generic.List<string> { "-- search commands --" };
        foreach (var heard in new[]
                 {
                     "Search.", "Search YouTube.", "Search on Spotify.", "Search YouTube for Amr Diab.", "Search for Amr Diab Baba.", "Search us put it for you.", "Search Spotifai.", "Search something nice.",
                     "ابحث", "ابحث في يوتيوب", "ابحث في سبوتيفاي عن عمرو دياب", "دورلي علي اليوتيوب", "Find me a song.", "Look up YouTube.",
                     "What time is it?", "Pull news.", "Quran surah.",
                 })
        {
            var r = SearchVoiceCommand.TryParse(heard);
            lines.Add($"\"{heard}\" -> {(r == null ? "-" : $"app {r.App?.ToString() ?? "ask"}, words {r.Words ?? "ask"}")}, command {(WidgetVoiceCommands.TryMatch(heard, out var c) ? c.ToString() : "-")}");
        }
        lines.Add("-- which app? --");
        foreach (var heard in new[] { "YouTube.", "You Tube", "Spotify.", "Spotifai.", "On Spotify.", "يوتيوب", "سبوتيفاي", "سبوتفاي", "Netflix.", "Yes." })
            lines.Add($"\"{heard}\" -> {SearchVoiceCommand.App(heard)?.ToString() ?? "-"}");
        lines.Add("-- words --");
        foreach (var heard in new[] { "Amr Diab, Baba.", "  عمرو دياب يارب يكون عجبهم. ", "?" })
            lines.Add($"\"{heard}\" -> [{SearchVoiceCommand.Words(heard) ?? "-"}]");
        lines.Add("-- choosing --");
        foreach (var heard in new[] { "Two.", "2", "Number 3.", "Play the first one.", "The second one.", "اتنين", "شغل التاني", "رقم 4", "Next.", "التالي", "Again.", "One more time." })
            lines.Add($"\"{heard}\" -> number {SearchVoiceCommand.Choice(heard)?.ToString() ?? "-"}, next {SearchVoiceCommand.IsNextOne(heard)}, again {SearchVoiceCommand.IsAgain(heard)}");
        lines.Add("-- controls (and what they'd otherwise be) --");
        foreach (var heard in new[] { "Pause the song.", "Resume.", "Next song.", "Previous song.", "وقف الأغنية", "كمل الأغنية", "الأغنية الجاية", "Pause.", "Next.", "Stop." })
            lines.Add($"\"{heard}\" -> {SearchVoiceCommand.TryControl(heard)?.ToString() ?? "-"}, command {(WidgetVoiceCommands.TryMatch(heard, out var c) ? c.ToString() : "-")}");
        lines.Add("-- YouTube results from a saved page (links in order) --");
        var links = new (string, string)[]
        {
            ("Your channel", "https://www.youtube.com/@someone"),
            ("Raaz e Dil", "https://www.googleadservices.com/pagead/aclk?sa=L&ai=x"),
            ("Watch", "https://www.youtube.com/watch?v=qr9viv4A33s"),
            ("Amr Diab - Baba (Official Lyric Video) | (كلمات) عمرو دياب - بابا 3 minutes, 19 seconds", "https://www.youtube.com/watch?v=3knHALzEC7M&pp=ygUN"),
            ("Amr Diab - Baba | عمرو دياب - بابا 3 minutes, 19 seconds", "https://www.youtube.com/watch?v=6e1ROHNUCXk&pp=ygUN"),
            ("Go to channel Mohamed El Husseiny | Director", "https://www.youtube.com/@elhusseinyofficial"),
            ("Mohamed El Husseiny | Director", "https://www.youtube.com/@elhusseinyofficial"),
            ("Your headcount doubled. Your HR team didn't.", "https://www.youtube.com/watch?v=H2FY5uftD1k"),
            ("Mix - Amr Diab - Baba (Official Lyric Video)", "https://www.youtube.com/watch?v=3knHALzEC7M&list=RD3knHA"),
            ("Amr Diab - Baba (Official Lyric Video) · 3:19", "https://www.youtube.com/watch?v=3knHALzEC7M&list=RD3kn"),
            ("A Short 45 seconds", "https://www.youtube.com/shorts/abcdefghijk"),
            ("Amr Diab - Baba (Official Lyric Video) | (كلمات) عمرو دياب - بابا 3 minutes, 19 seconds", "https://www.youtube.com/watch?v=3knHALzEC7M&pp=again"),
            ("Best of Amr Diab | أجمل اغاني عمرو دياب 59 minutes", "https://www.youtube.com/watch?v=FF10_N6qqYg&t=7s&pp=yg"),
            ("Go to channel Mazzika - مزيكا", "https://www.youtube.com/@Mazzika"),
            ("Live concert 1 hour, 2 minutes, 5 seconds", "https://www.youtube.com/watch?v=JPzYmo_PT12"),
            ("Go to channel Rotana", "https://www.youtube.com/@rotanamusic"),
        };
        foreach (var (result, index) in MediaSearch.YouTubeResults(links, 5))
            lines.Add($"link {index}: {result.Title} | by {result.By ?? "-"} | {result.Length} | {result.Address}");
        lines.Add("-- Spotify songs from saved play buttons --");
        foreach (var name in new[] { "Play Getlak by Amr Diab", "Play Lola El Banat by Amr Diab, Orange", "Play Stand by Me by Ben E. King", "Play", "Play Haga Tanya", "Add to playlist" })
            lines.Add($"\"{name}\" -> {(MediaSearch.SpotifySong(name) is { } song ? $"{song.Title} | by {song.By}" : "-")}");
        lines.Add($"address: {MediaSearch.YouTubeAddress("عمرو دياب & Baba")}");
        lines.Add($"address: {MediaSearch.SpotifyAddress("Amr Diab")}");
        lines.Add($"installed: YouTube (Edge) {MediaSearch.IsInstalled(SearchApp.YouTube)}, Spotify {MediaSearch.IsInstalled(SearchApp.Spotify)}");
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        return 0;
    }

    // "MiniCloClo.exe --search-live <youtube|spotify> <words> <report file>":
    // opens the app on its results and reads them - nothing is played.
    public static int SearchLive(string app, string words, string reportPath, int playAfterSeconds = -1)
    {
        var searchApp = app.Equals("spotify", StringComparison.OrdinalIgnoreCase) ? SearchApp.Spotify : SearchApp.YouTube;
        var lines = new System.Collections.Generic.List<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        MediaSearch.Trace = note => { lock (lines) lines.Add($"  {clock.Elapsed.TotalSeconds:0.0} s: {note}"); };
        var error = MediaSearch.OpenAsync(searchApp, words).GetAwaiter().GetResult();
        lines.Add($"  {clock.Elapsed.TotalSeconds:0.0} s: opened");
        lines.Add($"open: {error ?? "ok"}");
        var results = MediaSearch.ReadAsync(searchApp, words, 5, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
        lines.Add($"read in {clock.Elapsed.TotalSeconds:0.0} s: {results?.Count.ToString() ?? "none"}");
        foreach (var r in results ?? [])
            lines.Add($"{r.Title} | by {r.By ?? "-"} | {r.Length ?? "-"} | target {(r.Target != null ? "yes" : "no")}");
        // With a wait: plays the second result that long after reading -
        // as after Keeper has read the list - then pauses it.
        if (playAfterSeconds >= 0 && results is { Count: > 1 })
        {
            System.Threading.Thread.Sleep(playAfterSeconds * 1000);
            var played = MediaSearch.PlayAsync(searchApp, results[1]).GetAwaiter().GetResult();
            lines.Add($"played after {playAfterSeconds} s: {played}");
            System.Threading.Thread.Sleep(5000);
            lines.Add($"paused: {MediaSearch.ControlAsync(SearchVoiceCommand.Control.Pause).GetAwaiter().GetResult()}");
        }
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        return 0;
    }

    public static int MedicineTest(string reportPath)
    {
        var lines = new System.Collections.Generic.List<string>();
        MedicineLog.FolderOverride = Path.Combine(Path.GetTempPath(), "MiniCloClo-medicine-test");
        try
        {
            if (File.Exists(Path.Combine(MedicineLog.FolderOverride, "medicine-log.json")))
                File.Delete(Path.Combine(MedicineLog.FolderOverride, "medicine-log.json"));
            var day = new DateTime(2026, 10, 26);
            var tablet = new CustomReminderSettings { Message = "Blood pressure tablet", IntervalMinutes = 720, StartHour = 8, EndHour = 21, WaitForAnswer = true }.ValidateAndNormalize();
            var vitamin = new CustomReminderSettings { Message = "Vitamin D", IntervalMinutes = 1440, StartHour = 8, EndHour = 9, WaitForAnswer = true }.ValidateAndNormalize();
            var reminders = new[] { tablet, vitamin };
            var start = day.AddHours(7.5);
            foreach (var r in reminders) ReminderSchedule.Start(r, start);
            var tracker = new MedicineTracker();
            for (var t = start; t <= day.AddHours(22); t = t.AddSeconds(20))
            {
                foreach (var r in reminders)
                {
                    var (due, say, slot) = ReminderSchedule.Check(r, t);
                    if (!due) continue;
                    r.LastFired = slot;
                    if (say) tracker.Due(r, slot, t);
                }
                // The person's answers.
                if (t == day.AddHours(8).AddMinutes(7))
                {
                    lines.Add($"{t:HH:mm:ss}  person: \"Later.\" (later: {MedicineTracker.IsLater("Later.")})");
                    tracker.Later(t);
                }
                if (t == day.AddHours(8).AddMinutes(18))
                {
                    lines.Add($"{t:HH:mm:ss}  person: \"Yes, I took it.\" (yes: {CallVoiceCommand.IsYes("Yes, I took it.")})");
                    foreach (var dose in tracker.Taken())
                    {
                        MedicineLog.Record(dose.Message, dose.Due, MedicineLog.Taken, t);
                        lines.Add($"{t:HH:mm:ss}  logged taken: {dose.Message} ({dose.Due:HH:mm} dose)");
                    }
                }
                var (ask, missed) = tracker.Tick(t);
                foreach (var dose in missed)
                {
                    MedicineLog.Record(dose.Message, dose.Due, MedicineLog.Missed, t);
                    lines.Add($"{t:HH:mm:ss}  logged missed: {dose.Message} ({dose.Due:HH:mm} dose), asked {dose.Asked} times");
                }
                if (ask.Count > 0) lines.Add($"{t:HH:mm:ss}  Keeper: Reminder: {MedicineTracker.Question(ask).English}");
            }
            var evening = day.AddHours(22);
            foreach (var question in new[] { "Did I take my medicine?", "Did I take my medicine this morning?", "When did I last take my medicine?", "خدت الدوا؟" })
            {
                var (english, arabic) = MedicineLog.Answer(reminders.Select(r => r.Message).ToList(), question, evening);
                lines.Add($"\"{question}\" -> {english}");
                if (question.StartsWith("خدت")) lines.Add($"    {arabic}");
            }
            lines.Add("-- answers --");
            foreach (var heard in new[] { "Yes.", "نعم", "Later.", "بعدين", "Not yet.", "لسه", "No.", "What time is it?", "Cancel." })
                lines.Add($"\"{heard}\" -> yes {CallVoiceCommand.IsYes(heard)}, later {MedicineTracker.IsLater(heard)}, cancel {ApprovedVoiceAppLauncher.IsCancelCommand(heard)}, command {(WidgetVoiceCommands.TryMatch(heard, out var c) ? c.ToString() : "-")}");
            foreach (var heard in new[] { "Did I take my medicine?", "خدت الدوا؟", "When did I last take my medicine?" })
                lines.Add($"\"{heard}\" -> command {(WidgetVoiceCommands.TryMatch(heard, out var c) ? c.ToString() : "-")}");
        }
        catch (Exception ex)
        {
            lines.Add($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            MedicineLog.FolderOverride = null;
        }
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        return 0;
    }

    // "MiniCloClo.exe --reminder-test <report file>": a fake clock through a
    // day (#39), ticking every 20 s like the app: four reminders and the
    // hourly time, with a restart, the azan, a long prayer look and the PC
    // asleep. Writes what is said and when, through the same schedule and
    // the same combining as the announcement queue.
    public static int ReminderTest(string reportPath)
    {
        var lines = new System.Collections.Generic.List<string>();
        var day = new DateTime(2026, 10, 26);
        CustomReminderSettings Reminder(string message, int every, int from, int to) =>
            new CustomReminderSettings { Message = message, IntervalMinutes = every, StartHour = from, EndHour = to }.ValidateAndNormalize();
        var reminders = new[]
        {
            Reminder("Drink a glass of water", 60, 9, 21),
            Reminder("Take your medicine", 120, 8, 20),
            Reminder("Stand up and stretch", 90, 9, 21),
            Reminder("Night check", 30, 21, 6),
        };
        foreach (var r in reminders) lines.Add($"reminder: {r.Message}, every {r.IntervalMinutes} min, {r.StartHour}:00-{r.EndHour}:00");
        var start = day.AddHours(8.5);
        foreach (var r in reminders) ReminderSchedule.Start(r, start);
        lines.Add($"started {start:HH:mm}: last fired " + string.Join(", ", reminders.Select(r => r.LastFired?.ToString("HH:mm") ?? "-")));

        var queue = new System.Collections.Generic.List<Announcement>();
        var lastChimeHour = start.Hour;
        bool Held(DateTime t) =>
            (t >= day.AddHours(13) && t < day.AddHours(13).AddMinutes(5)) ||   // the azan
            (t >= day.AddHours(18) && t < day.AddHours(18).AddMinutes(20));    // a long prayer look
        bool Asleep(DateTime t) => t >= day.AddHours(15).AddMinutes(5) && t < day.AddHours(16).AddMinutes(40);
        for (var t = start; t <= day.AddDays(1).AddHours(2); t = t.AddSeconds(20))
        {
            if (Asleep(t)) continue;
            if (t == day.AddHours(12).AddMinutes(10))
            {
                // A restart: the saved times are kept; Start changes nothing.
                foreach (var r in reminders) ReminderSchedule.Start(r, t);
                lines.Add($"{t:HH:mm:ss}  (restart: last fired kept: {string.Join(", ", reminders.Select(r => r.LastFired?.ToString("HH:mm")))})");
            }
            if (t.Minute == 0 && t.Hour != lastChimeHour)
            {
                lastChimeHour = t.Hour;
                queue.Add(new Announcement(Announcement.Kinds.Hour, $"It's {SpokenTime.English(t)}", "", t));
            }
            else if (t.Minute != 0) lastChimeHour = -1;
            foreach (var r in reminders)
            {
                var (due, say, slot) = ReminderSchedule.Check(r, t);
                if (!due) continue;
                r.LastFired = slot;
                if (say) queue.Add(new Announcement(Announcement.Kinds.Reminder, r.Message, "", t));
                else lines.Add($"{t:HH:mm:ss}  (missed while asleep, not said: {r.Message} of {slot:HH:mm})");
            }
            var expired = queue.Where(a => t - a.Created > a.Expiry).ToList();
            foreach (var a in expired) lines.Add($"{t:HH:mm:ss}  (dropped, waited too long: {a.English})");
            queue.RemoveAll(a => expired.Contains(a));
            if (queue.Count == 0 || Held(t)) continue;
            var (english, _) = Announcement.Combine(queue);
            lines.Add($"{t:HH:mm:ss}  {english}{(queue.Any(a => a.Created < t) ? $"   [waited since {queue.Min(a => a.Created):HH:mm:ss}]" : "")}");
            queue.Clear();
        }
        lines.Add("-- long readings, sentence by sentence (paused and continued per sentence) --");
        foreach (var text in new[]
                 {
                     "Here it is. CBC | Top Stories News. 3 items. 1: Canada wins gold in curling. 2: Ontario weather warning for Tuesday. 3: New transit line opens in Mississauga. Say a number to hear more.",
                     "اتفضل. BBC Arabic. 3 عناصر. 1: خبر أول عن الاقتصاد. 2: خبر تاني عن الرياضة؟ 3: خبر تالت عن الطقس. قول رقم عشان تسمع التفاصيل.",
                     "It's 3:00 PM.",
                 })
            lines.Add($"{Voice.Sentences(text).Length} parts: " + string.Join(" | ", Voice.Sentences(text)));
        lines.Add("-- next time, shown in the reminders window --");
        var monday = new DateTime(2026, 10, 26);
        foreach (var (label, reminder) in new (string, CustomReminderSettings)[]
                 {
                     ("hourly 9-17", new CustomReminderSettings { Message = "Water", IntervalMinutes = 60, StartHour = 9, EndHour = 17 }),
                     ("90 min 9-17", new CustomReminderSettings { Message = "Stretch", IntervalMinutes = 90, StartHour = 9, EndHour = 17 }),
                     ("daily at 8", new CustomReminderSettings { Message = "Morning medicine", IntervalMinutes = 1440, StartHour = 8, EndHour = 9 }),
                     ("3 h 22-6", new CustomReminderSettings { Message = "Night", IntervalMinutes = 180, StartHour = 22, EndHour = 6 }),
                     ("off", new CustomReminderSettings { Message = "Off", IntervalMinutes = 60, StartHour = 9, EndHour = 17, Enabled = false }),
                 })
            foreach (var at in new[] { 7.5, 9.0, 10.75, 16.5, 23.5 })
                lines.Add($"{label,-12} at {monday.AddHours(at):HH:mm}: {ReminderSchedule.NextLabel(reminder, monday.AddHours(at))}");
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        return 0;
    }

    // "MiniCloClo.exe --engine-check <report file>": which speech runtime
    // loads (graphics card or processor) and how long loading the model and
    // one recognition of a second of silence take (#36).
    public static int EngineCheck(string reportPath)
    {
        var lines = new System.Collections.Generic.List<string>();
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var text = LocalAudioTranscriber.TranscribeCommandForTestAsync(new float[16000], arabic: false).GetAwaiter().GetResult();
            lines.Add($"runtime: {(LocalAudioTranscriber.OnGraphicsCard ? "graphics card (CUDA)" : "processor")}");
            lines.Add($"model load + first recognition: {clock.Elapsed.TotalSeconds:0.0} s");
            text = LocalAudioTranscriber.TranscribeCommandForTestAsync(new float[16000], arabic: false).GetAwaiter().GetResult();
            lines.Add($"second recognition: {LocalAudioTranscriber.LastTestDecode.TotalSeconds:0.00} s");
        }
        catch (Exception ex)
        {
            lines.Add($"{ex.GetType().Name}: {ex.Message}");
        }
        File.WriteAllLines(reportPath, lines);
        return 0;
    }

    // "MiniCloClo.exe --pull-test <report file> [real]": pull tools (#35) on
    // local test tools that don't go online - one that imports, one that
    // reads, one that fails, one that runs too long, one that writes nothing
    // - and the spoken commands. With "real", the installed dates tool also
    // runs once (that one goes online).
    public static int PullTest(string reportPath, bool real)
    {
        var lines = new System.Collections.Generic.List<string>();
        var folder = Path.Combine(Path.GetTempPath(), "MiniCloClo-pull-test");
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            void Tool(string id, string result, int timeout, string script)
            {
                var dir = Directory.CreateDirectory(Path.Combine(folder, id)).FullName;
                File.WriteAllText(Path.Combine(dir, "tool.json"),
                    $"{{\"name\": \"{id}\", \"otherName\": \"{id} بالعربي\", \"description\": \"test {id}\", \"result\": \"{result}\", \"timeoutSeconds\": {timeout}, \"settings\": {{ \"word\": \"Mango\" }}}}",
                    new System.Text.UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, "run.ps1"), "param($SettingsFile, $ResultFile)\n$s = (Get-Content -Raw -Encoding UTF8 $SettingsFile | ConvertFrom-Json).settings\n" + script,
                    new System.Text.UTF8Encoding(true));
            }
            Tool("fruits", "import", 30, "$json = '[{\"Name\":\"Fruits\",\"Actions\":[{\"Word\":\"' + $s.word + '\",\"Kind\":\"text\",\"Text\":\"A sweet fruit.\"},{\"Word\":\"Time\",\"Kind\":\"text\",\"Text\":\"clashes\"}]}]'\n[IO.File]::WriteAllText($ResultFile, $json)");
            Tool("news", "read", 30, "[IO.File]::WriteAllText($ResultFile, '{\"title\":\"Test headlines\",\"items\":[{\"text\":\"First headline.\"},{\"text\":\"Second headline.\",\"otherText\":\"الخبر التاني.\"}]}')");
            Tool("broken", "import", 30, "exit 3");
            Tool("slow", "import", 10, "Start-Sleep -Seconds 60");
            Tool("silent", "read", 30, "exit 0");
            Directory.CreateDirectory(Path.Combine(folder, "Bad Name!"));
            PullTools.FolderOverride = folder;

            var tools = PullTools.Load();
            lines.Add("tools found: " + string.Join(", ", tools.Select(t => $"{t.Id} ({t.Result}, {t.TimeoutSeconds}s, settings: {string.Join(";", t.Settings.Select(kv => kv.Key + "=" + kv.Value))})")));
            var own = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>
            {
                ["fruits"] = new() { ["word"] = "Papaya", ["unknown"] = "ignored" },
            };
            foreach (var tool in tools)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var (outcome, file) = PullTools.RunAsync(tool, PullTools.EffectiveSettings(tool, own)).GetAwaiter().GetResult();
                var line = $"{tool.Id}: {outcome} in {clock.Elapsed.TotalSeconds:0.0} s";
                if (file != null && tool.Result == PullTool.ImportResult)
                {
                    var topics = SampleTopics(DateTime.Today);
                    var (added, replaced, changed) = TopicImport.Merge(topics, TopicImport.ReadFile(file));
                    line += $" -> imported: {added} added, {replaced} replaced: " + string.Join(" | ", changed.Select(t => TopicAnswers.TopicContents(t).English));
                }
                else if (file != null && PullTools.ReadReading(file) is { } reading)
                    line += $" -> read: {reading.Title}: " + string.Join(" | ", reading.Items.Select(i => i.Text + (i.OtherText != null ? " / " + i.OtherText : "")));
                lines.Add(line);
            }
            lines.Add("-- what is said --");
            foreach (var heard in new[] { "Pull fruits.", "Pull the news.", "Pull.", "Pull nooz.", "Pull weather.", "What can I pull?", "اسحب news", "Update fruits.", "Pull dates.", "Pull it.", "Ramadan date.", "Paul nooz.", "Paul Simon.", "Pool fruits.", "Bull fruits.", "Bull the news.", "Bull market.", "Poll fruits.", "Pole news." })
            {
                var request = PullVoiceCommand.TryParse(heard, tools);
                lines.Add($"\"{heard}\" -> " + request switch
                {
                    PullVoiceCommand.ListRequest => "list of tools",
                    PullVoiceCommand.PullRequest { Tool: { } t } => "pull " + t.Id,
                    PullVoiceCommand.PullRequest { Unknown: { } u } => $"no tool by that name ({u})",
                    PullVoiceCommand.PullRequest => "which one?",
                    _ => "-",
                });
            }
            foreach (var heard in new[] { "Two.", "2", "Number 2.", "One four.", "اتنين", "رقم ٣", "One more time.", "Again.", "Two news.", "Call Mom." })
                lines.Add($"number alone \"{heard}\" -> {QuranVoiceAnswers.NumberAlone(heard)?.ToString() ?? "-"}");
            foreach (var word in new[] { "Pull", "Pull news", "Update", "Mango" })
                lines.Add($"own word \"{word}\" -> {TopicVoiceCommand.Clash(word) ?? "free"}");
        }
        catch (Exception ex)
        {
            lines.Add($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            PullTools.FolderOverride = null;
        }
        if (real)
        {
            foreach (var news in PullTools.Load().Where(t => t.Id == "news"))
                foreach (var arabic in new[] { false, true })
                {
                    var (newsOutcome, newsFile) = PullTools.RunAsync(news, PullTools.EffectiveSettings(news, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>()), arabic).GetAwaiter().GetResult();
                    var reading = newsFile != null ? PullTools.ReadReading(newsFile) : null;
                    lines.Add($"-- the news tool ({(arabic ? "Arabic" : "English")}): {newsOutcome}, {reading?.Title ?? "-"}, {reading?.Items.Count ?? 0} headlines");
                }
            var dates = PullTools.Load().FirstOrDefault(t => t.Id == "dates");
            lines.Add($"-- the installed dates tool: {(dates == null ? "not found" : dates.Folder)}");
            if (dates != null)
            {
                // No country or province set: they come from the place (Toronto).
                var settings = PullTools.EffectiveSettings(dates, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>());
                var toronto = OfflineCities.Find("Toronto")!;
                var place = new PullTools.Place(toronto.Name, OfflineCities.CountryCode(toronto), OfflineCities.RegionCode(toronto));
                lines.Add($"place: {place}");
                var (outcome, file) = PullTools.RunAsync(dates, settings, place: place).GetAwaiter().GetResult();
                lines.Add($"dates: {outcome}");
                if (file != null)
                {
                    var topics = SampleTopics(DateTime.Today);
                    var (added, replaced, changed) = TopicImport.Merge(topics, TopicImport.ReadFile(file));
                    lines.Add($"imported: {added} added, {replaced} replaced");
                    foreach (var topic in changed) lines.Add("  " + TopicAnswers.TopicContents(topic).English);
                }
            }
        }
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        return 0;
    }

    // "MiniCloClo.exe --topics-test <report file>": the answers and the
    // matching of questions and answers, on sample topics.
    public static int TopicsTest(string reportPath, string? importFile = null)
    {
        var lines = new System.Collections.Generic.List<string>();
        if (importFile != null)
        {
            // The Import button's path, on a real window with the ready-made topics.
            var app = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Voice.Muted = true;
            var window = new TopicsWindow(TopicAction.IslamicDates()) { Left = -20000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            Pump(300);
            lines.Add($"before import: shown topic {window.ShownTopic}, actions: {string.Join(" | ", window.ShownActions)}");
            window.ImportFile(importFile);
            Pump(300);
            lines.Add($"after import: shown topic {window.ShownTopic}, actions: {string.Join(" | ", window.ShownActions)}");
            foreach (var topic in window.Topics) lines.Add($"window topic: {topic.Name}: {string.Join(", ", topic.Actions.Select(a => $"{a.Word} [{a.Kind}, {a.Dates.Count}]"))}");
            window.Close();
            // Read a file the way Import does (tools\Get-CalendarTopics.ps1).
            var imported = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<TopicSettings>>(File.ReadAllText(importFile)) ?? new();
            foreach (var topic in imported.Select(t => t?.ValidateAndNormalize()).Where(t => t != null).Cast<TopicSettings>())
            {
                lines.Add($"imported topic: {topic.Name} / {topic.OtherName}, {topic.Actions.Count} actions");
                foreach (var action in topic.Actions)
                    lines.Add($"  {action.Word}{(action.OtherWord != null ? " / " + action.OtherWord : "")}: {action.Dates.Count} dates, clash: {TopicVoiceCommand.Clash(action.Word) ?? "none"} -> {TopicAnswers.Answer(action, DateTime.Today).English}");
            }
        }
        var today = DateTime.Today;
        var topics = SampleTopics(today);
        lines.Add($"today: {today:yyyy-MM-dd} ({SpokenTime.HijriText(today)})");
        foreach (var action in topics.SelectMany(t => t.Actions))
        {
            var (english, arabic) = TopicAnswers.Answer(action, today);
            lines.Add($"{action.Word}: {english} | {arabic}");
        }
        lines.Add("-- Hijri dates checked back --");
        foreach (var (month, day) in new[] { (9, 1), (10, 1), (12, 10), (1, 1), (12, 30) })
            lines.Add($"{day} {SpokenTime.HijriMonthName(month)} -> {SpokenTime.NextHijriDate(month, day, today):yyyy-MM-dd} = {(SpokenTime.NextHijriDate(month, day, today) is { } d ? SpokenTime.HijriText(d) : "-")}");
        lines.Add("-- what is said --");
        foreach (var heard in new[] { "Ramadan.", "When is Ramadan?", "How many days until Ramadan?", "Ramadhan.", "Eid.", "When is Eid al-Adha?",
                     "رمضان", "رمضان امتى؟", "فاضل قد ايه على رمضان؟", "العيد", "عيد الأضحى امتى", "الحج", "Civic.", "When is Civic?",
                     "Clinic.", "العيادة", "Vacations.", "What's in Vacations?", "الإجازات", "What topics do I have?", "مواضيعي",
                     "What time is it?", "Calculator.", "Call Mom.", "Hello there.", "رمضان أمجي.", "فاضل قد إه على العيدي.", "موضعي.", "الحجي.", "Colchia.", "Laylat al-Kodim.", "Laylat al-Qadr.", "Again.", "Say it again.", "Date again.", "Repeat that.", "One more time.", "قولها تاني", "تاني", "كمان مرة", "Repeat.", "Canada Day.", "When is Labour Day?", "Family Day date.", "Ashura date.", "Ramadan date.", "Ramadan's date.", "Eid al-Adha date.", "Civic date.", "Date of Ramadan.", "What's the date of Ashura?", "A surah date.", "Date.", "The date.", "تاريخ رمضان", "رمضان تاريخه", "عاشوراء ميعادها", "ميعاد الحج", "Ashura.", "A surah.", "A sura.", "Choose a surah.", "Quran surah.", "Surah.", "Shab-e-Barat.", "Isra and Miraj.", "Dhul Hijjah." })
        {
            var exact = TopicVoiceCommand.TryMatch(heard, topics, loose: false);
            var match = exact ?? TopicVoiceCommand.TryMatch(heard, topics, loose: true);
            var shown = match switch
            {
                TopicVoiceCommand.ActionMatch a => "action " + a.Action.Word,
                TopicVoiceCommand.ActionsMatch m => "actions " + string.Join(" + ", m.Actions.Select(x => x.Word)),
                TopicVoiceCommand.TopicMatch t => "topic " + t.Topic.Name,
                TopicVoiceCommand.ListMatch => "list of topics",
                _ => "-",
            };
            lines.Add($"\"{heard}\" -> {shown}{(match != null && exact == null ? " (by sound)" : "")}");
            if (WidgetVoiceCommands.TryMatch(heard, out var builtIn))
                lines.Add($"    built-in {builtIn}; own word that sounds the same: {(TopicVoiceCommand.TryMatch(heard, topics, loose: true, sameSound: true) is TopicVoiceCommand.ActionMatch same ? same.Action.Word : "none")}");
        }
        lines.Add("-- words that can't be used --");
        foreach (var word in new[] { "Time", "Quran", "Calculator", "Call Mom", "Cancel", "Ramadan", "Civic", "الساعة" })
            lines.Add($"\"{word}\" -> {TopicVoiceCommand.Clash(word) ?? "free"}");
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        return 0;
    }

    // "MiniCloClo.exe --bt-test <report file> <device name> [number]": the
    // experimental Bluetooth dialer. Without a number it only connects, does
    // the hands-free handshake and disconnects; with one it also dials it.
    public static int BluetoothTest(string reportPath, string deviceName, string? number)
    {
        var lines = new System.Collections.Generic.List<string>();
        try
        {
            var paired = BluetoothDialer.PairedDevicesAsync().GetAwaiter().GetResult();
            lines.Add("paired devices: " + string.Join(", ", paired.Select(d => d.Name)));
            var phone = paired.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
            if (phone == null) lines.Add($"no paired device named {deviceName}");
            else
            {
                var dialer = new BluetoothDialer();
                var ok = dialer.RunAsync(phone, number).GetAwaiter().GetResult();
                lines.AddRange(dialer.Log);
                lines.Add(ok ? "RESULT: success" : "RESULT: failed");
                if (ok && number != null)
                {
                    // Did the call outlive our disconnect? Look again after
                    // 10 s: the second CIND value is "call" (1 = in progress).
                    lines.Add("-- waiting 10 s disconnected, then checking the call is still up --");
                    System.Threading.Thread.Sleep(10000);
                    var check = new BluetoothDialer();
                    check.RunAsync(phone, null).GetAwaiter().GetResult();
                    lines.AddRange(check.Log);
                    var status = check.Log.LastOrDefault(l => l.Contains("AT+CIND? ->"));
                    var values = status?[(status.IndexOf("+CIND:") + 6)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split(',');
                    lines.Add(values is { Length: > 1 }
                        ? $"CALL AFTER DISCONNECT: {(values[1] == "1" ? "still in progress (call=1)" : "not in progress (call=0)")}, call setup={values[2]}"
                        : "CALL AFTER DISCONNECT: couldn't read the phone's status");
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add($"{ex.GetType().Name}: {ex.Message}");
        }
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        return 0;
    }

    // "MiniCloClo.exe --quran-test <report file>": the choose-a-surah end of
    // the Quran window - the whole of Al-Fatiha (which must start again from
    // ayah 1 after the last), then ayat 3 to 5 - recording what it shows
    // every half second. Plays the real recitation; speech is muted; the
    // saved Quran position is put back afterwards.
    public static int QuranTest(string reportPath)
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Voice.Muted = true;
        // Progress goes to a throwaway folder: never the person's own file.
        QuranStore.FolderOverride = Path.Combine(Path.GetTempPath(), "MiniCloClo-quran-test");
        var lines = new System.Collections.Generic.List<string>();
        try
        {
            var window = new QuranWindow(new QuranStore()) { SkipIntro = true, Left = -20000, ShowActivated = false, ShowInTaskbar = false };
            window.Show();
            // The choose-a-surah dialog acts on a new window right after
            // Show(), so this must already be true.
            lines.Add($"Loaded when Show() returns: {window.IsLoaded}");
            Pump(500);
            string State() => $"{(window.FindName("RepeatText") as System.Windows.Controls.TextBlock)?.Text} | reciting {window.IsReciting}";
            var last = "";
            void Watch(int seconds)
            {
                for (var i = 0; i < seconds * 2; i++)
                {
                    Pump(500);
                    var state = State();
                    if (state != last) lines.Add($"[{i / 2.0,5:0.0}s] {state}");
                    last = state;
                }
            }
            lines.Add("== PlayWholeSurah(1)");
            window.PlayWholeSurah(1);
            Watch(62);
            lines.Add("== OpenRange(1, 3, 5)");
            window.OpenRange(1, 3, 5);
            Watch(18);
            window.Close();
        }
        finally
        {
            File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        }
        app.Shutdown();
        return 0;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    // "MiniCloClo.exe --prayer-test <report file>": today's times for a few
    // listed cities, plus the spoken date, to compare with a mosque timetable.
    public static int PrayerTest(string reportPath)
    {
        var lines = new System.Collections.Generic.List<string> { SpokenTime.DateEnglish(DateTime.Now), SpokenTime.DateArabic(DateTime.Now) };
        foreach (var name in new[] { "Cairo", "Makkah", "London", "Toronto", "Karachi" })
        {
            var city = OfflineCities.Find(name)!;
            var zone = PrayerTimeZone.Resolve(city.TimeZoneId);
            var date = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date;
            var times = PrayerTimesCalculator.ForDate(date, city.Latitude, city.Longitude, city.Method, "standard");
            lines.Add($"{city.Name} ({city.Method}, {zone.Id}): " + string.Join("  ",
                times.Select(p => $"{p.Name} {TimeZoneInfo.ConvertTime(p.StartsAtUtc, zone):HH:mm}")));
            if (PrayerTimesCalculator.SunTimes(date, city.Latitude, city.Longitude) is { } sun)
                lines.Add($"    sunrise {TimeZoneInfo.ConvertTime(sun.Sunrise, zone):HH:mm}, sunset {TimeZoneInfo.ConvertTime(sun.Sunset, zone):HH:mm}, " +
                          $"now {TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone):HH:mm} -> {(DateTimeOffset.UtcNow < sun.Sunrise || DateTimeOffset.UtcNow >= sun.Sunset ? "moon" : "sun")}");
            lines.Add($"    azan: {AzanLibrary.Default ?? "(none)"}");
        }
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        return 0;
    }

    // "MiniCloClo.exe --voice-test <report file>": speaks each phrase with a
    // Windows voice into memory, recognizes it as a voice command would be,
    // and writes what was heard and which command it matched.
    // only: run just the spoken phrases containing this text (quicker).
    public static int VoiceTest(string reportPath, string? only = null)
    {
        var phrases = new (string Text, bool Arabic)[]
        {
            ("What time is it?", false), ("What's the date?", false), ("Next prayer.", false), ("Prayer times.", false),
            ("Open the Quran.", false), ("Next.", false), ("Reminders.", false), ("Help.", false), ("Stop.", false),
            ("I prayed.", false), ("Open Notepad.", false), ("Calculator.", false), ("Narrator.", false), ("Shut down.", false),
            ("Pull dates.", false), ("Pull the dates.", false), ("What can I pull?", false), ("Ramadan.", false), ("When is Eid al-Adha?", false), ("How many days until Ramadan?", false), ("Civic.", false), ("What topics do I have?", false), ("Choose a surah.", false), ("Quran surah.", false), ("Quran, surah.", false), ("Quran surah please.", false), ("Quran.", false), ("Al-Fatiha.", false), ("Al-Baqara.", false), ("Surah Yasin.", false), ("Number two.", false),
            ("Three.", false), ("Five.", false), ("Ten.", false), ("Fourteen.", false), ("Seventeen.", false), ("Forty.", false), ("One four.", false), ("One three.", false), ("One seven.", false), ("Two three four.", false), ("Two eight six.", false), ("One hundred and twenty.", false), ("No.", false), ("Go back.", false), ("All.", false), ("The whole surah.", false), ("Twenty five.", false), ("To the end.", false),
            ("اسحب التواريخ", true), ("حدث التواريخ", true), ("رمضان", true), ("رمضان امتى؟", true), ("فاضل قد ايه على العيد؟", true), ("الحج", true), ("مواضيعي", true), ("اختار سورة", true), ("القرآن سورة", true), ("الفاتحة", true), ("سورة البقرة", true), ("ثلاثة", true), ("خمسة", true), ("عشرة", true), ("أربعتاشر", true), ("سبعة وعشرين", true), ("واحد أربعة", true), ("واحد سبعة", true), ("اتنين تلاتة أربعة", true), ("واحد تلاتة", true), ("لا", true), ("ارجع", true), ("كلها", true), ("السورة كلها", true),
            ("Call Mom.", false), ("Call Ahmed.", false), ("Call sho.", false), ("Yes.", false), ("Yes!", false), ("Yes, yes.", false),
            ("Yes, call him.", false), ("Yeah.", false), ("Yes please.", false), ("Okay.", false), ("اتصل بماما", true), ("اتصل بأحمد", true), ("ايوه", true), ("نعم", true), ("نعم.", true), ("نعم، اتصل", true), ("نعم نعم", true), ("لا", true),
            ("الساعة كام", true), ("كم الساعة", true), ("مواقيت الصلاة", true), ("اسكت", true), ("افتح المفكرة", true), ("الصلاة الجاية", true), ("افتح القرآن", true), ("صليت", true), ("التاريخ", true),
        };
        var lines = new System.Collections.Generic.List<string>();
        var topics = SampleTopics(DateTime.Today);
        var pullTools = PullTools.Load();
        var decodeTimes = new System.Collections.Generic.List<double>();
        lines.Add($"model: {LocalAudioTranscriber.TestModelPath ?? "bundled base"}");
        static string TopicLabel(TopicVoiceCommand.Match match) => match switch
        {
            TopicVoiceCommand.ActionMatch a => a.Action.Word,
            TopicVoiceCommand.ActionsMatch m => string.Join(" + ", m.Actions.Select(x => x.Word)),
            TopicVoiceCommand.TopicMatch t => "topic " + t.Topic.Name,
            _ => "list of topics",
        };
        foreach (var (text, arabic) in phrases)
        {
            if (only != null && !text.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var samples = Synthesize(text, arabic).GetAwaiter().GetResult();
                if (samples == null) { lines.Add($"{text} -> (no {(arabic ? "Arabic" : "English")} voice installed)"); continue; }
                var heard = LocalAudioTranscriber.TranscribeCommandForTestAsync(samples, arabic,
                    CallVoiceCommand.RecognitionHint(TestContacts, arabic)).GetAwaiter().GetResult();
                var decode = LocalAudioTranscriber.LastTestDecode;
                decodeTimes.Add(decode.TotalSeconds);
                // Answers to a choose-a-surah question, heard with its own hint.
                var answer = LocalAudioTranscriber.TranscribeCommandForTestAsync(samples, arabic, prompt: arabic
                    ? QuranVoiceAnswers.ArabicRecognitionPrompt : QuranVoiceAnswers.RecognitionPrompt).GetAwaiter().GetResult();
                var answers = $" [as an answer \"{answer}\": surah {QuranVoiceAnswers.Surah(answer)?.ToString() ?? "-"}, number {QuranVoiceAnswers.Number(answer)?.ToString() ?? "-"}, all {QuranVoiceAnswers.IsAll(answer)}, end {QuranVoiceAnswers.IsEnd(answer)}]";
                string match = CallVoiceCommand.TryParse(heard, out var joined) is { } who && (!joined || CallVoiceCommand.Match(who, TestContacts) != null)
                        ? "call:" + (CallVoiceCommand.Match(who, TestContacts)?.Name ?? $"(no contact for \"{who}\")")
                    : CallVoiceCommand.IsYes(heard) ? "yes (confirms a call)"
                    : WidgetVoiceCommands.TryMatch(heard, out var command) ? "widget:" + command
                    : TopicVoiceCommand.TryMatch(heard, topics, loose: false) is { } topicMatch ? "topic:" + TopicLabel(topicMatch)
                    : PullVoiceCommand.TryParse(heard, pullTools) is { } pull ? "pull:" + (pull is PullVoiceCommand.PullRequest { Tool: { } pulled } ? pulled.Id : pull is PullVoiceCommand.ListRequest ? "list" : "which one")
                    : ApprovedVoiceAppLauncher.IsShutdownCommand(heard) ? "shutdown"
                    : ApprovedVoiceAppLauncher.IsCancelCommand(heard) ? "cancel/stop"
                    : ApprovedVoiceAppLauncher.TryMatchHeldCommand(heard, out var app) ? "app:" + app
                    : TopicVoiceCommand.TryMatch(heard, topics, loose: true) is { } looseTopic ? "topic (by sound):" + TopicLabel(looseTopic)
                    : WidgetVoiceCommands.TryMatchLoosely(heard, out var loose) ? "widget (loose):" + loose
                    : "NO MATCH";
                lines.Add($"{text} -> heard \"{heard}\" -> {match} ({decode.TotalSeconds:0.00} s){answers}");
            }
            catch (Exception ex)
            {
                lines.Add($"{text} -> {ex.GetType().Name}: {ex.Message}");
            }
        }
        // Arabic voice mode during a surah question: each answer is read as
        // Arabic and as English, and the clean number wins.
        if (decodeTimes.Count > 0)
            lines.Add($"-- command recognition time: average {decodeTimes.Average():0.00} s, slowest {decodeTimes.Max():0.00} s, {decodeTimes.Count} phrases --");
        lines.Add("-- Arabic mode answers, read as Arabic and as English --");
        foreach (var (text, voiceArabic) in new (string, bool)[]
                 {
                     ("One four.", false), ("Two three four.", false), ("One seven.", false), ("Two eight six.", false),
                     ("واحد أربعة", true), ("اتنين تلاتة أربعة", true), ("سبعة وعشرين", true), ("واحد سبعة", true), ("خمسة", true),
                 })
        {
            try
            {
                var samples = Synthesize(text, voiceArabic).GetAwaiter().GetResult();
                if (samples == null) { lines.Add($"{text} -> (no voice installed)"); continue; }
                var asArabic = LocalAudioTranscriber.TranscribeCommandForTestAsync(samples, arabic: true,
                    prompt: QuranVoiceAnswers.ArabicRecognitionPrompt).GetAwaiter().GetResult();
                var asEnglish = LocalAudioTranscriber.TranscribeCommandForTestAsync(samples, arabic: false,
                    prompt: QuranVoiceAnswers.RecognitionPrompt).GetAwaiter().GetResult();
                lines.Add($"{text} -> Arabic \"{asArabic}\" ({QuranVoiceAnswers.Number(asArabic)?.ToString() ?? "-"}), " +
                          $"English \"{asEnglish}\" ({QuranVoiceAnswers.Number(asEnglish)?.ToString() ?? "-"}) -> " +
                          $"{QuranVoiceAnswers.Number(asArabic, asEnglish)?.ToString() ?? "asked again"}");
            }
            catch (Exception ex)
            {
                lines.Add($"{text} -> {ex.GetType().Name}: {ex.Message}");
            }
        }
        lines.Add("-- choose-a-surah answers, as Whisper may write them --");
        foreach (var heard in new[] { "1", "Surah 2.", "Al-Faatiha", "Fatiha.", "The Cow.", "Baqarah.", "Yaseen.", "Surat Al-Kahf", "114.",
                     "115.", "Ayah 3.", "From ayah three.", "twenty-five", "Two hundred and eighty six.", "the first", "from the start",
                     "All.", "All of it.", "The whole surah.", "To the end.", "Until the end of the surah.",
                     "الفاتحه", "سورة البقرة", "سوره يس", "الكهف", "٣", "ثلاثة", "التالتة", "خمسة وعشرين", "ميتين وستة وتمانين",
                     "من الآية ٣", "لحد ٥", "كلها", "السورة كلها", "للآخر",
                     "1.4.", "واحد 4.", "From 3 to 5.", "الفاتحة. حدوه.", "Won tree.", "One, seven.", "2, 3, 4.", "واحد سلاسة جن.", "واحد سبات.", "واحد سباتن.", "اتنين تلاته اربعه", "حدوه", "جن", "Den.", "Forteen.", "Fiftin.", "Sixdeen.", "Twendy.", "One four.", "Two, eight, six.", "Seven.", "سبأ", "سبع", "Al-Fadiha.", "Al-Bakira.", "Surah Yawson.", "سلاسة جن.", "قوليها.", "Choosa sura.", "اخطار سورة.", "Quran surah.", "Quran, surah.", "Koran Sura.", "القرآن سورة", "القرآن وصورة جن.", "Quran.", "القرآن" })
            lines.Add($"\"{heard}\" -> surah {QuranVoiceAnswers.Surah(heard)?.ToString() ?? "-"}, number {QuranVoiceAnswers.Number(heard)?.ToString() ?? "-"}, all {QuranVoiceAnswers.IsAll(heard)}, end {QuranVoiceAnswers.IsEnd(heard)}, command {(WidgetVoiceCommands.TryMatch(heard, out var c) ? c.ToString() : "-")}");
        foreach (var heard in new[] { "No.", "No, no.", "Go back.", "Back.", "Wrong.", "No, that's wrong.", "لا", "لأ.", "ارجع", "غلط", "لا غلط.",
                     "No, five.", "Cancel.", "Stop.", "Not now.", "لا. لا. لديك.", "لا لا لا", "لا، لديك", "No. No. Thank you.", "لا خمسة", "لا ٥", "No, all.", "Nobody." })
            lines.Add($"\"{heard}\" -> back {QuranVoiceAnswers.IsBack(heard)}, cancel {ApprovedVoiceAppLauncher.IsCancelCommand(heard)}, number {QuranVoiceAnswers.Number(heard)?.ToString() ?? "-"}");
        lines.Add("-- transcripts as Whisper writes them --");
        foreach (var heard in new[] { "نعم.", "نعم، إتصل.", "نام نام.", "نعم لا.", "لا نعم", "لأسفل.", "اي تصلار بأحمد.", "إتو صلاة بمامن.", "اي صلاة الجاية", "اتصلاق بأحمد.", "Yet.", "Yet!", "Not yet.", "Yet another.", "Yes. Call sho.", "Yes, yes.", "Yes, call him.", "Yeah, sure.", "OK.", "ايوه تمام",
                     "No, don't call.", "Cancel.", "Yes. No.", "Calcia.", "Call sho.", "Callsho." })
            lines.Add($"\"{heard}\" -> yes: {CallVoiceCommand.IsYes(heard)}, call: " +
                      (CallVoiceCommand.TryParse(heard) is { } who ? CallVoiceCommand.Match(who, TestContacts)?.Name ?? $"(no contact for \"{who}\")" : "-"));
        File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);
        return 0;
    }

    private static readonly ContactSettings[] TestContacts =
    [
        new() { Name = "Mom", OtherName = "ماما", Number = "1000" },
        new() { Name = "Ahmed", OtherName = "أحمد", Number = "2000" },
        new() { Name = "sho", OtherName = "sho", Number = "3000" },
    ];

    private static async System.Threading.Tasks.Task<float[]?> Synthesize(string text, bool arabic)
    {
        var voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v =>
            v.Language.StartsWith(arabic ? "ar" : "en", StringComparison.OrdinalIgnoreCase));
        if (voice == null) return null;
        using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer { Voice = voice };
        using var stream = await synth.SynthesizeTextToStreamAsync(text);
        var wav = new MemoryStream();
        using (var read = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream)) await read.CopyToAsync(wav);
        wav.Position = 0;
        using var reader = new NAudio.Wave.WaveFileReader(wav);
        NAudio.Wave.ISampleProvider samples = NAudio.Wave.WaveExtensionMethods.ToSampleProvider(reader);
        if (samples.WaveFormat.Channels == 2) samples = new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(samples);
        if (samples.WaveFormat.SampleRate != 16000) samples = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(samples, 16000);
        var all = new System.Collections.Generic.List<float>();
        var buffer = new float[16000];
        int n;
        while ((n = samples.Read(buffer.AsSpan())) > 0) all.AddRange(buffer.Take(n));
        return all.ToArray();
    }
}
#endif

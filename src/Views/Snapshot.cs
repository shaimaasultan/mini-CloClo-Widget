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
    public static int VoiceTest(string reportPath)
    {
        var phrases = new (string Text, bool Arabic)[]
        {
            ("What time is it?", false), ("What's the date?", false), ("Next prayer.", false), ("Prayer times.", false),
            ("Open the Quran.", false), ("Next.", false), ("Reminders.", false), ("Help.", false), ("Stop.", false),
            ("I prayed.", false), ("Open Notepad.", false), ("Calculator.", false), ("Narrator.", false), ("Shut down.", false),
            ("Call Mom.", false), ("Call Ahmed.", false), ("Call sho.", false), ("Yes.", false), ("Yes!", false), ("Yes, yes.", false),
            ("Yes, call him.", false), ("Yeah.", false), ("Yes please.", false), ("Okay.", false), ("اتصل بماما", true), ("اتصل بأحمد", true), ("ايوه", true),
            ("الساعة كام", true), ("كم الساعة", true), ("مواقيت الصلاة", true), ("اسكت", true), ("افتح المفكرة", true), ("الصلاة الجاية", true), ("افتح القرآن", true), ("صليت", true), ("التاريخ", true),
        };
        var lines = new System.Collections.Generic.List<string>();
        foreach (var (text, arabic) in phrases)
        {
            try
            {
                var samples = Synthesize(text, arabic).GetAwaiter().GetResult();
                if (samples == null) { lines.Add($"{text} -> (no {(arabic ? "Arabic" : "English")} voice installed)"); continue; }
                var heard = LocalAudioTranscriber.TranscribeCommandForTestAsync(samples, arabic,
                    CallVoiceCommand.RecognitionHint(TestContacts, arabic)).GetAwaiter().GetResult();
                string match = CallVoiceCommand.TryParse(heard) is { } who
                        ? "call:" + (CallVoiceCommand.Match(who, TestContacts)?.Name ?? $"(no contact for \"{who}\")")
                    : CallVoiceCommand.IsYes(heard) ? "yes (confirms a call)"
                    : WidgetVoiceCommands.TryMatch(heard, out var command) ? "widget:" + command
                    : ApprovedVoiceAppLauncher.IsShutdownCommand(heard) ? "shutdown"
                    : ApprovedVoiceAppLauncher.IsCancelCommand(heard) ? "cancel/stop"
                    : ApprovedVoiceAppLauncher.TryMatchHeldCommand(heard, out var app) ? "app:" + app
                    : WidgetVoiceCommands.TryMatchLoosely(heard, out var loose) ? "widget (loose):" + loose
                    : "NO MATCH";
                lines.Add($"{text} -> heard \"{heard}\" -> {match}");
            }
            catch (Exception ex)
            {
                lines.Add($"{text} -> {ex.GetType().Name}: {ex.Message}");
            }
        }
        lines.Add("-- transcripts as Whisper writes them --");
        foreach (var heard in new[] { "Yes. Call sho.", "Yes, yes.", "Yes, call him.", "Yeah, sure.", "OK.", "ايوه تمام",
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

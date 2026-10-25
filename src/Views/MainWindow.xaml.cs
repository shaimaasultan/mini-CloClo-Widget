using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace MiniCloClo;

// Mini CloClo: the avatar with the time and dates, prayer times with the
// azan, custom reminders, Quran memorization and voice commands - and
// nothing that goes online. Made to be used without seeing the screen:
// everything it shows is also spoken, and everything it does can be reached
// by voice (MainWindow.PushToTalk.cs), by Ctrl+Alt shortcuts from any app
// (MainWindow.Hotkeys.cs) or from the keyboard once the widget has focus.
public partial class MainWindow : Window
{
    private readonly WidgetSettings _settings = WidgetSettings.Load();
    private TrayIcon? _trayIcon;
    private DispatcherTimer? _topmostTimer;
    private DispatcherTimer? _idleTimer;
    private DispatcherTimer? _minuteTimer;
    private DispatcherTimer? _prayerTimer;
    private DispatcherTimer? _labelTimer;
    private DispatcherTimer? _reminderLookTimer;
    private LocalAudioPlayer? _azanPlayer;
    private LocalAudioPlayer? _reminderAudioPlayer;
    private DateTimeOffset _lastPrayerCheckUtc;
    private readonly HashSet<long> _playedPrayerInstants = new();
    private readonly HashSet<long> _alertedPrayerInstants = new();
    private int _lastChimeHour = -1;
    // "awake", "asleep" or "prayer" — see UpdateActivityState.
    private string _activityState = "awake";
    // Set when a prayer time arrives with the prayer look on: the avatar
    // wears prayer clothes on a prayer mat until the prayer is marked done.
    private bool _prayerLookActive;
    private string? _currentPrayer;
    private ContextMenu? _activeContextMenu;
    private QuranWindow? _quranWindow;
    private QuranStore? _quranStore;
    private DateTime _lastQuranReminder = DateTime.Now;
    private Window? _openDialog;
    private bool _closing;
    // Hold to talk doesn't listen while the azan or a recitation plays.
    private bool _voicePausedForAudio;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    // How much of the widget (in physical pixels, each axis) has to land on a
    // monitor's working area before its position counts as usable.
    private const int MinVisiblePixels = 80;

    // KeeperView draws on a 150x292 stage scaled by 1.3 and the widget size;
    // these rects are in stage units (see KeeperView.xaml).
    private const double StageScale = 1.3;
    private static readonly Rect PrayerIconRect = new(24.5, 230, 24, 22);
    private static readonly Rect QuranIconRect = new(51.5, 230, 24, 22);
    private static readonly Rect RemindersIconRect = new(78.5, 230, 24, 22);
    private static readonly Rect MicIconRect = new(105.5, 230, 24, 22);
    private static readonly Rect CloseLabelRect = new(124, 255, 24, 22);
    private static readonly Rect LabelRect = new(3, 255, 144, 36);
    // At prayer time the check badge beside the head, and the head itself,
    // both mean "I've prayed". The art is drawn in 100x140 units in a
    // 108x145 Viewbox placed at stage (21, 24).
    private static readonly Rect PrayedBadgeRect = ArtRect(77, 15, 19, 19);
    private static readonly Rect HeadRect = ArtRect(23, 18, 54, 54);

    private static Rect ArtRect(double x, double y, double width, double height)
    {
        const double artScale = 145.0 / 140;
        const double artLeft = 21 + (108 - 100 * artScale) / 2;
        return new Rect(artLeft + x * artScale, 24 + y * artScale, width * artScale, height * artScale);
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    public MainWindow()
    {
        InitializeComponent();
        Voice.Configure(() => _settings.VoiceCommandsInArabic, _settings.SpeechRate);
        Loaded += MainWindow_Loaded;
        Closing += (_, _) =>
        {
            _closing = true;
            CancelShutdownCountdown();
            UpdatePushToTalkHook(); // closing: removes the mouse watcher
            // Frees the speech model (at once, or when a command still being
            // decoded finishes with it).
            LocalAudioTranscriber.UnloadModel();
            UnregisterHotkeys();
            Keeper.SetVoiceStatus("off");
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SaveWindowPosition();
            foreach (var timer in new[] { _topmostTimer, _idleTimer, _minuteTimer, _prayerTimer, _labelTimer, _reminderLookTimer })
                timer?.Stop();
            _azanPlayer?.Dispose();
            _azanPlayer = null;
            _reminderAudioPlayer?.Dispose();
            _reminderAudioPlayer = null;
            SpeechReader.Stop();
        };
        MouseLeftButtonDown += Widget_MouseLeftButtonDown;
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenMenu(fromTray: false, atMouse: true);
        };
        KeyDown += Widget_KeyDown;
        GotKeyboardFocus += (_, _) => FocusRing.Visibility = Visibility.Visible;
        LostKeyboardFocus += (_, _) => FocusRing.Visibility = Visibility.Collapsed;
        Deactivated += (_, _) => FocusRing.Visibility = Visibility.Collapsed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StartupRegistration.RepairIfStale();
        // Starting Mini CloClo again while it runs just brings this copy back.
        Program.Instance?.ListenForShowRequests(() => Dispatcher.BeginInvoke(new Action(ShowBecauseStartedAgain)));
        // An install or uninstall asks it to exit before touching its files.
        Program.Instance?.ListenForExitRequests(() => Dispatcher.BeginInvoke(new Action(() => System.Windows.Application.Current.Shutdown())));
        ApplyDisplaySettings();
        PlaceWindow();
        SetupTrayIcon();
        UpdatePushToTalkHook();
        UpdateHotkeys();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Topmost = _settings.AlwaysOnTop;
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
        _topmostTimer.Start();

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += (_, _) => UpdateActivityState();
        _idleTimer.Start();

        var now = DateTime.Now;
        // Reminders already started keep their saved times (#39); new ones
        // start from their current slot.
        foreach (var reminder in _settings.CustomReminders) ReminderSchedule.Start(reminder, now);
        _lastChimeHour = now.Hour;
        _minuteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _minuteTimer.Tick += (_, _) => MinuteTick();
        _minuteTimer.Start();

        _lastPrayerCheckUtc = DateTimeOffset.UtcNow;
        _prayerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _prayerTimer.Tick += PrayerTimer_Tick;
        _prayerTimer.Start();
        UpdateNextPrayer();

        UpdateActivityState();
        // The speech model loads in the background now, so the first voice
        // command starts listening at once; it stays loaded until exit.
        _ = System.Threading.Tasks.Task.Run(LocalAudioTranscriber.PreloadModelAsync);
        if (_settings.SpeakGreeting)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(SpeakGreeting));
    }

    // ---- What the widget says ----

    private void SpeakGreeting()
    {
        if (_closing) return;
        var name = UserAccountName.GetDisplayName() ?? UserAccountName.GetUserName();
        var now = DateTime.Now;
        var english = (now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening") +
                      (name != null ? $", {name}." : ".") + $" It's {SpokenTime.English(now)}. " +
                      (_settings.PushToTalkEnabled ? "Hold the right mouse button to talk to me, or press Control Alt H for help."
                                                   : "Press Control Alt H for help.");
        var arabic = (now.Hour < 12 ? "صباح الخير" : "مساء الخير") + (name != null ? $" يا {name}." : ".") +
                     $" الساعة {SpokenTime.Arabic(now)}. " +
                     (_settings.PushToTalkEnabled ? "دوس كليك يمين مطول عشان تكلمني، أو كنترول ألت H للمساعدة."
                                                  : "دوس كنترول ألت H للمساعدة.");
        Voice.Say(english, arabic, () => Dispatcher.BeginInvoke(new Action(Keeper.Salute)));
    }

    internal void SayTime()
    {
        var now = DateTime.Now;
        ShowLabel(now.ToString("t"), "The time", "🕒");
        Voice.Say($"It's {SpokenTime.English(now)}.", $"الساعة {SpokenTime.Arabic(now)}.");
        Keeper.Perk();
    }

    internal void SayDate()
    {
        var now = DateTime.Now;
        ShowLabel(now.ToString("dddd d MMMM"), SpokenTime.HijriText(now), "📅");
        Voice.Say(SpokenTime.DateEnglish(now), SpokenTime.DateArabic(now));
        Keeper.Perk();
    }

    internal void SayNextPrayer()
    {
        var zone = PrayerTimeZone.Resolve(_settings.LocationTimeZoneId);
        if (NextPrayerAfter(DateTimeOffset.UtcNow) is not { } next)
        {
            Voice.Say("Prayer times are unavailable for this place. Choose your city under Prayer times.",
                "مواقيت الصلاة مش متاحة للمكان ده. اختار مدينتك من مواقيت الصلاة.");
            return;
        }
        var at = TimeZoneInfo.ConvertTime(next.StartsAtUtc, zone).DateTime;
        var left = next.StartsAtUtc - DateTimeOffset.UtcNow;
        ShowLabel($"{next.Name} {at:t}", $"In {SpokenTime.DurationEnglish(left)}", "🕌");
        var english = $"The next prayer is {next.Name}, at {SpokenTime.English(at)}, in {SpokenTime.DurationEnglish(left)}.";
        var arabic = $"الصلاة الجاية {SpokenTime.ArabicPrayerName(next.Name)} الساعة {SpokenTime.Arabic(at)}، بعد {SpokenTime.DurationArabic(left)}.";
        if (_prayerLookActive && _currentPrayer != null)
        {
            english = $"It's {_currentPrayer} time now. " + english;
            arabic = $"دلوقتي وقت صلاة {SpokenTime.ArabicPrayerName(_currentPrayer)}. " + arabic;
        }
        Voice.Say(english, arabic);
    }

    internal void SayPrayerTimes()
    {
        var zone = PrayerTimeZone.Resolve(_settings.LocationTimeZoneId);
        var times = PrayerTimesCalculator.ForDate(PrayerTimeZone.Today(_settings.LocationTimeZoneId),
            _settings.Latitude, _settings.Longitude, _settings.PrayerCalculationMethod, _settings.PrayerAsrMethod);
        if (times.Count == 0)
        {
            Voice.Say("Prayer times are unavailable for this place.", "مواقيت الصلاة مش متاحة للمكان ده.");
            return;
        }
        string Local(PrayerOccurrence p, bool arabic)
        {
            var at = TimeZoneInfo.ConvertTime(p.StartsAtUtc, zone).DateTime;
            return arabic ? SpokenTime.Arabic(at) : SpokenTime.English(at);
        }
        ShowLabel($"Prayer times in {_settings.City}",
            string.Join("  ", times.Select(p => $"{p.Name} {TimeZoneInfo.ConvertTime(p.StartsAtUtc, zone):t}")), "🕌");
        Voice.Say($"Prayer times in {_settings.City} today: " + string.Join(", ", times.Select(p => $"{p.Name} {Local(p, false)}")) + ".",
            "مواقيت الصلاة النهارده: " + string.Join("، ", times.Select(p => $"{SpokenTime.ArabicPrayerName(p.Name)} {Local(p, true)}")) + ".");
    }

    internal void SayHelp()
    {
        var keys = _hotkeysRegistered
            ? " Keyboard shortcuts, from any app, with Control and Alt: T the time, D the date, P the next prayer, A all prayer times, " +
              "Q the Quran, R reminders, K contacts and calls, I questions and answers, V talk, S stop sounds, M the menu, and H this help."
            : "";
        var keysArabic = _hotkeysRegistered
            ? " اختصارات لوحة المفاتيح مع كنترول وألت: T الساعة، D التاريخ، P الصلاة الجاية، A كل المواقيت، Q القرآن، R التذكيرات، K جهات الاتصال، I الأسئلة والأجوبة، V الكلام، S إيقاف الصوت، M القائمة، H المساعدة."
            : "";
        var talk = _settings.PushToTalkEnabled ? "Hold the right mouse button anywhere for a second" : "Press Control Alt V";
        var talkArabic = _settings.PushToTalkEnabled ? "دوس كليك يمين مطول في أي مكان" : "دوس كنترول ألت V";
        ShowLabel("Help", "Say: time, date, next prayer, Quran, reminders, stop", "❔");
        Voice.Say($"{talk}, wait for the short chime, then say a command: what time is it, the date, next prayer, prayer times, " +
                  "Quran, Quran surah, next, previous, reminders, call and a name, your own questions like Ramadan date, say it again, what topics do I have, pull dates, stop, I prayed, help, or open Notepad, Word, Calculator, Narrator, Magnifier or Settings, " +
                  "or shut down." + keys,
            $"{talkArabic}، استنى صوت التنبيه القصير، وبعدين قول أمر: الساعة كام، التاريخ، الصلاة الجاية، مواقيت الصلاة، " +
            "القرآن، القرآن سورة، اللي بعده، اللي قبله، التذكيرات، اتصل باسم، أسئلتك زي تاريخ رمضان، قولها تاني، مواضيعي، اسحب التواريخ، اسكت، صليت، مساعدة، أو افتح المفكرة، وورد، الآلة الحاسبة، الراوي، المكبر أو الإعدادات، " +
            "أو اقفل الجهاز." + keysArabic);
    }

    // Stops the azan, a reminder sound, a recitation and speech. True if
    // anything was playing.
    internal bool StopSounds()
    {
        var any = _azanPlayer?.IsPlaying == true || _reminderAudioPlayer?.IsPlaying == true ||
                  _quranWindow?.IsReciting == true || SpeechReader.IsSpeaking;
        // "Stop" during a reminder: what it paused doesn't carry on (#39).
        if (_announcing) _announcementStopped = true;
        Voice.Forget();
        _azanPlayer?.Stop();
        _reminderAudioPlayer?.Stop();
        _quranWindow?.StopRecitation();
        SpeechReader.Stop();
        ResumeVoiceAfterAudio();
        return any;
    }

    // ---- The label under the icons ----

    private void ShowLabel(string title, string? subtitle, string glyph = "🔔", int seconds = 15)
    {
        Keeper.SetNowPlaying(title, subtitle, glyph);
        _labelTimer ??= new DispatcherTimer();
        _labelTimer.Stop();
        _labelTimer.Interval = TimeSpan.FromSeconds(seconds);
        _labelTimer.Tick -= LabelTimer_Tick;
        _labelTimer.Tick += LabelTimer_Tick;
        _labelTimer.Start();
    }

    private void LabelTimer_Tick(object? sender, EventArgs e) => HideLabel();

    private void HideLabel()
    {
        _labelTimer?.Stop();
        Keeper.SetNowPlaying(null, null);
    }

    // ---- Prayer times ----

    private IEnumerable<PrayerOccurrence> PrayersAround(int days)
    {
        var locationDate = PrayerTimeZone.Today(_settings.LocationTimeZoneId);
        for (var day = -days; day <= days; day++)
            foreach (var prayer in PrayerTimesCalculator.ForDate(locationDate.AddDays(day),
                         _settings.Latitude, _settings.Longitude,
                         _settings.PrayerCalculationMethod, _settings.PrayerAsrMethod))
                yield return prayer;
    }

    private PrayerOccurrence? NextPrayerAfter(DateTimeOffset utc) =>
        PrayersAround(1).Where(p => p.StartsAtUtc > utc).OrderBy(p => p.StartsAtUtc).Cast<PrayerOccurrence?>().FirstOrDefault();

    private void UpdateNextPrayer()
    {
        var now = DateTimeOffset.UtcNow;
        var zone = PrayerTimeZone.Resolve(_settings.LocationTimeZoneId);
        var next = NextPrayerAfter(now);
        if (next is { } n)
        {
            var at = TimeZoneInfo.ConvertTime(n.StartsAtUtc, zone).DateTime;
            Keeper.SetNextPrayer($"Next: {n.Name} {at:t}",
                $"Next prayer {n.Name} at {SpokenTime.English(at)}, in {SpokenTime.DurationEnglish(n.StartsAtUtc - now)}");
        }
        else Keeper.SetNextPrayer(null, null);
        // The sky follows the clock shown on the widget, the PC's own: the
        // moon from sunset, the sun from sunrise, using the chosen city's
        // sunrise and sunset when it's in the PC's time zone. Otherwise the
        // city's sky would be the wrong one for the PC's time, so the widget
        // falls back to 6 AM - 6 PM by the PC's clock (null).
        var sameZone = zone.GetUtcOffset(now) == TimeZoneInfo.Local.GetUtcOffset(now);
        var sun = sameZone
            ? PrayerTimesCalculator.SunTimes(PrayerTimeZone.Today(_settings.LocationTimeZoneId), _settings.Latitude, _settings.Longitude)
            : null;
        Keeper.SetNight(sun is { } s ? now < s.Sunrise || now >= s.Sunset : null);
    }

    private void PrayerTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var previous = _lastPrayerCheckUtc;
        _lastPrayerCheckUtc = now;
        UpdateNextPrayer();
        // After sleep or a clock change, missed prayers aren't announced late.
        if (now - previous > TimeSpan.FromMinutes(2) || previous > now) return;

        var preAlert = TimeSpan.FromMinutes(_settings.PrayerPreAlertMinutes);
        foreach (var prayer in PrayersAround(2).OrderBy(p => p.StartsAtUtc))
        {
            if (preAlert > TimeSpan.Zero)
            {
                var alertAt = prayer.StartsAtUtc - preAlert;
                if (alertAt > previous && alertAt <= now && _alertedPrayerInstants.Add(prayer.StartsAtUtc.UtcTicks))
                    AnnounceUpcomingPrayer(prayer);
            }
            if (prayer.StartsAtUtc <= previous || prayer.StartsAtUtc > now ||
                !_playedPrayerInstants.Add(prayer.StartsAtUtc.UtcTicks)) continue;
            StartPrayerTime(prayer);
            break;
        }
        _playedPrayerInstants.RemoveWhere(ticks => ticks < now.AddDays(-3).UtcTicks);
        _alertedPrayerInstants.RemoveWhere(ticks => ticks < now.AddDays(-3).UtcTicks);
    }

    private void AnnounceUpcomingPrayer(PrayerOccurrence prayer)
    {
        if (_azanPlayer?.IsPlaying == true) return;
        var minutes = _settings.PrayerPreAlertMinutes;
        ShowLabel($"{prayer.Name} in {minutes} minutes", _settings.City, "🕌");
        Voice.Say($"{prayer.Name} prayer is in {minutes} minutes.",
            $"صلاة {SpokenTime.ArabicPrayerName(prayer.Name)} بعد {SpokenTime.DurationArabic(TimeSpan.FromMinutes(minutes))}.");
    }

    // At a prayer time: the prayer look, the azan recording (if chosen),
    // otherwise (or as well, before it) the spoken announcement.
    private void StartPrayerTime(PrayerOccurrence prayer)
    {
        _currentPrayer = prayer.Name;
        if (_settings.PrayerLookEnabled)
        {
            HideReminderLook();
            _prayerLookActive = true;
            UpdateActivityState();
        }
        ShowLabel($"{prayer.Name} prayer time", _settings.City, "🕌", seconds: 60);
        var azan = _settings.PrayerAzanEnabled ? AzanLibrary.Effective(_settings.PrayerAzanAudioPath) : null;
        if (azan != null)
        {
            _reminderAudioPlayer?.Stop();
            _quranWindow?.StopRecitation();
            SpeechReader.Stop();
            if (_azanPlayer == null)
            {
                _azanPlayer = new LocalAudioPlayer();
                _azanPlayer.Finished += ResumeVoiceAfterAudio;
            }
            if (_settings.PrayerAnnounce)
            {
                // Said first, then the azan follows.
                _ = AnnounceThenAzanAsync(prayer, azan);
                return;
            }
            if (_azanPlayer.TryPlay(azan)) PauseVoiceForAudio();
            return;
        }
        if (_settings.PrayerAnnounce) SayPrayerArrived(prayer);
    }

    private async System.Threading.Tasks.Task AnnounceThenAzanAsync(PrayerOccurrence prayer, string azan)
    {
        PauseVoiceForAudio();
        var (english, arabic) = PrayerArrivedText(prayer);
        await Voice.SayAndWaitAsync(english, arabic, TimeSpan.FromSeconds(10));
        if (_closing || _azanPlayer == null) return;
        if (!_azanPlayer.TryPlay(azan)) ResumeVoiceAfterAudio();
    }

    private (string English, string Arabic) PrayerArrivedText(PrayerOccurrence prayer) =>
        ($"It's time for the {prayer.Name} prayer.", $"حان الآن موعد صلاة {SpokenTime.ArabicPrayerName(prayer.Name)}.");

    private void SayPrayerArrived(PrayerOccurrence prayer)
    {
        var (english, arabic) = PrayerArrivedText(prayer);
        Voice.Say(english, arabic);
    }

    // From the check badge, a click on the head, the menu, a shortcut or
    // "I prayed": back to the everyday look, with a small nod.
    internal void MarkPrayed()
    {
        if (!_prayerLookActive)
        {
            Voice.Say("There is no prayer waiting to be marked.", "مفيش صلاة مستنية.");
            return;
        }
        _prayerLookActive = false;
        UpdateActivityState();
        Keeper.Perk();
        HideLabel();
        Voice.Say("May Allah accept your prayer.", "تقبل الله.");
    }

    // ---- Custom reminders, the hourly time and the Quran reminder ----

    // Every 20 seconds: what is due goes into the announcement queue
    // (MainWindow.Announcements.cs), which says it in turn - nothing is cut
    // off or dropped. Reminders keep coming while the avatar is asleep (no
    // keyboard or mouse use doesn't mean nobody is listening); during the
    // prayer look the queue holds them.
    private void MinuteTick()
    {
        var now = DateTime.Now;
        if (_settings.AnnounceHourly && now.Minute == 0 && now.Hour != _lastChimeHour)
        {
            _lastChimeHour = now.Hour;
            Announce(new Announcement(Announcement.Kinds.Hour, $"It's {SpokenTime.English(now)}", $"الساعة {SpokenTime.Arabic(now)}", now));
        }
        else if (now.Minute != 0) _lastChimeHour = -1;

        // On the clock, each reminder at its start hour plus whole intervals,
        // its last time saved (#39).
        var changed = false;
        foreach (var reminder in _settings.CustomReminders)
        {
            var (due, say, slot) = ReminderSchedule.Check(reminder, now);
            if (!due) continue;
            reminder.LastFired = slot;
            changed = true;
            if (!say) continue; // missed while the PC was asleep or off
            Announce(new Announcement(Announcement.Kinds.Reminder,
                reminder.Speak ? reminder.Message : "", reminder.Speak ? reminder.Message : "", now,
                reminder.PlayAudio ? reminder.AudioPath : null));
        }
        if (changed) _settings.Save();
        QuranReminderTick(now);
    }

    private void ReminderLookTimer_Tick(object? sender, EventArgs e) => HideReminderLook();

    private void HideReminderLook()
    {
        _reminderLookTimer?.Stop();
        Keeper.SetCustomReminderActive(false);
    }


    // The ayah being memorized, every so often if chosen in the Quran window
    // - asleep too, like the custom reminders, but not during the prayer look.
    private void QuranReminderTick(DateTime now)
    {
        if (_activityState == "prayer") { _lastQuranReminder = now; return; }
        _quranStore ??= QuranStore.Load();
        var minutes = _quranStore.ReminderMinutes;
        if (minutes <= 0 || now - _lastQuranReminder < TimeSpan.FromMinutes(minutes)) return;
        _lastQuranReminder = now;
        var (surah, ayah) = (_quranStore.Surah, _quranStore.Ayah);
        var info = QuranText.GetSurah(surah);
        // Said in turn by the announcement queue - not over a recitation.
        Announce(new Announcement(Announcement.Kinds.Quran, $"Time to review your Quran: {info.Name}, ayah {ayah}",
            $"وقت مراجعة الحفظ: سورة {info.ArabicName}، الآية {ayah}", now));
    }

    private void ShowQuranReminderLabel()
    {
        _quranStore ??= QuranStore.Load();
        var (surah, ayah) = (_quranStore.Surah, _quranStore.Ayah);
        var info = QuranText.GetSurah(surah);
        var text = QuranText.GetAyah(surah, ayah).Text;
        ShowLabel($"Quran · {info.Name} {surah}:{ayah}", text.Length > 70 ? text[..70] + "…" : text, "📗", seconds: 30);
    }

    // ---- The Quran window ----

    internal void OpenQuran() => OpenQuran(skipIntro: false);

    // skipIntro: the caller says what is opened (the choose-a-surah dialog).
    private void OpenQuran(bool skipIntro)
    {
        if (_quranWindow != null)
        {
            if (_quranWindow.WindowState == WindowState.Minimized) _quranWindow.WindowState = WindowState.Normal;
            _quranWindow.Activate();
            if (!skipIntro) _quranWindow.AnnouncePosition();
            return;
        }
        _quranStore ??= QuranStore.Load();
        var window = new QuranWindow(_quranStore) { Topmost = false, SkipIntro = skipIntro };
        window.RecitingChanged += on =>
        {
            Keeper.SetReading(on);
            if (on) PauseVoiceForAudio();
            else ResumeVoiceAfterAudio();
        };
        _quranWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_quranWindow, window)) _quranWindow = null;
            Keeper.SetReading(false);
            ResumeVoiceAfterAudio();
            Voice.Say("Quran window closed.", "اتقفلت شاشة القرآن.");
        };
        window.Show();
        window.Activate();
    }

    // "Quran" by voice: opens the window, or with it open starts or pauses
    // the recitation.
    private void QuranVoiceCommand(WidgetVoiceCommand command)
    {
        if (_quranWindow == null)
        {
            OpenQuran();
            return;
        }
        switch (command)
        {
            case WidgetVoiceCommand.QuranNext: _quranWindow.MemorizedNext(); break;
            case WidgetVoiceCommand.QuranPrevious: _quranWindow.Previous(); break;
            default: _quranWindow.TogglePlay(); break;
        }
    }

    // Nothing listens while the azan or a recitation plays: a hold in
    // progress is dropped and a new one waits until it ends.
    private bool CloCloSoundPlaying => _azanPlayer?.IsPlaying == true || _quranWindow?.IsReciting == true;

    private void PauseVoiceForAudio()
    {
        if (_voicePausedForAudio) return;
        _voicePausedForAudio = true;
        AbortPushToTalk();
    }

    private void ResumeVoiceAfterAudio()
    {
        if (!_voicePausedForAudio || CloCloSoundPlaying) return;
        _voicePausedForAudio = false;
    }

    // ---- The avatar's state ----

    // Prayer comes first, then sleep after the chosen idle time, otherwise
    // awake.
    private void UpdateActivityState()
    {
        string state;
        if (_prayerLookActive && _settings.PrayerLookEnabled) state = "prayer";
        else
        {
            _prayerLookActive = false;
            var sleepThreshold = _settings.SleepAfterMinutes;
            state = sleepThreshold > 0 && IdleDetection.TimeSinceLastInput() >= TimeSpan.FromMinutes(sleepThreshold)
                ? "asleep" : "awake";
        }
        if (state == _activityState) return;
        _activityState = state;
        Keeper.SetActivity(state);
    }

    // ---- Dialogs ----

    internal void OpenCustomReminders()
    {
        if (FocusOpenDialog()) return;
        var dialog = new CustomRemindersWindow(_settings.CustomReminders);
        if (ShowDialogWindow(dialog) != true)
        {
            Voice.Say("Reminders not changed.", "التذكيرات زي ما هي.");
            return;
        }
        _reminderAudioPlayer?.Stop();
        _settings.CustomReminders = dialog.Reminders.ToList();
        // Kept reminders keep their times; new or edited ones start from now (#39).
        var now = DateTime.Now;
        foreach (var reminder in _settings.CustomReminders) ReminderSchedule.Start(reminder, now);
        _settings.Save();
        var count = _settings.CustomReminders.Count(r => r.Enabled);
        Voice.Say(count == 1 ? "Reminders saved. 1 reminder is on." : $"Reminders saved. {count} reminders are on.",
            $"اتحفظت التذكيرات. شغال منها {count}.");
    }

    internal void OpenPrayerTimes()
    {
        if (FocusOpenDialog()) return;
        var dialog = new PrayerTimesWindow(_settings);
        if (ShowDialogWindow(dialog) != true) return;
        _settings.City = dialog.City;
        _settings.Latitude = WidgetSettings.RoundCoordinate(dialog.Latitude);
        _settings.Longitude = WidgetSettings.RoundCoordinate(dialog.Longitude);
        _settings.LocationTimeZoneId = dialog.TimeZoneId;
        _settings.PrayerCalculationMethod = dialog.CalculationMethod;
        _settings.PrayerAsrMethod = dialog.AsrMethod;
        _settings.PrayerAzanAudioPath = dialog.AudioPath;
        _settings.PrayerAzanEnabled = dialog.AzanEnabled;
        _settings.PrayerLookEnabled = dialog.PrayerLookEnabled;
        _settings.PrayerAnnounce = dialog.AnnounceEnabled;
        _settings.PrayerPreAlertMinutes = dialog.PreAlertMinutes;
        _settings.Save();
        UpdateActivityState(); // turning the prayer look off ends one in progress
        _lastPrayerCheckUtc = DateTimeOffset.UtcNow;
        _playedPrayerInstants.Clear();
        _alertedPrayerInstants.Clear();
        _azanPlayer?.Stop();
        ResumeVoiceAfterAudio();
        UpdateNextPrayer();
        Voice.Say($"Saved. Prayer times are for {_settings.City}.", $"اتحفظ. المواقيت لمدينة {_settings.City}.");
    }

    // One dialog at a time; asking again brings it back to the front.
    private bool FocusOpenDialog()
    {
        if (_openDialog == null) return false;
        _openDialog.Activate();
        return true;
    }

    private bool? ShowDialogWindow(Window dialog)
    {
        _openDialog = dialog;
        var resumeTopmost = _topmostTimer?.IsEnabled == true;
        _topmostTimer?.Stop();
        try
        {
            dialog.Topmost = _settings.AlwaysOnTop;
            return dialog.ShowDialog();
        }
        finally
        {
            _openDialog = null;
            if (resumeTopmost)
            {
                _topmostTimer?.Start();
                ReassertTopmost();
            }
        }
    }

    // ---- Mouse and keyboard on the widget ----

    // Where the pointer is, in stage units.
    private Point StagePoint(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var scale = StageScale * _settings.WidgetSize;
        return new Point(p.X / scale, p.Y / scale);
    }

    private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var p = StagePoint(e);
        if (e.ClickCount >= 2)
        {
            OpenMenu(fromTray: false, atMouse: true);
            return;
        }
        if (_activityState == "prayer" && (PrayedBadgeRect.Contains(p) || HeadRect.Contains(p)))
        {
            MarkPrayed();
            return;
        }
        if (PrayerIconRect.Contains(p)) { OpenPrayerTimes(); return; }
        if (QuranIconRect.Contains(p)) { OpenQuran(); return; }
        if (RemindersIconRect.Contains(p)) { OpenCustomReminders(); return; }
        if (MicIconRect.Contains(p)) { ToggleKeyboardVoiceCommand(); return; }
        if (CloseLabelRect.Contains(p)) { HideLabel(); return; }

        // Dragging moves the widget; a click that doesn't move it says the
        // time (on the head) or what the label shows.
        var before = new Point(Left, Top);
        try { DragMove(); } catch { /* button already released */ }
        if (Math.Abs(Left - before.X) > 2 || Math.Abs(Top - before.Y) > 2)
        {
            SaveWindowPosition();
            return;
        }
        if (HeadRect.Contains(p)) SayTime();
        else if (LabelRect.Contains(p) && Keeper.NowPlaying.Visibility == Visibility.Visible)
            Voice.Say(Keeper.NowPlayingTitle.Text + ". " + Keeper.NowPlayingSubtitle.Text);
    }

    private void Widget_KeyDown(object sender, KeyEventArgs e)
    {
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.T: SayTime(); break;
            case Key.D: SayDate(); break;
            case Key.P when shift:
            case Key.A: SayPrayerTimes(); break;
            case Key.P: SayNextPrayer(); break;
            case Key.Q when shift: StartChooseSurah(); break;
            case Key.Q: OpenQuran(); break;
            case Key.R: OpenCustomReminders(); break;
            case Key.K: OpenContacts(); break;
            case Key.I: OpenTopics(); break;
            case Key.V or Key.Space: ToggleKeyboardVoiceCommand(); break;
            case Key.S: if (!StopSounds()) Voice.Say("Nothing is playing.", "مفيش حاجة شغالة."); break;
            case Key.H or Key.F1: SayHelp(); break;
            case Key.M or Key.Apps: OpenMenu(); break;
            case Key.F10 when shift: OpenMenu(); break;
            case Key.Enter when _activityState == "prayer": MarkPrayed(); break;
            case Key.Escape: HideLabel(); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---- The menu (right-click, double-click, M, the tray icon or Ctrl+Alt+M) ----

    internal void OpenMenu() => OpenMenu(fromTray: false, atMouse: false);

    private void OpenMenu(bool fromTray, bool atMouse)
    {
        if (_activeContextMenu?.IsOpen == true) return;
        var menu = BuildMenu(fromTray);
        // The keyboard should land in the menu, wherever it was opened from.
        if (fromTray && _trayIcon != null) SetForegroundWindow(_trayIcon.Handle);
        else
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
            Activate();
        }
        menu.PlacementTarget = this;
        menu.Placement = atMouse || fromTray ? PlacementMode.MousePoint : PlacementMode.Center;
        _activeContextMenu = menu;
        menu.Opened += (_, _) =>
        {
            if (menu.Items.Count > 0 && menu.Items[0] is MenuItem first) first.Focus();
        };
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeContextMenu, menu)) _activeContextMenu = null;
        };
        menu.IsOpen = true;
    }

    private ContextMenu BuildMenu(bool fromTray)
    {
        var menu = new ContextMenu { FontSize = 14 * Math.Min(_settings.WidgetSize, 1.5) };
        System.Windows.Automation.AutomationProperties.SetName(menu, "Mini CloClo menu");
        if (_activityState == "prayer") menu.Items.Add(Item("_I've prayed", MarkPrayed, icon: ""));
        menu.Items.Add(Item("Say the _time", SayTime, icon: ""));
        menu.Items.Add(Item("Say the _date", SayDate, icon: ""));
        menu.Items.Add(Item("Say the _next prayer", SayNextPrayer, icon: ""));
        menu.Items.Add(Item("Say _all of today's prayer times", SayPrayerTimes));
        menu.Items.Add(Item("Talk to CloClo (_voice command)", ToggleKeyboardVoiceCommand, icon: ""));
        menu.Items.Add(Item("_Stop sounds", () => { if (!StopSounds()) Voice.Say("Nothing is playing.", "مفيش حاجة شغالة."); }, icon: ""));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("_Prayer times and azan…", OpenPrayerTimes, icon: ""));
        menu.Items.Add(Item("_Quran memorization…", OpenQuran, icon: ""));
        menu.Items.Add(Item("Choose a surah by voice…", StartChooseSurah));
        menu.Items.Add(Item("Custom _reminders…", OpenCustomReminders, icon: ""));
        menu.Items.Add(Item("_Contacts and calls…", OpenContacts, icon: ""));
        menu.Items.Add(Item("_Questions and answers…", OpenTopics, icon: ""));
        menu.Items.Add(Item("P_ull tools…", OpenPullTools, icon: "\uE896"));
        menu.Items.Add(new Separator());
        menu.Items.Add(SubMenu("Voice and _speech", "", new object[]
        {
            Item("_Hold right-click to talk", TogglePushToTalk, isChecked: _settings.PushToTalkEnabled),
            Item("Voice commands and replies in _Arabic", ToggleVoiceCommandsInArabic, isChecked: _settings.VoiceCommandsInArabic),
            Item("_Keyboard shortcuts (Ctrl+Alt)", ToggleKeyboardShortcuts, isChecked: _settings.KeyboardShortcutsEnabled),
            Item("Say the time every _hour", ToggleAnnounceHourly, isChecked: _settings.AnnounceHourly),
            Item("_Greeting at startup", ToggleSpeakGreeting, isChecked: _settings.SpeakGreeting),
            SubMenu("Speaking _speed", null, WidgetSettings.SpeechRateChoices.Select(rate => (object)Item(
                rate == 1 ? "Normal" : rate < 1 ? "Slower" : $"{rate:0.##} times faster",
                () => SetSpeechRate(rate), isChecked: _settings.SpeechRate == rate))),
        }));
        menu.Items.Add(SubMenu("_Look", "", new object[]
        {
            SubMenu("_Avatar", null, new object[]
            {
                Item("_Keeper", () => SetAvatar("keeper"), isChecked: _settings.Avatar == "keeper"),
                Item("_Lady Keeper", () => SetAvatar("lady"), isChecked: _settings.Avatar == "lady"),
            }),
            SubMenu("_Outfit colour", null, KeeperView.Outfits.Select(outfit => (object)Item(outfit.Label, () => SetOutfit(outfit.Key),
                isChecked: CurrentOutfit == outfit.Key))),
            SubMenu("_Size", null, WidgetSettings.SizeChoices.Select(size => (object)Item(
                size == 1 ? "Normal" : size == 1.5 ? "Large" : "Extra large", () => SetWidgetSize(size), isChecked: _settings.WidgetSize == size))),
            SubMenu("_Motion", null, new (string Value, string Label)[] { ("full", "Full motion"), ("gentle", "Gentle motion"), ("still", "Still") }
                .Select(opt => (object)Item(opt.Label, () => SetMotionMode(opt.Value), isChecked: _settings.MotionMode == opt.Value))),
            SubMenu("Sleep _after", null, new (int Minutes, string Label)[] { (5, "5 minutes"), (15, "15 minutes"), (30, "30 minutes"), (0, "Never") }
                .Select(opt => (object)Item(opt.Label, () => SetSleepAfter(opt.Minutes), isChecked: _settings.SleepAfterMinutes == opt.Minutes))),
        }));
        menu.Items.Add(Item("Always on _top", ToggleAlwaysOnTop, isChecked: _settings.AlwaysOnTop));
        menu.Items.Add(Item("Start with _Windows", ToggleStartWithWindows, isChecked: StartupRegistration.IsEnabled()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("_Help", SayHelp, icon: ""));
        if (fromTray)
            menu.Items.Add(Item(Visibility == Visibility.Visible ? "Hide the widget" : "Show the widget", ToggleVisible, icon: ""));
        menu.Items.Add(Item("E_xit", () => System.Windows.Application.Current.Shutdown(), icon: ""));
        return menu;
    }

    private static MenuItem Item(string header, Action? onClick, bool isChecked = false, bool enabled = true, string? icon = null)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked, IsEnabled = enabled, Icon = icon != null ? Glyph(icon) : null };
        // Screen readers hear the name without the access-key underscore.
        System.Windows.Automation.AutomationProperties.SetName(item, header.Replace("_", "").Replace("…", ""));
        if (onClick != null) item.Click += (_, _) => onClick();
        return item;
    }

    private static UIElement Glyph(string glyph) => new TextBlock
    {
        Text = glyph,
        FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static MenuItem SubMenu(string header, string? icon, IEnumerable<object> items)
    {
        var menu = Item(header, null, icon: icon);
        foreach (var item in items) menu.Items.Add(item);
        return menu;
    }

    // ---- Settings from the menu ----

    private void Confirm(bool on, string english, string arabic) =>
        Voice.Say($"{english} {(on ? "on" : "off")}.", $"{arabic} {(on ? "شغال" : "مقفول")}.");

    private void TogglePushToTalk()
    {
        _settings.PushToTalkEnabled = !_settings.PushToTalkEnabled;
        _settings.Save();
        if (!_settings.PushToTalkEnabled) CancelShutdownCountdown();
        UpdatePushToTalkHook();
        Confirm(_settings.PushToTalkEnabled, "Hold right-click to talk", "الكلام بالكليك اليمين");
    }

    private void ToggleVoiceCommandsInArabic()
    {
        _settings.VoiceCommandsInArabic = !_settings.VoiceCommandsInArabic;
        _settings.Save();
        Keeper.SetArabicDates(_settings.VoiceCommandsInArabic);
        if (_settings.VoiceCommandsInArabic && !SpeechReader.IsVoiceAvailable(ReadAloudVoice.ArabicHoda))
            Voice.Say("Voice commands are now in Arabic. No Arabic speaking voice is installed, so replies stay in English. " +
                      "Add the Arabic speech pack in Windows Settings, Time and language, Speech.");
        else Voice.Say("Voice commands in English.", "الأوامر الصوتية بالعربي.");
    }

    private void ToggleKeyboardShortcuts()
    {
        _settings.KeyboardShortcutsEnabled = !_settings.KeyboardShortcutsEnabled;
        _settings.Save();
        UpdateHotkeys();
        Confirm(_settings.KeyboardShortcutsEnabled, "Keyboard shortcuts", "اختصارات لوحة المفاتيح");
    }

    private void ToggleAnnounceHourly()
    {
        _settings.AnnounceHourly = !_settings.AnnounceHourly;
        _settings.Save();
        Confirm(_settings.AnnounceHourly, "Saying the time every hour", "الساعة كل ساعة");
    }

    private void ToggleSpeakGreeting()
    {
        _settings.SpeakGreeting = !_settings.SpeakGreeting;
        _settings.Save();
        Confirm(_settings.SpeakGreeting, "Greeting at startup", "التحية عند التشغيل");
    }

    private void SetSpeechRate(double rate)
    {
        _settings.SpeechRate = rate;
        _settings.Save();
        SpeechReader.Rate = rate;
        Voice.Say("This is the new speaking speed.", "ده السرعة الجديدة للكلام.");
    }

    private void SetAvatar(string avatar)
    {
        _settings.Avatar = avatar;
        _settings.Save();
        Keeper.SetAvatar(avatar);
        Keeper.SetOutfit(CurrentOutfit);
        Keeper.Perk();
    }

    private void SetOutfit(string outfit)
    {
        CurrentOutfit = outfit;
        _settings.Save();
        Keeper.SetOutfit(outfit);
    }

    private string CurrentOutfit
    {
        get => _settings.Avatar == "lady" ? _settings.LadyOutfit : _settings.KeeperOutfit;
        set
        {
            if (_settings.Avatar == "lady") _settings.LadyOutfit = value;
            else _settings.KeeperOutfit = value;
        }
    }

    private void SetWidgetSize(double size)
    {
        _settings.WidgetSize = size;
        _settings.Save();
        ApplyDisplaySettings();
        EnsureOnScreen();
        SaveWindowPosition();
    }

    private void SetMotionMode(string mode)
    {
        _settings.MotionMode = mode;
        _settings.Save();
        Keeper.SetMotionMode(mode);
    }

    private void SetSleepAfter(int minutes)
    {
        _settings.SleepAfterMinutes = minutes;
        _settings.Save();
        UpdateActivityState(); // e.g. picking "Never" wakes it right away
    }

    private void ToggleAlwaysOnTop()
    {
        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        Topmost = _settings.AlwaysOnTop;
        _settings.Save();
        Confirm(_settings.AlwaysOnTop, "Always on top", "دايمًا فوق");
    }

    private void ToggleStartWithWindows()
    {
        var on = !StartupRegistration.IsEnabled();
        StartupRegistration.SetEnabled(on);
        Confirm(on, "Start with Windows", "التشغيل مع ويندوز");
    }

    // ---- Window placement ----

    private void ApplyDisplaySettings()
    {
        var size = _settings.WidgetSize;
        Keeper.SetSize(size);
        Width = Keeper.Width;
        Height = Keeper.Height;
        Keeper.SetAvatar(_settings.Avatar);
        Keeper.SetOutfit(CurrentOutfit);
        Keeper.SetMotionMode(_settings.MotionMode);
        Keeper.SetArabicDates(_settings.VoiceCommandsInArabic);
    }

    private void ReassertTopmost()
    {
        if (!_settings.AlwaysOnTop || _activeContextMenu?.IsOpen == true) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }

    private void PlaceWindow()
    {
        // Only trusts the saved spot if it's still on a connected monitor.
        if (_settings.WindowLeft is double left && _settings.WindowTop is double top && IsOnAnyScreen(left, top))
        {
            Left = left;
            Top = top;
            return;
        }
        MoveToDefaultCorner();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(EnsureOnScreen));

    private void EnsureOnScreen()
    {
        if (IsOnAnyScreen(Left, Top)) return;
        MoveToDefaultCorner();
        SaveWindowPosition();
    }

    // Bottom-right, just above the taskbar clock.
    private void MoveToDefaultCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 16;
    }

    private bool IsOnAnyScreen(double left, double top)
    {
        // Left/Top are device-independent units; screen bounds are physical pixels.
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var rect = new Int32Rect(
            (int)Math.Round(left * dpi.DpiScaleX), (int)Math.Round(top * dpi.DpiScaleY),
            (int)Math.Round(Width * dpi.DpiScaleX), (int)Math.Round(Height * dpi.DpiScaleY));
        return NativeScreen.WorkingAreas().Any(area =>
        {
            var width = Math.Min(area.X + area.Width, rect.X + rect.Width) - Math.Max(area.X, rect.X);
            var height = Math.Min(area.Y + area.Height, rect.Y + rect.Height) - Math.Max(area.Y, rect.Y);
            return width >= MinVisiblePixels && height >= MinVisiblePixels;
        });
    }

    private void SaveWindowPosition()
    {
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
    }

    // ---- Tray icon ----

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon("Mini CloClo");
        _trayIcon.DoubleClick += ToggleVisible;
        _trayIcon.RightClick += () => OpenMenu(fromTray: true, atMouse: true);
        System.Windows.Application.Current.Exit += (_, _) => _trayIcon?.Dispose();
    }

    // A second copy was started (see SingleInstance.cs): show this one.
    private void ShowBecauseStartedAgain()
    {
        Visibility = Visibility.Visible;
        ShowLabel("Mini CloClo is already running", "This is it.");
        Voice.Say("Mini CloClo is already running.", "ميني كلوكلو شغال بالفعل.");
    }

    private void ToggleVisible()
    {
        if (Visibility == Visibility.Visible && _trayIcon?.IsAvailable != true) return;
        Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
        Voice.Say(Visibility == Visibility.Visible ? "Widget shown." : "Widget hidden. It keeps working, and the shortcuts still work.",
            Visibility == Visibility.Visible ? "الويدجت ظهر." : "الويدجت اختفى، بس لسه شغال.");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace MiniCloClo;

// Draws the avatar natively in WPF, with the looks Mini CloClo uses: the
// everyday outfit, night clothes while asleep, the reminder cardigan, prayer
// clothes on a prayer mat, an open mushaf while the Quran is recited, and the
// voice-command pin on the collar. Above it the time, below it the date, the
// Hijri date and the next prayer. The host calls the Set*/Perk methods.
public partial class KeeperView : System.Windows.Controls.UserControl
{
    // CSS's own ease-in-out / ease-out curves, so motion feels the same as
    // the full CloClo.
    private static readonly KeySpline EaseInOut = new(0.42, 0, 0.58, 1);
    private static readonly KeySpline EaseOut = new(0, 0, 0.58, 1);
    private const double LineWidth = 99;
    private const double StageWidth = 150, StageHeight = 292, BaseScale = 1.3;

    private readonly Random _random = new();
    private readonly Dictionary<string, List<(IAnimatable Target, DependencyProperty Property, AnimationTimeline Animation)>> _loops = new();

    private bool? _isNightOverride;
    private bool _isNight;
    private bool _arabicDates;
    private string _activityState = "awake";
    private bool _customReminderActive;
    private bool _reading;
    private string _avatar = "keeper";
    private string _outfit = "classic";
    private string _motionMode = "gentle";
    private readonly string? _shirtName;

    // Colour sets for the everyday outfit: the sweater, Keeper's trousers and
    // Lady Keeper's skirt. Night and prayer clothes keep their own.
    public static readonly (string Key, string Label, Color Top, Color Trousers, Color Skirt)[] Outfits =
    {
        ("classic", "Classic red", Rgb(0xb0483d), Rgb(0x46506a), Rgb(0x4b3f72)),
        ("ocean", "Ocean blue", Rgb(0x3d7cc0), Rgb(0x34405a), Rgb(0x27496d)),
        ("forest", "Forest green", Rgb(0x3f8a5a), Rgb(0x4a4436), Rgb(0x2f5a3e)),
        ("plum", "Plum", Rgb(0x8a4f9e), Rgb(0x3d3550), Rgb(0x3d3550)),
        ("mustard", "Mustard", Rgb(0xd09a2c), Rgb(0x4a3f35), Rgb(0x6b4a2f)),
        ("charcoal", "Charcoal", Rgb(0x4a4f58), Rgb(0x2a2d33), Rgb(0x7a2f3a)),
    };

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    // Set while a one-shot (perk, or a fidget that moves the whole body)
    // owns KeeperWrap's transform, so two never stomp each other.
    private bool _wrapBusy;
    // The time and dates, ticking on their own.
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DispatcherTimer? _blinkTimer;
    private DispatcherTimer? _blinkReopenTimer;
    private DispatcherTimer? _fidgetTimer;
    private DispatcherTimer? _startupSaluteTimer;
    private DispatcherTimer? _voiceResultTimer;
    private string _voiceCaptureState = "off";
    private string? _voiceResultState;

    public KeeperView()
    {
        InitializeComponent();
        _shirtName = UserAccountName.GetUserName();
        WorkoutCountdown.Text = _shirtName ?? string.Empty;
        WorkoutCountdown.Visibility = Show(!string.IsNullOrEmpty(_shirtName));

        BuildSunRays();
        BuildLoops();

        Unloaded += (_, _) =>
        {
            ClearStartupSalute();
            SetVoiceStatus("off");
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _clockTimer.Start();

        ApplySky();
        RestartAmbient();
        ScheduleBlink();
        ScheduleFidget();
    }

    // ---------------------------------------------------------------- host API

    // 1, 1.5 or 2 times the normal size.
    public void SetSize(double zoom)
    {
        StageScale.ScaleX = StageScale.ScaleY = BaseScale * zoom;
        Width = StageWidth * BaseScale * zoom;
        Height = StageHeight * BaseScale * zoom;
    }

    // The Hijri date in Arabic (with Arabic voice commands) or English.
    public void SetArabicDates(bool arabic)
    {
        _arabicDates = arabic;
        UpdateClock();
    }

    // Night from sunset to sunrise in the chosen city, as the host works it
    // out; otherwise (null) by the clock, 6 PM to 6 AM.
    public void SetNight(bool? night)
    {
        _isNightOverride = night;
        UpdateNight(DateTime.Now);
    }

    // "Next: Asr 3:12 PM", or null to hide the line.
    public void SetNextPrayer(string? text, string? spoken)
    {
        NextPrayerPlate.Visibility = Show(text != null);
        NextPrayer.Text = text ?? "";
        System.Windows.Automation.AutomationProperties.SetName(NextPrayer, spoken ?? text ?? "");
    }

    // Persistent local capture state with a two-second result overlay. The
    // overlay returns to the actual capture state, so a slow model restart
    // cannot make the pin claim it is listening before the microphone starts.
    public void SetVoiceStatus(string state)
    {
        if (state is "complete" or "failed")
        {
            _voiceResultState = state;
            StopVoiceResultTimer();
            _voiceResultTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _voiceResultTimer.Tick += VoiceResultTimer_Tick;
            _voiceResultTimer.Start();
        }
        else
        {
            _voiceCaptureState = state is "armed" or "processing" ? state : "off";
            if (_voiceCaptureState == "off")
            {
                StopVoiceResultTimer();
                _voiceResultState = null;
            }
        }
        RenderVoiceStatus();
    }

    private void VoiceResultTimer_Tick(object? sender, EventArgs e)
    {
        StopVoiceResultTimer();
        _voiceResultState = null;
        RenderVoiceStatus();
    }

    private void StopVoiceResultTimer()
    {
        if (_voiceResultTimer == null) return;
        _voiceResultTimer.Stop();
        _voiceResultTimer.Tick -= VoiceResultTimer_Tick;
        _voiceResultTimer = null;
    }

    private void RenderVoiceStatus()
    {
        var state = _voiceResultState ?? _voiceCaptureState;
        VoiceStatusPin.Visibility = Show(state != "off");
        VoiceMicGlyph.Visibility = Show(state == "armed");
        VoiceWorkingGlyph.Visibility = Show(state == "processing");
        VoiceDoneGlyph.Visibility = Show(state == "complete");
        VoiceFailedGlyph.Visibility = Show(state == "failed");
        VoicePinBody.Fill = state switch
        {
            "processing" or "failed" => Brushes.DarkOrange,
            "complete" => Brushes.ForestGreen,
            _ => Brushes.DodgerBlue,
        };
        // The icon bar's microphone lights up while listening.
        MicGlyph.Foreground = state == "armed" ? Brushes.DeepSkyBlue : (Brush)FindResource("WhiteText");

        VoicePinBody.BeginAnimation(UIElement.OpacityProperty, null);
        VoicePinBody.Opacity = 1;
        VoiceHeadRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        VoiceHeadRotate.Angle = 0;
        if (_motionMode == "still") return;
        if (state == "armed")
        {
            VoicePinBody.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1,
                _motionMode == "gentle" ? 0.8 : 0.55,
                TimeSpan.FromMilliseconds(_motionMode == "gentle" ? 1100 : 700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
            VoiceHeadRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, _motionMode == "gentle" ? 2 : 4, TimeSpan.FromMilliseconds(220)));
        }
        else if (state == "complete")
        {
            VoiceHeadRotate.BeginAnimation(RotateTransform.AngleProperty,
                OneShot(Keys(0.8, EaseInOut, (0, 0), (0.3, 5), (0.65, -2), (1, 0))));
        }
    }

    public void SetMotionMode(string mode)
    {
        var normalized = mode is "full" or "gentle" or "still" ? mode : "gentle";
        if (_motionMode == normalized) return;
        _motionMode = normalized;
        ClearStartupSalute();
        RenderVoiceStatus();
        StopIdleMotionTimers();
        _wrapBusy = false;
        ClearWrap();
        HeadRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        RightArmShift.BeginAnimation(TranslateTransform.XProperty, null);
        RightArmShift.BeginAnimation(TranslateTransform.YProperty, null);
        EyesOpen.Visibility = Show(!EyesResting);
        EyesClosed.Visibility = Show(EyesResting);
        foreach (var group in _loops.Keys.ToArray()) SetLoops(group, false);
        ApplySky();
        SetLoops("zzz", _activityState == "asleep");
        if (_motionMode != "still")
        {
            ScheduleBlink();
            ScheduleFidget();
        }
    }

    // One of Outfits' keys; anything else falls back to "classic".
    public void SetOutfit(string key)
    {
        _outfit = Array.Exists(Outfits, o => o.Key == key) ? key : "classic";
        ApplyOutfit();
    }

    // "keeper" or "lady": the same figure and poses, with long hair, lashes,
    // rose lips and a skirt for "lady".
    public void SetAvatar(string avatar)
    {
        _avatar = avatar == "lady" ? "lady" : "keeper";
        var lady = _avatar == "lady";
        LadyLashes.Visibility = Show(lady);
        var mouth = (Brush)FindResource(lady ? "Lips" : "Hair");
        MouthAwake.Stroke = MouthSleep.Stroke = MouthWorkout.Fill = mouth;
        ApplyOutfit();
    }

    // Null title hides the label.
    public void SetNowPlaying(string? title, string? subtitle, string glyph = "🔔")
    {
        if (string.IsNullOrEmpty(title))
        {
            NowPlayingTitle.Text = "";
            NowPlayingSubtitle.Text = "";
            FitLine(NowPlayingTitle, LineWidth);
            FitLine(NowPlayingSubtitle, LineWidth);
            SubtitleLine.Visibility = Visibility.Collapsed;
            NowPlaying.Visibility = Visibility.Collapsed;
            return;
        }

        NowPlayingNote.Text = glyph;
        NowPlayingTitle.Text = title;
        NowPlayingSubtitle.Text = subtitle ?? "";
        SubtitleLine.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        NowPlaying.Visibility = Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(NowPlaying,
            string.IsNullOrEmpty(subtitle) ? title : $"{title}. {subtitle}");

        // Only scroll a line if it actually overflows — a short one sits still.
        FitLine(NowPlayingTitle, LineWidth);
        FitLine(NowPlayingSubtitle, LineWidth);
    }

    public void SetCustomReminderActive(bool active)
    {
        if (_customReminderActive == active) return;
        _customReminderActive = active;
        // The cardigan's note pocket sits where the name is.
        WorkoutCountdown.Visibility = Show(!active && _activityState == "awake" && !string.IsNullOrEmpty(_shirtName));
        ApplyArms();
        ApplyOutfit();
    }

    // An open mushaf in both hands while a recitation plays.
    public void SetReading(bool reading)
    {
        if (_reading == reading) return;
        _reading = reading;
        ClearStartupSalute();
        ApplyArms();
        UpdateTimeDetails(DateTime.Now);
    }

    // "awake", "asleep" or "prayer" — decided host-side (see
    // UpdateActivityState in MainWindow.xaml.cs). Purely cosmetic.
    public void SetActivity(string state)
    {
        if (state == _activityState) return;
        _activityState = state;
        ClearStartupSalute();
        var sleeping = state == "asleep";
        // Prayer: eyes softly closed and a calm smile, standing still on the
        // mat with folded hands - no z's and no dimming, unlike sleep.
        var praying = state == "prayer";

        MouthAwake.Visibility = Show(!sleeping && !praying);
        MouthSleep.Visibility = Show(sleeping || praying);
        MouthWorkout.Visibility = Visibility.Collapsed;
        Zzz.Visibility = Show(sleeping);
        EyesOpen.Visibility = Show(!EyesResting);
        EyesClosed.Visibility = Show(EyesResting);
        PrayerMat.Visibility = PrayedBadge.Visibility = Show(praying);
        WorkoutCountdown.Visibility = Show(state == "awake" && !_customReminderActive && !string.IsNullOrEmpty(_shirtName));
        ApplyArms();
        ApplyOutfit();
        UpdateTimeDetails(DateTime.Now);

        SetLoops("zzz", sleeping);
        // Dims a touch once asleep — a resting cue that reads even out of the
        // corner of your eye.
        if (_motionMode == "full")
            Stage.BeginAnimation(OpacityProperty, new DoubleAnimation(sleeping ? 0.85 : 1, TimeSpan.FromSeconds(0.6))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        else
        {
            Stage.BeginAnimation(OpacityProperty, null);
            Stage.Opacity = sleeping ? 0.85 : 1;
        }
        RestartAmbient();
    }

    // A one-shot "noticed that" bounce.
    public void Perk()
    {
        if (_activityState != "awake" || _wrapBusy) return;
        RunWrapOneShot(0.5, EaseOut,
            (WrapTranslate, TranslateTransform.YProperty, new[] { (0.0, 0.0), (0.3, -6), (0.6, -2), (1, 0) }),
            (WrapScale, ScaleTransform.ScaleXProperty, new[] { (0.0, 1.0), (0.3, 1.04), (0.6, 1.01), (1, 1) }),
            (WrapScale, ScaleTransform.ScaleYProperty, new[] { (0.0, 1.0), (0.3, 1.04), (0.6, 1.01), (1, 1) }));
    }

    // ---------------------------------------------------------------- outfit

    // Which arms show: folded at prayer, holding the mushaf while reciting
    // (awake only), otherwise hanging.
    private void ApplyArms()
    {
        var praying = _activityState == "prayer";
        var reading = _reading && _activityState == "awake";
        PrayerArms.Visibility = Show(praying);
        QuranPose.Visibility = Show(reading);
        HangingArms.Visibility = Show(ArmsHang && !reading && SalutePose.Visibility != Visibility.Visible);
    }

    // What the avatar wears: the everyday outfit while awake, the reminder
    // cardigan during a reminder, night clothes while asleep and prayer
    // clothes at prayer time - for either avatar.
    private void ApplyOutfit()
    {
        var lady = _avatar == "lady";
        var sleep = _activityState == "asleep";
        var prayer = _activityState == "prayer";
        var reminder = _customReminderActive && !sleep && !prayer;
        var everyday = !sleep && !prayer && !reminder;

        foreach (var el in new UIElement[] { ReminderTop, PajamaTop, Nightgown, Nightcap, PrayerRobe, PrayerDress, Kufi, Hijab })
            el.Visibility = Visibility.Collapsed;
        TorsoDefault.Visibility = Show(everyday);
        LadySkirt.Visibility = Show(lady && everyday);
        // The hijab covers Lady Keeper's hair at prayer time.
        HairDefault.Visibility = Show(!lady);
        LadyHairBack.Visibility = LadyHairFront.Visibility = Show(lady && !prayer);

        string arms, legs, shoes;
        if (prayer)
        {
            // Bare feet on the mat; sleeves edged so folded arms read on the robe.
            (lady ? PrayerDress : PrayerRobe).Visibility = Visibility.Visible;
            (lady ? (UIElement)Hijab : Kufi).Visibility = Visibility.Visible;
            var clothes = lady ? "PrayerDress" : "PrayerRobe";
            (arms, legs, shoes) = (clothes, clothes, "Skin");
            PrayerArmLEdge.Stroke = PrayerArmREdge.Stroke = (Brush)FindResource(clothes + "Edge");
        }
        else if (sleep)
        {
            Nightcap.Visibility = Visibility.Visible;
            NightcapBody.Fill = (Brush)FindResource(lady ? "Nightgown" : "Pajama");
            (lady ? Nightgown : PajamaTop).Visibility = Visibility.Visible;
            (arms, legs, shoes) = (lady ? "Nightgown" : "Pajama", lady ? "Skin" : "Pajama", "Slippers");
        }
        else if (reminder)
        {
            ReminderTop.Visibility = Visibility.Visible;
            (arms, legs, shoes) = ("ReminderTop", "Trousers", "Shoes");
        }
        else
        {
            (arms, legs, shoes) = ("Sweater", "Trousers", "Shoes");
        }

        // The everyday outfit's colours come from the chosen set.
        var set = Array.Find(Outfits, o => o.Key == _outfit);
        Brush Paint(string key) => key switch
        {
            "Sweater" => new SolidColorBrush(set.Top),
            "Trousers" => new SolidColorBrush(set.Trousers),
            _ => (Brush)FindResource(key),
        };
        SweaterBody.Fill = Paint("Sweater");
        LadySkirt.Fill = new SolidColorBrush(set.Skirt);

        var armBrush = Paint(arms);
        ArmL.Stroke = ArmR.Stroke = PrayerArmL.Stroke = PrayerArmR.Stroke = armBrush;
        LegL.Fill = LegR.Fill = Paint(legs);
        ShoeL.Fill = ShoeR.Fill = (Brush)FindResource(shoes);
    }

    // ---------------------------------------------------------------- clock

    // The time in Windows' own short format (12- or 24-hour); the date as
    // "Tue 20 Oct 2026"; the Hijri date underneath.
    private void UpdateClock()
    {
        var now = DateTime.Now;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var time = now.ToString("t", culture);
        var date = now.ToString("ddd d MMM yyyy", culture);
        var hijri = _arabicDates ? SpokenTime.HijriTextArabic(now) : SpokenTime.HijriText(now);
        if (ClockTime.Text != time)
        {
            ClockTime.Text = time;
            System.Windows.Automation.AutomationProperties.SetName(ClockTime, "Time " + SpokenTime.English(now));
        }
        if (ClockDate.Text != date)
        {
            ClockDate.Text = date;
            System.Windows.Automation.AutomationProperties.SetName(ClockDate, now.ToString("dddd, MMMM d, yyyy", culture));
        }
        if (HijriDate.Text != hijri)
        {
            HijriDate.Text = hijri;
            HijriDate.FlowDirection = _arabicDates ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            HijriPlate.Visibility = Show(hijri.Length > 0);
        }
        UpdateTimeDetails(now);
        UpdateNight(now);
    }

    private void UpdateNight(DateTime now)
    {
        var night = _isNightOverride ?? (now.Hour < 6 || now.Hour >= 18);
        if (night == _isNight && (Sun.Visibility == Visibility.Visible || Moon.Visibility == Visibility.Visible)) return;
        _isNight = night;
        ApplySky();
    }

    // Briefly raise a hand when the startup greeting begins. Still mode
    // holds a static salute and returns automatically.
    public void Salute()
    {
        if (_activityState != "awake" || _reading) return;
        ClearStartupSalute();
        HangingArms.Visibility = Visibility.Collapsed;
        SalutePose.Visibility = Visibility.Visible;
        _startupSaluteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_motionMode == "still" ? 2.5 : 2.1) };
        _startupSaluteTimer.Tick += StartupSaluteTimer_Tick;
        _startupSaluteTimer.Start();

        if (_motionMode == "still") return;
        var animation = Keys(2.1, null, (0.0, 27), (0.2, 0), (0.78, 0), (1.0, 27));
        animation.Completed += (_, _) => ClearStartupSalute();
        SaluteArmShift.BeginAnimation(TranslateTransform.YProperty, OneShot(animation));
        SaluteArmWave.BeginAnimation(RotateTransform.AngleProperty,
            OneShot(Keys(2.1, null, (0.0, 0), (0.2, 0), (0.31, -7), (0.42, 7), (0.53, -7), (0.64, 7), (0.76, 0), (1.0, 0))));
    }

    private void StartupSaluteTimer_Tick(object? sender, EventArgs e) => ClearStartupSalute();

    private void ClearStartupSalute()
    {
        if (_startupSaluteTimer != null)
        {
            _startupSaluteTimer.Stop();
            _startupSaluteTimer.Tick -= StartupSaluteTimer_Tick;
            _startupSaluteTimer = null;
        }
        SaluteArmShift.BeginAnimation(TranslateTransform.YProperty, null);
        SaluteArmWave.BeginAnimation(RotateTransform.AngleProperty, null);
        SalutePose.Visibility = Visibility.Collapsed;
        ApplyArms();
    }

    // The prayer pose replaces the hanging arms.
    private bool ArmsHang => _activityState != "prayer";

    // Asleep and at prayer the eyes stay closed, so blinks leave them be.
    private bool EyesResting => _activityState is "asleep" or "prayer";

    // A morning mug and an evening book, only while awake and not reciting.
    private void UpdateTimeDetails(DateTime now)
    {
        var awake = _activityState == "awake" && !_reading;
        MorningMug.Visibility = Show(awake && now.Hour >= 7 && now.Hour < 11);
        EveningBook.Visibility = Show(awake && now.Hour >= 19 && now.Hour < 22);
    }

    // ---------------------------------------------------------------- sky

    private void ApplySky()
    {
        Sun.Visibility = Show(!_isNight);
        Moon.Visibility = Show(_isNight);
        SetLoops("moon", _isNight);
    }

    private void BuildSunRays()
    {
        var highlight = (Brush)FindResource("Highlight");
        for (var i = 0; i < 8; i++)
        {
            var ray = new Rectangle
            {
                Width = 2, Height = 6, RadiusX = 1, RadiusY = 1, Fill = highlight, Opacity = 0.85,
                RenderTransform = new RotateTransform(360.0 / 8 * i, 1, 13),
            };
            Canvas.SetLeft(ray, 12);
            Canvas.SetTop(ray, 12);
            SunRays.Children.Add(ray);
        }
    }

    private void BuildLoops()
    {
        AddLoop("moon", Star1, OpacityProperty, Loop(Keys(2.4, EaseInOut, (0, 0.25), (0.5, 1), (1, 0.25)), 0));
        AddLoop("moon", Star2, OpacityProperty, Loop(Keys(2.4, EaseInOut, (0, 0.25), (0.5, 1), (1, 0.25)), 1.1));
        foreach (var (z, delay) in new[] { (Z1, 0.0), (Z2, 0.4) })
        {
            AddLoop("zzz", (TranslateTransform)z.RenderTransform, TranslateTransform.YProperty, Loop(Keys(2.6, EaseInOut, (0, 0), (0.5, -3), (1, 0)), delay));
            AddLoop("zzz", z, OpacityProperty, Loop(Keys(2.6, EaseInOut, (0, 0.55), (0.5, 0.9), (1, 0.55)), delay));
        }
    }

    // ---------------------------------------------------------------- motion

    // The always-on motion: a gentle breath (slower asleep, slow and shallow
    // at prayer).
    private void RestartAmbient()
    {
        if (_wrapBusy) return;
        ClearWrap();
        if (_motionMode == "still") return;
        var praying = _activityState == "prayer";
        var period = _activityState switch { "asleep" => 5.5, "prayer" => 6, _ => 3.2 };
        if (_motionMode == "gentle") period = Math.Max(period, 6.5);
        var lift = _motionMode == "gentle" ? -0.8 : praying ? -1.2 : -3;
        WrapTranslate.BeginAnimation(TranslateTransform.YProperty, Loop(Keys(period, EaseInOut, (0, 0), (0.5, lift), (1, 0)), 0));
    }

    private void ClearWrap()
    {
        WrapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        WrapTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        WrapRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        WrapScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        WrapScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private void RunWrapOneShot(double seconds, KeySpline spline, params (IAnimatable Target, DependencyProperty Property, (double, double)[] Frames)[] parts)
    {
        if (_motionMode == "still") return;
        if (_motionMode == "gentle") seconds *= 1.5;
        _wrapBusy = true;
        ClearWrap();
        var anims = parts.Select(p => OneShot(Keys(seconds, spline, p.Frames))).ToList();
        // Must be attached before BeginAnimation — the clock copies the
        // timeline's handlers when it's created.
        anims[^1].Completed += (_, _) => { _wrapBusy = false; RestartAmbient(); };
        for (var i = 0; i < parts.Length; i++) parts[i].Target.BeginAnimation(parts[i].Property, anims[i]);
    }

    private void ScheduleBlink()
    {
        if (_motionMode == "still") return;
        _blinkTimer?.Stop();
        var gentle = _motionMode == "gentle";
        var timer = _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(gentle ? 9000 + _random.NextDouble() * 6000 : 2400 + _random.NextDouble() * 2800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_motionMode == "still") return;
            if (!EyesResting)
            {
                EyesOpen.Visibility = Visibility.Collapsed;
                EyesClosed.Visibility = Visibility.Visible;
            }
            var reopen = _blinkReopenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            reopen.Tick += (_, _) =>
            {
                reopen.Stop();
                if (_motionMode == "still") return;
                if (!EyesResting)
                {
                    EyesOpen.Visibility = Visibility.Visible;
                    EyesClosed.Visibility = Visibility.Collapsed;
                }
                ScheduleBlink();
            };
            reopen.Start();
        };
        timer.Start();
    }

    // Idle fidgets — rare, random micro-animations while just standing there.
    private void ScheduleFidget()
    {
        if (_motionMode == "still") return;
        var gentle = _motionMode == "gentle";
        var timer = _fidgetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(gentle ? 55000 + _random.NextDouble() * 35000 : 15000 + _random.NextDouble() * 25000) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_motionMode != "still" && _activityState == "awake" && !_wrapBusy && !_reading)
                PlayFidget(gentle ? _random.Next(2, 5) : _random.Next(5));
            ScheduleFidget();
        };
        timer.Start();
    }

    private void StopIdleMotionTimers()
    {
        _blinkTimer?.Stop();
        _blinkTimer = null;
        _blinkReopenTimer?.Stop();
        _blinkReopenTimer = null;
        _fidgetTimer?.Stop();
        _fidgetTimer = null;
    }

    private void PlayFidget(int which)
    {
        switch (which)
        {
            case 0: // lean
                RunWrapOneShot(1.1, EaseInOut, (WrapRotate, RotateTransform.AngleProperty, new[] { (0.0, 0.0), (0.5, -2), (1, 0) }));
                break;
            case 1: // squish
                RunWrapOneShot(0.9, EaseInOut, (WrapScale, ScaleTransform.ScaleYProperty, new[] { (0.0, 1.0), (0.5, 0.97), (1, 1) }));
                break;
            case 2: // head tilt — doesn't lock the body
                HeadRotate.BeginAnimation(RotateTransform.AngleProperty, OneShot(Keys(1.3, EaseInOut, (0, 0), (0.5, 4), (1, 0))));
                break;
            case 3: // weight shift
                RunWrapOneShot(1.2, EaseInOut, (WrapTranslate, TranslateTransform.XProperty, new[] { (0.0, 0.0), (0.5, 2.5), (1, 0) }));
                break;
            default: // quick tug at the sleeve — arm and hand move as one
                RightArmShift.BeginAnimation(TranslateTransform.XProperty, OneShot(Keys(0.8, EaseInOut, (0, 0), (0.4, 1), (0.75, -0.5), (1, 0))));
                RightArmShift.BeginAnimation(TranslateTransform.YProperty, OneShot(Keys(0.8, EaseInOut, (0, 0), (0.4, -1.5), (0.75, 0), (1, 0))));
                break;
        }
    }

    // ---------------------------------------------------------------- helpers

    private void FitLine(TextBlock text, double lineWidth)
    {
        var shift = (TranslateTransform)text.RenderTransform;
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.X = 0;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = text.DesiredSize.Width;
        if (width <= lineWidth || _motionMode == "still") return;
        // Scrolls slowly from right to left, then starts again.
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(lineWidth, -width, TimeSpan.FromSeconds(Math.Max(8, width / 12)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    private void AddLoop(string group, IAnimatable target, DependencyProperty property, AnimationTimeline animation)
    {
        if (!_loops.TryGetValue(group, out var list)) _loops[group] = list = new();
        list.Add((target, property, animation));
    }

    // Starts or stops one decoration's animations.
    private void SetLoops(string group, bool on)
    {
        if (!_loops.TryGetValue(group, out var list)) return;
        foreach (var (target, property, animation) in list)
            target.BeginAnimation(property, on && _motionMode == "full" ? animation : null);
    }

    private static DoubleAnimationUsingKeyFrames Keys(double seconds, KeySpline? spline, params (double Time, double Value)[] frames)
    {
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds) };
        foreach (var (time, value) in frames)
        {
            anim.KeyFrames.Add(spline == null
                ? new LinearDoubleKeyFrame(value, KeyTime.FromPercent(time))
                : new SplineDoubleKeyFrame(value, KeyTime.FromPercent(time), spline));
        }
        return anim;
    }

    private static AnimationTimeline Loop(DoubleAnimationUsingKeyFrames anim, double delaySeconds)
    {
        anim.RepeatBehavior = RepeatBehavior.Forever;
        anim.BeginTime = TimeSpan.FromSeconds(delaySeconds);
        anim.Freeze();
        return anim;
    }

    private static AnimationTimeline OneShot(DoubleAnimationUsingKeyFrames anim)
    {
        anim.FillBehavior = FillBehavior.Stop;
        return anim;
    }

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
}

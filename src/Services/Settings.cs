using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MiniCloClo;

public class WidgetSettings
{
    private const int MaxSettingsFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions LoadOptions = new() { MaxDepth = 16 };
    private static readonly HashSet<int> AllowedSleepIntervals = new() { 0, 5, 15, 30 };
    public static readonly int[] PreAlertChoices = { 0, 5, 10, 15, 30 };
    public static readonly double[] SpeechRateChoices = { 0.75, 1.0, 1.25, 1.5, 2.0 };
    public static readonly double[] SizeChoices = { 1.0, 1.5, 2.0 };
    public const int MaxCustomReminders = 10;

    // The place used for prayer times only. Picked from the offline city list
    // (OfflineCities) or typed in as coordinates - nothing is looked up online
    // and the PC's location is never read.
    public string City { get; set; } = "Cairo";
    public double Latitude { get; set; } = 30.04;
    public double Longitude { get; set; } = 31.24;
    public string? LocationTimeZoneId { get; set; } = "Egypt Standard Time";
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    // How large the widget is drawn: 1, 1.5 or 2 times its normal size.
    public double WidgetSize { get; set; } = 1.5;
    // How long with no keyboard/mouse input before the avatar falls asleep;
    // 0 means never.
    public int SleepAfterMinutes { get; set; } = 15;
    public List<CustomReminderSettings> CustomReminders { get; set; } = new();
    public string PrayerCalculationMethod { get; set; } = "egyptian";
    public string PrayerAsrMethod { get; set; } = "standard";
    // The azan is a local MP3/WAV the person chooses; without one, the prayer
    // time is still spoken (PrayerAnnounce).
    public bool PrayerAzanEnabled { get; set; }
    public string? PrayerAzanAudioPath { get; set; }
    // Says "It's time for the Asr prayer" at each prayer time.
    public bool PrayerAnnounce { get; set; } = true;
    // Says "Asr is in 10 minutes" ahead of each prayer; 0 = off.
    public int PrayerPreAlertMinutes { get; set; } = 10;
    // The avatar changes into prayer clothes on a prayer mat at each prayer
    // time, until "I've prayed".
    public bool PrayerLookEnabled { get; set; } = true;
    // Speaks a short welcome using the local account name on startup.
    public bool SpeakGreeting { get; set; } = true;
    // Speaks the time on every full hour.
    public bool AnnounceHourly { get; set; }
    // Hold the right mouse button anywhere for a second to give a voice
    // command; releasing it runs the command. On by default here, since this
    // build is meant to be used by voice. Audio is never saved.
    public bool PushToTalkEnabled { get; set; } = true;
    // Ctrl+Alt shortcuts that work from any app (see MainWindow.Hotkeys.cs).
    public bool KeyboardShortcutsEnabled { get; set; } = true;
    // Steers voice-command recognition toward Arabic speech, and answers in
    // Arabic when an Arabic voice is installed.
    public bool VoiceCommandsInArabic { get; set; }
    // Windows' speaking rate for everything CloClo says (1 = normal).
    public double SpeechRate { get; set; } = 1.0;
    public string Avatar { get; set; } = "keeper";
    public string KeeperOutfit { get; set; } = "classic";
    public string LadyOutfit { get; set; } = "classic";
    public string MotionMode { get; set; } = "gentle";

    public static double RoundCoordinate(double value) => Math.Round(value, 2);

    internal static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniCloClo");

    private static string FilePath => Path.Combine(FolderPath, "settings.json");

    public static WidgetSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<WidgetSettings>(ReadSettingsBytes(), LoadOptions);
                if (loaded != null) return loaded.ValidateAndNormalize();
            }
        }
        catch
        {
            // Corrupt or unreadable settings file: fall back to defaults below.
        }
        return new WidgetSettings();
    }

    private static byte[] ReadSettingsBytes()
    {
        // Read at most the limit plus one byte; checking FileInfo.Length alone
        // would allow a file that grows after the check to use unbounded memory.
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[MaxSettingsFileBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = stream.Read(buffer, count, buffer.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count > MaxSettingsFileBytes || stream.ReadByte() != -1)
            throw new InvalidDataException("Settings file exceeds the size limit.");
        Array.Resize(ref buffer, count);
        return buffer;
    }

    private WidgetSettings ValidateAndNormalize()
    {
        City = IsSafeText(City, 100) ? City.Trim() : "Cairo";
        Latitude = IsValidCoordinate(Latitude, -90, 90) ? RoundCoordinate(Latitude) : 30.04;
        Longitude = IsValidCoordinate(Longitude, -180, 180) ? RoundCoordinate(Longitude) : 31.24;
        LocationTimeZoneId = PrayerTimeZone.IsValidId(LocationTimeZoneId) ? LocationTimeZoneId : null;
        WindowLeft = IsValidWindowCoordinate(WindowLeft) ? WindowLeft : null;
        WindowTop = IsValidWindowCoordinate(WindowTop) ? WindowTop : null;
        WidgetSize = SizeChoices.Contains(WidgetSize) ? WidgetSize : 1.5;
        Avatar = Avatar is "keeper" or "lady" ? Avatar : "keeper";
        MotionMode = MotionMode is "full" or "gentle" or "still" ? MotionMode : "gentle";
        KeeperOutfit = IsAllowedOutfit(KeeperOutfit) ? KeeperOutfit : "classic";
        LadyOutfit = IsAllowedOutfit(LadyOutfit) ? LadyOutfit : "classic";
        SleepAfterMinutes = AllowedSleepIntervals.Contains(SleepAfterMinutes) ? SleepAfterMinutes : 15;
        SpeechRate = SpeechRateChoices.Contains(SpeechRate) ? SpeechRate : 1.0;
        PrayerPreAlertMinutes = PreAlertChoices.Contains(PrayerPreAlertMinutes) ? PrayerPreAlertMinutes : 10;
        CustomReminders = (CustomReminders ?? new List<CustomReminderSettings>())
            .Take(MaxCustomReminders)
            .Select(reminder => reminder?.ValidateAndNormalize())
            .Where(reminder => reminder != null)
            .Cast<CustomReminderSettings>()
            .ToList();
        PrayerCalculationMethod = PrayerCalculationMethod is "isna" or "mwl" or "egyptian" or "karachi" or "makkah"
            ? PrayerCalculationMethod : "egyptian";
        PrayerAsrMethod = PrayerAsrMethod is "standard" or "hanafi" ? PrayerAsrMethod : "standard";
        if (!LocalAudioPlayer.IsAllowedLocalAudioFile(PrayerAzanAudioPath))
            PrayerAzanAudioPath = null;
        return this;
    }

    private static bool IsSafeText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Any(char.IsControl);

    private static bool IsValidCoordinate(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    private static bool IsValidWindowCoordinate(double? value) =>
        value is null || (double.IsFinite(value.Value) && Math.Abs(value.Value) <= 1_000_000);

    private static bool IsAllowedOutfit(string? value) =>
        value is not null && KeeperView.Outfits.Any(outfit => outfit.Key == value);

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            // Written beside and then swapped in, so a crash mid-write can
            // never leave a half-written file. The temporary file gets a
            // random name and must not exist yet (CreateNew), so nothing can
            // pre-create it as a link to redirect this write.
            var temp = Path.Combine(FolderPath, "settings." + Path.GetRandomFileName() + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                }
                File.Move(temp, FilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        catch
        {
            // Best-effort persistence; losing a save isn't fatal.
        }
    }
}

public sealed class CustomReminderSettings
{
    public static readonly int[] IntervalChoices = { 15, 30, 45, 60, 90, 120, 180, 240 };

    public string Message { get; set; } = "Drink a glass of water.";
    public int IntervalMinutes { get; set; } = 60;
    public int StartHour { get; set; } = 9;
    public int EndHour { get; set; } = 21;
    // Spoken by default: this build is for people who may not see the label.
    public bool Speak { get; set; } = true;
    public bool PlayAudio { get; set; }
    public string? AudioPath { get; set; }
    public bool Enabled { get; set; } = true;

    public CustomReminderSettings ValidateAndNormalize()
    {
        if (string.IsNullOrWhiteSpace(Message) || Message.Length > 120 || Message.Any(char.IsControl))
            Message = "Drink a glass of water.";
        else
            Message = Message.Trim();

        if (!IntervalChoices.Contains(IntervalMinutes)) IntervalMinutes = 60;
        if (StartHour is < 0 or > 23) StartHour = 9;
        if (EndHour is < 0 or > 23 || EndHour == StartHour) EndHour = StartHour == 21 ? 9 : 21;
        if (!LocalAudioPlayer.IsAllowedLocalAudioFile(AudioPath))
        {
            AudioPath = null;
            PlayAudio = false;
        }
        return this;
    }

    public CustomReminderSettings Copy() => new()
    {
        Message = Message,
        IntervalMinutes = IntervalMinutes,
        StartHour = StartHour,
        EndHour = EndHour,
        Speak = Speak,
        PlayAudio = PlayAudio,
        AudioPath = AudioPath,
        Enabled = Enabled,
    };

    public static string IntervalLabel(int minutes) =>
        minutes < 60 ? $"{minutes} minutes" : minutes == 60 ? "1 hour" : minutes % 60 == 0 ? $"{minutes / 60} hours" : $"{minutes / 60.0:0.#} hours";

    // What a screen reader says for the reminder in the list.
    [System.Text.Json.Serialization.JsonIgnore]
    public string Spoken => $"{Message}. {Describe()}";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Description => Describe();

    public string Describe() =>
        $"Every {IntervalLabel(IntervalMinutes)}, from {StartHour:00}:00 to {EndHour:00}:00, " +
        (PlayAudio ? "plays a sound, " : "") + (Speak ? "spoken" : "shown only") +
        $", {(Enabled ? "on" : "off")}";
}

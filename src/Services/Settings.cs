using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    // The azan plays at each prayer time: the person's own MP3/WAV if one is
    // chosen, otherwise the built-in recording (AzanLibrary).
    public bool PrayerAzanEnabled { get; set; } = true;
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
    // The shortcuts turned off one by one (their letters), e.g. one another
    // app needs; the rest keep working.
    public List<string> ShortcutsOff { get; set; } = new();
    // Steers voice-command recognition toward Arabic speech, and answers in
    // Arabic when an Arabic voice is installed.
    public bool VoiceCommandsInArabic { get; set; }
    // Windows' speaking rate for everything CloClo says (1 = normal).
    public double SpeechRate { get; set; } = 1.0;
    public string Avatar { get; set; } = "keeper";
    public string KeeperOutfit { get; set; } = "classic";
    public string LadyOutfit { get; set; } = "classic";
    public string MotionMode { get; set; } = "gentle";
    // People to call by voice ("call Mom"): only these numbers are ever
    // dialed. Kept on this PC only.
    public List<ContactSettings> Contacts { get; set; } = new();
    public const int MaxContacts = 50;
    // The paired phone that places calls (BluetoothDialer): Windows' id for
    // it and its name as shown in Settings. Null until chosen or found.
    public string? PhoneDeviceId { get; set; }
    public string? PhoneName { get; set; }
    // The person's own questions and answers, by topic (Topics.cs). The
    // Islamic dates are added once, the first time; after that, what the
    // person keeps or removes stays that way.
    public List<TopicSettings> Topics { get; set; } = new();
    public bool TopicsSeeded { get; set; }
    // "Pull" (#35): off until turned on, since the tools go online; and the
    // person's own values for each tool's settings, by tool folder name.
    public bool AllowPullCommands { get; set; }
    public Dictionary<string, Dictionary<string, string>> PullToolSettings { get; set; } = new();
    // "Search" (#38): off until turned on, since it opens Edge or Spotify
    // online; and how many results Keeper reads.
    public bool AllowSearch { get; set; }
    public int SearchResultCount { get; set; } = 5;
    public static readonly int[] SearchResultChoices = [1, 2, 3, 4, 5];

    public static double RoundCoordinate(double value) => Math.Round(value, 2);

    // Everything Mini CloClo keeps - settings, Quran progress, the medicine
    // log, learned phrases - is in %LOCALAPPDATA%\MiniCloClo: the local
    // folder, never copied anywhere (a domain's roaming profile copies
    // %APPDATA% to its server at sign-out).
    internal static string FolderPath => FolderOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniCloClo");

    // Debug tests point this at a throwaway folder.
    internal static string? FolderOverride;

    // Before 1.10 it was in %APPDATA%\MiniCloClo: moved here once, at
    // startup. A file already here is kept, and its old copy left where it
    // was; the old folder goes once it's empty.
    internal static void MoveFromRoaming()
    {
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniCloClo");
            if (!Directory.Exists(old)) return;
            Directory.CreateDirectory(FolderPath);
            foreach (var file in Directory.GetFiles(old))
            {
                var moved = Path.Combine(FolderPath, Path.GetFileName(file));
                if (!File.Exists(moved)) File.Move(file, moved);
            }
            if (!Directory.EnumerateFileSystemEntries(old).Any()) Directory.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tried again at the next start.
        }
    }

    // The settings are kept in several files in FolderPath, each named for
    // what it holds (#45). Parts that belong to a place - prayer times,
    // reminders, questions and answers, each pull tool's options - are kept
    // in a folder per place ({0}: "mississauga-ca") with that place inside
    // ("Location"), so going back to a city brings back what was set there.
    // The tools each have their folder in tools\. Everything not listed here
    // is in settings.json.
    private static readonly (string File, string[] Keys)[] Parts =
    [
        ("location.json", [nameof(City), nameof(Latitude), nameof(Longitude), nameof(LocationTimeZoneId)]),
        (@"prayer-times\{0}\settings.json", [nameof(PrayerCalculationMethod), nameof(PrayerAsrMethod), nameof(PrayerAzanEnabled),
            nameof(PrayerAzanAudioPath), nameof(PrayerAnnounce), nameof(PrayerPreAlertMinutes), nameof(PrayerLookEnabled)]),
        (@"reminders\{0}\settings.json", [nameof(CustomReminders)]),
        (@"tools\questions-and-answers\{0}\settings.json", [nameof(Topics), nameof(TopicsSeeded)]),
        (@"tools\pull\settings.json", [nameof(AllowPullCommands)]),
        (@"tools\search\settings.json", [nameof(AllowSearch), nameof(SearchResultCount)]),
        ("contacts.json", [nameof(Contacts), nameof(PhoneDeviceId), nameof(PhoneName)]),
    ];
    private const string GeneralFile = "settings.json";
    private const string LocationKey = "Location";

    // Each pull tool's own options, per place:
    // tools\pull\<tool>\<place>\settings.json, { "Location", "Settings" },
    // beside the result its last pull brought (PullTools).
    private const string PullToolFile = @"tools\pull\{1}\{0}\settings.json", PullToolKey = "Settings";
    internal static string PullToolFolder(string tool, string place) => Path.Combine(FolderPath, "tools", "pull", tool, place);
    private static readonly System.Text.RegularExpressions.Regex ToolId = new("^[a-z0-9-]{1,30}$");

    private static bool IsPerPlace(string file) => file.Contains("{0}");

    // The place the per-place parts were loaded for; a save for another
    // place (the city was changed) first brings back what that place had.
    private string? _place;

    // Set by a save that changed the place to one kept before: its own
    // prayer times, reminders, questions and pull tools came back.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PlaceRestored { get; private set; }

    // "mississauga-ca", "london-gb": the city, and its country when it is in
    // the offline list (so London, Ontario isn't London, England); typed-in
    // coordinates without a name use the coordinates.
    internal static string PlaceKey(string city, double latitude, double longitude)
    {
        var name = new string(city.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        name = string.Join("-", name.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > 40) name = name[..40];
        if (name.Length == 0)
            name = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{latitude:0.00}_{longitude:0.00}").Replace('.', 'p');
        var country = OfflineCities.ForPlace(city, latitude, longitude) is { } known ? OfflineCities.CountryCode(known) : null;
        return country is { Length: > 0 } ? $"{name}-{country.ToLowerInvariant()}" : name;
    }

    internal string PlaceKey() => PlaceKey(City, Latitude, Longitude);

    private static string PartFile(string file, string place, string tool = "") =>
        Path.Combine(FolderPath, string.Format(file, place, tool));

    public static WidgetSettings Load()
    {
        try
        {
            SplitOneFileSettings();
            MoveIntoFolders();
            var location = ReadPart(PartFile(Parts[0].File, ""));
            var general = ReadPart(Path.Combine(FolderPath, GeneralFile));
            if (location != null || general != null)
            {
                var merged = new JsonObject();
                Merge(merged, location);
                var defaults = new WidgetSettings();
                var place = PlaceKey(
                    merged[nameof(City)]?.GetValue<string>() ?? defaults.City,
                    merged[nameof(Latitude)]?.GetValue<double>() ?? defaults.Latitude,
                    merged[nameof(Longitude)]?.GetValue<double>() ?? defaults.Longitude);
                Merge(merged, general);
                foreach (var (file, _) in Parts.Skip(1))
                    Merge(merged, ReadPart(PartFile(file, place)));
                Merge(merged, ReadPullTools(place));
                var loaded = merged.Deserialize<WidgetSettings>(LoadOptions);
                if (loaded != null)
                {
                    loaded._place = place;
                    return loaded.ValidateAndNormalize();
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings: fall back to defaults below.
        }
        return ForThisPc();
    }

    // One part's settings (null: missing or unreadable). The place a
    // per-place file is for ("Location") is left out: the place in use is
    // location.json's.
    private static JsonObject? ReadPart(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (JsonNode.Parse(ReadSettingsBytes(path), documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) is not JsonObject part) return null;
            part.Remove(LocationKey);
            return part;
        }
        catch
        {
            return null;
        }
    }

    private static void Merge(JsonObject into, JsonObject? part)
    {
        if (part == null) return;
        foreach (var (key, value) in part) into[key] = value?.DeepClone();
    }

    // The pull tools' options kept for a place, as { "PullToolSettings":
    // { "<tool>": { ... } } } (null: none kept there).
    private static JsonObject? ReadPullTools(string place)
    {
        var pull = Path.Combine(FolderPath, "tools", "pull");
        if (!Directory.Exists(pull)) return null;
        var tools = new JsonObject();
        foreach (var folder in Directory.GetDirectories(pull).Order(StringComparer.OrdinalIgnoreCase))
        {
            var tool = Path.GetFileName(folder);
            if (ToolId.IsMatch(tool) && ReadPart(PartFile(PullToolFile, place, tool))?[PullToolKey] is JsonObject values)
                tools[tool] = values.DeepClone();
        }
        return tools.Count == 0 ? null : new JsonObject { [nameof(PullToolSettings)] = tools };
    }

    // Before #45 everything was in one settings.json (City, prayer times,
    // reminders, contacts... together): split into the named files once. The
    // old file is kept in backups, as is the backup made before the medicine
    // reminders; quran.json is now quran-progress.json.
    private static void SplitOneFileSettings()
    {
        var general = Path.Combine(FolderPath, GeneralFile);
        if (!File.Exists(PartFile(Parts[0].File, "")) && File.Exists(general) &&
            ReadPart(general) is { } old && old.ContainsKey(nameof(City)) &&
            old.Deserialize<WidgetSettings>(LoadOptions) is { } settings)
        {
            var backups = Path.Combine(FolderPath, "backups");
            Directory.CreateDirectory(backups);
            File.Copy(general, Path.Combine(backups, $"settings-{File.GetLastWriteTime(general):yyyy-MM-dd}-before-split.json"), overwrite: false);
            settings.ValidateAndNormalize();
            settings.WriteFiles(settings.PlaceKey());
        }
        var quran = Path.Combine(FolderPath, "quran.json");
        var quranProgress = Path.Combine(FolderPath, "quran-progress.json");
        if (File.Exists(quran) && !File.Exists(quranProgress)) File.Move(quran, quranProgress);
        foreach (var before in Directory.Exists(FolderPath) ? Directory.GetFiles(FolderPath, "settings.before-*.json") : [])
        {
            var backups = Path.Combine(FolderPath, "backups");
            Directory.CreateDirectory(backups);
            var what = Path.GetFileNameWithoutExtension(before)["settings.".Length..];
            var moved = Path.Combine(backups, $"settings-{File.GetLastWriteTime(before):yyyy-MM-dd}-{what}.json");
            if (!File.Exists(moved)) File.Move(before, moved);
        }
    }

    // The first split (#45) kept per-place parts as "<part>_<place>.json"
    // beside settings.json: moved into their folders once, a copy of each
    // kept in backups. The pull results folder (pull\) is now each tool's
    // folder, so the old one goes to backups too.
    private static void MoveIntoFolders()
    {
        if (!Directory.Exists(FolderPath)) return;
        var flat = new System.Text.RegularExpressions.Regex("^(prayer-times|reminders|questions-and-answers|pull-tools)_(.+)\\.json$");
        var old = Directory.GetFiles(FolderPath, "*.json")
            .Select(file => (File: file, Match: flat.Match(Path.GetFileName(file))))
            .Where(f => f.Match.Success)
            .ToList();
        var search = Path.Combine(FolderPath, "search.json");
        var results = Path.Combine(FolderPath, "pull");
        if (old.Count == 0 && !File.Exists(search) && !Directory.Exists(results)) return;
        var backups = Path.Combine(FolderPath, "backups", $"settings-{DateTime.Now:yyyy-MM-dd}-before-folders");
        Directory.CreateDirectory(backups);

        // Pull on/off was kept per place: the one of the place in use.
        var location = ReadPart(PartFile(Parts[0].File, ""));
        var inUse = location?[nameof(City)] is JsonValue city && location[nameof(Latitude)] is JsonValue latitude && location[nameof(Longitude)] is JsonValue longitude
            ? PlaceKey(city.GetValue<string>(), latitude.GetValue<double>(), longitude.GetValue<double>()) : null;
        foreach (var (file, match) in old.OrderBy(f => f.Match.Groups[2].Value == inUse ? 0 : 1))
        {
            var (part, place) = (match.Groups[1].Value, match.Groups[2].Value);
            if (!File.Exists(Path.Combine(backups, Path.GetFileName(file)))) File.Copy(file, Path.Combine(backups, Path.GetFileName(file)));
            if (Read(file) is not { } content) continue;
            if (part == "pull-tools")
            {
                var pull = PartFile(Parts.First(p => p.Keys.Contains(nameof(AllowPullCommands))).File, place);
                if (!File.Exists(pull) && content[nameof(AllowPullCommands)] is { } allow)
                    WriteFile(pull, new JsonObject { [nameof(AllowPullCommands)] = allow.DeepClone() });
                if (content[nameof(PullToolSettings)] is JsonObject tools)
                    foreach (var (tool, values) in tools)
                        if (ToolId.IsMatch(tool) && values is JsonObject && PartFile(PullToolFile, place, tool) is var path && !File.Exists(path))
                            WriteFile(path, new JsonObject { [LocationKey] = content[LocationKey]?.DeepClone(), [PullToolKey] = values.DeepClone() });
            }
            else if (PartFile(Parts.First(p => p.File.StartsWith(part) || p.File.Contains(@"\" + part + @"\")).File, place) is var path && !File.Exists(path))
                WriteFile(path, content);
            File.Delete(file);
        }
        if (File.Exists(search))
        {
            if (!File.Exists(Path.Combine(backups, "search.json"))) File.Copy(search, Path.Combine(backups, "search.json"));
            var moved = PartFile(Parts.First(p => p.Keys.Contains(nameof(AllowSearch))).File, "");
            if (!File.Exists(moved) && Read(search) is { } content) WriteFile(moved, content);
            File.Delete(search);
        }
        if (Directory.Exists(results) && !Directory.Exists(Path.Combine(backups, "pull")))
            Directory.Move(results, Path.Combine(backups, "pull"));

        static JsonObject? Read(string path)
        {
            try
            {
                return JsonNode.Parse(ReadSettingsBytes(path), documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject;
            }
            catch
            {
                return null;
            }
        }
    }

    // First run: the first listed city in the PC's own time zone (Windows'
    // setting, not its location), so prayer times and the sky match the
    // clock from the start. Cairo when none is listed.
    private static WidgetSettings ForThisPc()
    {
        var settings = new WidgetSettings();
        if (OfflineCities.All.FirstOrDefault(c => c.TimeZoneId == TimeZoneInfo.Local.Id) is { } city)
        {
            settings.City = city.Name;
            settings.Latitude = city.Latitude;
            settings.Longitude = city.Longitude;
            settings.LocationTimeZoneId = city.TimeZoneId;
            settings.PrayerCalculationMethod = city.Method;
        }
        settings.Topics = TopicAction.IslamicDates();
        settings.TopicsSeeded = true;
        return settings;
    }

    private static byte[] ReadSettingsBytes(string path)
    {
        // Read at most the limit plus one byte; checking FileInfo.Length alone
        // would allow a file that grows after the check to use unbounded memory.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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
        Contacts = (Contacts ?? new List<ContactSettings>())
            .Where(contact => contact != null && contact.IsValid())
            .Take(MaxContacts)
            .ToList();
        if (PhoneDeviceId is not null && (PhoneDeviceId.Length > 512 || PhoneDeviceId.Any(char.IsControl))) PhoneDeviceId = null;
        if (PhoneName is not null && !IsSafeText(PhoneName, 100)) PhoneName = null;
        if (!TopicsSeeded)
        {
            Topics = TopicAction.IslamicDates();
            TopicsSeeded = true;
        }
        Topics = (Topics ?? new List<TopicSettings>())
            .Take(TopicSettings.MaxTopics)
            .Select(topic => topic?.ValidateAndNormalize())
            .Where(topic => topic != null)
            .Cast<TopicSettings>()
            .ToList();
        PullToolSettings = (PullToolSettings ?? new())
            .Where(tool => tool.Value != null && System.Text.RegularExpressions.Regex.IsMatch(tool.Key, "^[a-z0-9-]{1,30}$"))
            .Take(30)
            .ToDictionary(tool => tool.Key, tool => tool.Value
                .Where(s => System.Text.RegularExpressions.Regex.IsMatch(s.Key, "^[A-Za-z][A-Za-z0-9]{0,29}$") && PullTools.IsSettingValue(s.Value))
                .Take(20)
                .ToDictionary(s => s.Key, s => s.Value));
        SearchResultCount = SearchResultChoices.Contains(SearchResultCount) ? SearchResultCount : 5;
        ShortcutsOff = (ShortcutsOff ?? new())
            .Where(key => key is { Length: 1 } && key[0] is >= 'A' and <= 'Z')
            .Distinct()
            .ToList();
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
            var place = PlaceKey();
            PlaceRestored = false;
            // The city was changed: a place kept before brings back its own
            // prayer times, reminders, questions and pull tools; a new place
            // starts from the ones in use. The old place's files stay as they
            // were.
            if (_place != null && place != _place) PlaceRestored = BringBackPlace(place);
            _place = place;
            WriteFiles(place);
        }
        catch
        {
            // Best-effort persistence; losing a save isn't fatal.
        }
    }

    private bool BringBackPlace(string place)
    {
        var current = JsonSerializer.SerializeToNode(this) as JsonObject;
        if (current == null) return false;
        var found = false;
        foreach (var kept in Parts.Where(p => IsPerPlace(p.File)).Select(p => ReadPart(PartFile(p.File, place))).Append(ReadPullTools(place)))
            if (kept != null)
            {
                Merge(current, kept);
                found = true;
            }
        if (!found || current.Deserialize<WidgetSettings>(LoadOptions)?.ValidateAndNormalize() is not { } restored) return false;
        foreach (var key in Parts.Where(p => IsPerPlace(p.File)).SelectMany(p => p.Keys).Append(nameof(PullToolSettings)))
            if (typeof(WidgetSettings).GetProperty(key) is { CanWrite: true } property)
                property.SetValue(this, property.GetValue(restored));
        return true;
    }

    private void WriteFiles(string place)
    {
        Directory.CreateDirectory(FolderPath);
        if (JsonSerializer.SerializeToNode(this) is not JsonObject all) return;
        var location = new JsonObject();
        foreach (var key in Parts[0].Keys) location[key] = all[key]?.DeepClone();
        var used = new HashSet<string> { nameof(PullToolSettings) };
        foreach (var (file, keys) in Parts)
        {
            var part = new JsonObject();
            if (IsPerPlace(file)) part[LocationKey] = location.DeepClone();
            foreach (var key in keys)
            {
                part[key] = all[key]?.DeepClone();
                used.Add(key);
            }
            WriteFile(PartFile(file, place), part);
        }
        // A tool whose options went back to its own (left out here) keeps
        // its file for this place, emptied, so the old options can't return.
        var pull = Path.Combine(FolderPath, "tools", "pull");
        var tools = Directory.Exists(pull)
            ? Directory.GetDirectories(pull).Select(Path.GetFileName).Where(t => t != null && ToolId.IsMatch(t) && Directory.Exists(PullToolFolder(t, place))).Cast<string>()
            : [];
        foreach (var tool in tools.Union(PullToolSettings.Keys))
            WriteFile(PartFile(PullToolFile, place, tool), new JsonObject
            {
                [LocationKey] = location.DeepClone(),
                [PullToolKey] = all[nameof(PullToolSettings)]?[tool]?.DeepClone() ?? new JsonObject(),
            });
        var general = new JsonObject();
        foreach (var (key, value) in all)
            if (!used.Contains(key)) general[key] = value?.DeepClone();
        WriteFile(Path.Combine(FolderPath, GeneralFile), general);
    }

    // Written beside and then swapped in, so a crash mid-write can never
    // leave a half-written file. The temporary file gets a random name and
    // must not exist yet (CreateNew), so nothing can pre-create it as a link
    // to redirect this write. A file that already says the same is left.
    private static void WriteFile(string path, JsonObject content)
    {
        var json = content.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == json) return;
        }
        catch (IOException) { }
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "." + Path.GetRandomFileName() + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

public sealed class CustomReminderSettings
{
    public static readonly int[] IntervalChoices = { 15, 30, 45, 60, 90, 120, 180, 240, 360, 480, 720, 1440 };

    public string Message { get; set; } = "Drink a glass of water.";
    public int IntervalMinutes { get; set; } = 60;
    public int StartHour { get; set; } = 9;
    public int EndHour { get; set; } = 21;
    // Spoken by default: this build is for people who may not see the label.
    public bool Speak { get; set; } = true;
    public bool PlayAudio { get; set; }
    public string? AudioPath { get; set; }
    public bool Enabled { get; set; } = true;
    // The last scheduled time it fired (local time), kept so a restart or
    // reinstall doesn't start its count again (#39). Null: not started.
    public DateTime? LastFired { get; set; }
    // A medicine reminder (#29): asks "Did you take it?" and again every
    // AskAgainMinutes until "yes", for at most AskForMinutes, then the dose
    // is logged as missed.
    public bool WaitForAnswer { get; set; }
    public int AskAgainMinutes { get; set; } = 5;
    public int AskForMinutes { get; set; } = 60;
    public static readonly int[] AskAgainChoices = { 5, 10, 15 };
    public static readonly int[] AskForChoices = { 30, 60, 120 };

    public CustomReminderSettings ValidateAndNormalize()
    {
        if (string.IsNullOrWhiteSpace(Message) || Message.Length > 120 || Message.Any(char.IsControl))
            Message = "Drink a glass of water.";
        else
            Message = Message.Trim();

        if (!IntervalChoices.Contains(IntervalMinutes)) IntervalMinutes = 60;
        if (!AskAgainChoices.Contains(AskAgainMinutes)) AskAgainMinutes = 5;
        if (!AskForChoices.Contains(AskForMinutes)) AskForMinutes = 60;
        // A time in the future (a changed clock) would hold it back: start over.
        if (LastFired is { } last && last > DateTime.Now.AddMinutes(1)) LastFired = null;
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
        LastFired = LastFired,
        WaitForAnswer = WaitForAnswer,
        AskAgainMinutes = AskAgainMinutes,
        AskForMinutes = AskForMinutes,
    };

    public static string IntervalLabel(int minutes) =>
        minutes < 60 ? $"{minutes} minutes" : minutes == 60 ? "1 hour" : minutes == 1440 ? "24 hours (once a day)"
        : minutes % 60 == 0 ? $"{minutes / 60} hours" : $"{minutes / 60.0:0.#} hours";

    // What a screen reader says for the reminder in the list.
    [System.Text.Json.Serialization.JsonIgnore]
    public string Spoken => $"{NextTime}. {Message}. {Describe()}";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Description => Describe();

    // When it fires next, beside it in the reminders window.
    [System.Text.Json.Serialization.JsonIgnore]
    public string NextTime => ReminderSchedule.NextLabel(this, DateTime.Now);

    public string Describe() =>
        $"Every {IntervalLabel(IntervalMinutes)}, from {StartHour:00}:00 to {EndHour:00}:00, " +
        (PlayAudio ? "plays a sound, " : "") + (Speak ? "spoken" : "shown only") +
        (WaitForAnswer ? ", medicine: waits for an answer" : "") +
        $", {(Enabled ? "on" : "off")}";
}

public sealed class ContactSettings
{
    public string Name { get; set; } = "";
    // Another name the person may say, e.g. the Arabic one ("ماما" for Mom).
    public string? OtherName { get; set; }
    public string Number { get; set; } = "";

    public bool IsValid() =>
        IsName(Name) && (OtherName is null || OtherName.Length == 0 || IsName(OtherName)) && IsDialable(Number);

    private static bool IsName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 40 && !value.Any(char.IsControl);

    // Digits with an optional leading +, as the phone dials them.
    public static bool IsDialable(string? number) =>
        number is { Length: > 0 and <= 21 } &&
        number.Select((c, i) => char.IsAsciiDigit(c) || (c == '+' && i == 0)).All(ok => ok) &&
        number.Any(char.IsAsciiDigit);

    // Spaces, dashes, dots and brackets typed in a number are dropped.
    public static string CleanNumber(string text) =>
        new(text.Trim().Where((c, i) => char.IsAsciiDigit(c) || (c == '+' && i == 0)).ToArray());

    public ContactSettings Copy() => new() { Name = Name, OtherName = OtherName, Number = Number };

    // "416 555 0123": read in groups, which is how people say numbers.
    public static string SpokenNumber(string number)
    {
        var digits = number.TrimStart('+');
        var groups = new List<string>();
        var i = 0;
        while (i < digits.Length)
        {
            var size = digits.Length - i == 4 ? 4 : Math.Min(3, digits.Length - i);
            groups.Add(string.Join(' ', digits.Substring(i, size).ToCharArray()));
            i += size;
        }
        return (number.StartsWith('+') ? "plus " : "") + string.Join(", ", groups);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(OtherName) ? Name : $"{Name} — {OtherName}";

    [System.Text.Json.Serialization.JsonIgnore]
    public string Spoken => $"{Label}, {SpokenNumber(Number)}";
}

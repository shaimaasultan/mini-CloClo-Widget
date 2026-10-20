using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MiniCloClo;

// The place, calculation method and what happens at prayer time. The place
// comes from the offline city list or is typed in as coordinates - nothing
// is looked up online, and the PC's location is never read.
public partial class PrayerTimesWindow : Window
{
    // The last entry of the city list: coordinates typed in by hand.
    private sealed record OtherPlace(string Name)
    {
        public override string ToString() => "Another place — type its coordinates below";
    }

    private sealed record ZoneChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record Choice(int Value, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly LocalAudioPlayer _preview = new();
    private readonly OtherPlace _other;
    private string? _audioPath;
    private bool _ready;

    public string City { get; private set; } = "";
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string? TimeZoneId => (TimeZoneBox.SelectedItem as ZoneChoice)?.Id;
    public bool AzanEnabled => EnableAzanBox.IsChecked == true;
    public bool PrayerLookEnabled => EnableLookBox.IsChecked == true;
    public bool AnnounceEnabled => AnnounceBox.IsChecked == true;
    public int PreAlertMinutes => (PreAlertBox.SelectedItem as Choice)?.Value ?? 0;
    public string CalculationMethod => SelectedTag(MethodBox, "egyptian");
    public string AsrMethod => SelectedTag(AsrBox, "standard");
    public string? AudioPath => _audioPath;

    public PrayerTimesWindow(WidgetSettings settings)
    {
        InitializeComponent();
        _other = new OtherPlace(settings.City);

        var cities = new List<object>(OfflineCities.All) { _other };
        CityBox.ItemsSource = cities;
        TimeZoneBox.ItemsSource = TimeZoneInfo.GetSystemTimeZones()
            .Select(z => new ZoneChoice(z.Id, z.DisplayName)).ToList();
        PreAlertBox.ItemsSource = WidgetSettings.PreAlertChoices
            .Select(m => new Choice(m, m == 0 ? "Off" : $"{m} minutes before")).ToList();

        var city = OfflineCities.All.FirstOrDefault(c => c.Name == settings.City &&
            Math.Abs(c.Latitude - settings.Latitude) < 0.02 && Math.Abs(c.Longitude - settings.Longitude) < 0.02);
        CityBox.SelectedItem = (object?)city ?? _other;
        City = settings.City;
        Latitude = settings.Latitude;
        Longitude = settings.Longitude;
        LatitudeBox.Text = Latitude.ToString("0.00", CultureInfo.InvariantCulture);
        LongitudeBox.Text = Longitude.ToString("0.00", CultureInfo.InvariantCulture);
        SelectZone(settings.LocationTimeZoneId);
        SelectTag(MethodBox, settings.PrayerCalculationMethod);
        SelectTag(AsrBox, settings.PrayerAsrMethod);
        PreAlertBox.SelectedItem = ((List<Choice>)PreAlertBox.ItemsSource).FirstOrDefault(c => c.Value == settings.PrayerPreAlertMinutes)
            ?? ((List<Choice>)PreAlertBox.ItemsSource)[0];
        _audioPath = settings.PrayerAzanAudioPath;
        EnableAzanBox.IsChecked = settings.PrayerAzanEnabled;
        EnableLookBox.IsChecked = settings.PrayerLookEnabled;
        AnnounceBox.IsChecked = settings.PrayerAnnounce;
        UpdateAudioLabel();
        UpdateCoordinateFields();
        _ready = true;
        UpdateTimes();
        Loaded += (_, _) => CityBox.Focus();
        Closed += (_, _) => _preview.Dispose();
    }

    private void SelectZone(string? id)
    {
        var zones = (List<ZoneChoice>)TimeZoneBox.ItemsSource;
        var zone = PrayerTimeZone.Resolve(id);
        TimeZoneBox.SelectedItem = zones.FirstOrDefault(z => z.Id == zone.Id) ?? zones.FirstOrDefault(z => z.Id == TimeZoneInfo.Local.Id);
    }

    private static string SelectedTag(ComboBox box, string fallback) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private static void SelectTag(ComboBox box, string tag)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            ?? box.Items[0];
    }

    // Coordinates are typed only for "another place"; a listed city fills them in.
    private void UpdateCoordinateFields()
    {
        var other = CityBox.SelectedItem is OtherPlace;
        LatitudeBox.IsReadOnly = LongitudeBox.IsReadOnly = !other;
        LatitudeBox.Opacity = LongitudeBox.Opacity = other ? 1 : 0.75;
    }

    private void CityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (CityBox.SelectedItem is OfflineCity city)
        {
            City = city.Name;
            Latitude = city.Latitude;
            Longitude = city.Longitude;
            LatitudeBox.Text = city.Latitude.ToString("0.00", CultureInfo.InvariantCulture);
            LongitudeBox.Text = city.Longitude.ToString("0.00", CultureInfo.InvariantCulture);
            _ready = false;
            SelectZone(city.TimeZoneId);
            SelectTag(MethodBox, city.Method);
            _ready = true;
        }
        else
        {
            City = "My place";
        }
        UpdateCoordinateFields();
        UpdateTimes();
    }

    private void Coordinates_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || CityBox.SelectedItem is not OtherPlace) return;
        if (TryReadCoordinates(out var latitude, out var longitude))
        {
            Latitude = latitude;
            Longitude = longitude;
            StatusText.Text = "";
            UpdateTimes();
        }
        else StatusText.Text = "Type the latitude (−90 to 90) and longitude (−180 to 180) as numbers, for example 30.04 and 31.24.";
    }

    private bool TryReadCoordinates(out double latitude, out double longitude)
    {
        longitude = 0;
        var text = (LatitudeBox.Text + "").Trim().Replace(',', '.');
        var text2 = (LongitudeBox.Text + "").Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) &&
               double.TryParse(text2, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) &&
               double.IsFinite(latitude) && double.IsFinite(longitude) &&
               Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;
    }

    private void Settings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) UpdateTimes();
    }

    private TimeZoneInfo Zone => PrayerTimeZone.Resolve(TimeZoneId);

    private IReadOnlyList<PrayerOccurrence> TodaysTimes(out DateTime date)
    {
        date = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).Date;
        return PrayerTimesCalculator.ForDate(date, Latitude, Longitude, CalculationMethod, AsrMethod);
    }

    private void UpdateTimes()
    {
        var times = TodaysTimes(out var date);
        var zone = Zone;
        ScheduleHeading.Text = $"Prayer times in {City} — {date.ToString("dddd, d MMMM", CultureInfo.CurrentCulture)}";
        TimesText.Text = times.Count == 0
            ? "Prayer times are unavailable for this place and date."
            : string.Join(Environment.NewLine, times.Select(prayer =>
                $"{prayer.Name,-9}  {TimeZoneInfo.ConvertTime(prayer.StartsAtUtc, zone).ToString("t", CultureInfo.CurrentCulture)}"));
        System.Windows.Automation.AutomationProperties.SetHelpText(TimesText, TimesText.Text.Replace(Environment.NewLine, ", "));
        TimeZoneNote.Text = $"Worked out on this PC from {Latitude:0.00}, {Longitude:0.00} in {zone.DisplayName}. " +
                            "Compare them with your local mosque.";
    }

    // Which azan will play: the chosen file, else the built-in one.
    private void UpdateAudioLabel()
    {
        var builtIn = AzanLibrary.Default;
        AudioFileText.Text = LocalAudioPlayer.IsPlayable(_audioPath) ? Path.GetFileName(_audioPath)
            : builtIn != null ? $"Built-in: {AzanLibrary.DisplayName(builtIn)}"
            : "No azan recording — the prayer time is spoken instead";
        BuiltInButton.IsEnabled = _audioPath != null && builtIn != null;
    }

    private void UseBuiltIn_Click(object sender, RoutedEventArgs e)
    {
        _audioPath = null;
        UpdateAudioLabel();
        StatusText.Text = AzanLibrary.Default is { } builtIn
            ? $"The built-in azan, {AzanLibrary.DisplayName(builtIn)}, will play."
            : "There is no built-in azan recording.";
    }

    private void ChooseAudio_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose an azan recording on this PC",
            Filter = "Audio files (*.mp3;*.wav)|*.mp3;*.wav",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (picker.ShowDialog(this) != true) return;
        if (!LocalAudioPlayer.IsAllowedLocalAudioFile(picker.FileName))
        {
            StatusText.Text = "Choose an MP3 or WAV file on this PC (not on a network share).";
            return;
        }
        _audioPath = picker.FileName;
        EnableAzanBox.IsChecked = true;
        StatusText.Text = "Azan recording chosen.";
        UpdateAudioLabel();
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_preview.IsPlaying)
        {
            _preview.Stop();
            PreviewButton.Content = "Pre_view";
            StatusText.Text = "Preview stopped.";
            return;
        }
        var playing = _preview.TryPlay(AzanLibrary.Effective(_audioPath));
        PreviewButton.Content = playing ? "Stop pre_view" : "Pre_view";
        StatusText.Text = playing
            ? "Playing the azan. Press Preview again to stop."
            : "Choose an azan recording first: an MP3 or WAV file of up to 30 MB.";
    }

    private void ReadAloud_Click(object sender, RoutedEventArgs e)
    {
        var times = TodaysTimes(out _);
        var zone = Zone;
        if (times.Count == 0)
        {
            Voice.Say("Prayer times are unavailable for this place and date.", "مواقيت الصلاة غير متاحة للمكان ده.");
            return;
        }
        var english = $"Prayer times in {City} today: " + string.Join(", ", times.Select(p =>
            $"{p.Name} {SpokenTime.English(TimeZoneInfo.ConvertTime(p.StartsAtUtc, zone).DateTime)}")) + ".";
        var arabic = "مواقيت الصلاة النهارده: " + string.Join("، ", times.Select(p =>
            $"{SpokenTime.ArabicPrayerName(p.Name)} {SpokenTime.Arabic(TimeZoneInfo.ConvertTime(p.StartsAtUtc, zone).DateTime)}")) + ".";
        Voice.Say(english, arabic);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CityBox.SelectedItem is OtherPlace)
        {
            if (!TryReadCoordinates(out var latitude, out var longitude))
            {
                StatusText.Text = "Type the latitude (−90 to 90) and longitude (−180 to 180) before saving.";
                LatitudeBox.Focus();
                return;
            }
            Latitude = latitude;
            Longitude = longitude;
        }
        if (AzanEnabled && AzanLibrary.Effective(_audioPath) == null)
        {
            StatusText.Text = "Choose a readable MP3 or WAV azan recording (up to 30 MB) before turning the azan on.";
            return;
        }
        DialogResult = true;
    }
}

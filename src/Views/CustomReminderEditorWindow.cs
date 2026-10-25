using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace MiniCloClo;

// One reminder: its message, how often, its active hours and whether it is
// spoken or plays a sound. Every field has a label with an access key, so it
// can be filled in from the keyboard with a screen reader.
internal sealed class CustomReminderEditorWindow : Window
{
    private readonly TextBox _message = new() { MaxLength = 120, MinHeight = 58, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Padding = new Thickness(3) };
    private readonly ComboBox _interval = new() { MinWidth = 150 };
    private readonly ComboBox _startHour = new() { MinWidth = 110 };
    private readonly ComboBox _endHour = new() { MinWidth = 110 };
    private readonly CheckBox _enabled = new() { Content = "Reminder _on", Margin = new Thickness(0, 10, 0, 0) };
    private readonly CheckBox _speak = new() { Content = "_Say the message out loud", Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _playAudio = new() { Content = "_Play a sound file first", Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _waitForAnswer = new() { Content = "_Wait for an answer (for medicine): ask until I say yes", Margin = new Thickness(0, 10, 0, 0) };
    private readonly ComboBox _askAgain = new() { MinWidth = 120 };
    private readonly ComboBox _askFor = new() { MinWidth = 120 };
    private readonly TextBlock _audioName = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _audioStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly LocalAudioPlayer _preview = new();
    private string? _audioPath;

    public CustomReminderSettings Reminder { get; private set; }

    public CustomReminderEditorWindow(CustomReminderSettings reminder, bool isNew)
    {
        Reminder = reminder.Copy();
        Title = isNew ? "New reminder" : "Edit reminder";
        Width = 470;
        FontSize = 15;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Closed += (_, _) => _preview.Dispose();
        Loaded += (_, _) => { _message.Focus(); _message.SelectAll(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
            }
        };
        AutomationProperties.SetLiveSetting(_audioStatus, AutomationLiveSetting.Assertive);

        foreach (var minutes in CustomReminderSettings.IntervalChoices)
            _interval.Items.Add(CustomReminderSettings.IntervalLabel(minutes));
        foreach (var hour in Enumerable.Range(0, 24))
        {
            var label = new System.DateTime(2000, 1, 1, hour, 0, 0).ToString("t", System.Globalization.CultureInfo.CurrentCulture);
            _startHour.Items.Add(label);
            _endHour.Items.Add(label);
        }

        _message.Text = Reminder.Message;
        _interval.SelectedIndex = System.Array.IndexOf(CustomReminderSettings.IntervalChoices, Reminder.IntervalMinutes);
        _startHour.SelectedIndex = Reminder.StartHour;
        _endHour.SelectedIndex = Reminder.EndHour;
        _enabled.IsChecked = Reminder.Enabled;
        _speak.IsChecked = Reminder.Speak;
        _playAudio.IsChecked = Reminder.PlayAudio;
        _audioPath = Reminder.AudioPath;
        foreach (var minutes in CustomReminderSettings.AskAgainChoices) _askAgain.Items.Add($"{minutes} minutes");
        foreach (var minutes in CustomReminderSettings.AskForChoices) _askFor.Items.Add(minutes < 60 ? $"{minutes} minutes" : minutes == 60 ? "1 hour" : $"{minutes / 60} hours");
        _askAgain.SelectedIndex = System.Array.IndexOf(CustomReminderSettings.AskAgainChoices, Reminder.AskAgainMinutes);
        _askFor.SelectedIndex = System.Array.IndexOf(CustomReminderSettings.AskForChoices, Reminder.AskForMinutes);
        _waitForAnswer.IsChecked = Reminder.WaitForAnswer;
        UpdateAudioName();

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(Labelled("_Message", _message));
        content.Children.Add(Labelled("_Repeat every", _interval, top: 12));

        var hours = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        hours.Children.Add(Labelled("Active _from", _startHour));
        var to = Labelled("Active _until", _endHour);
        to.Margin = new Thickness(16, 0, 0, 0);
        hours.Children.Add(to);
        content.Children.Add(hours);
        content.Children.Add(_enabled);
        content.Children.Add(_speak);
        content.Children.Add(_playAudio);
        // Medicine (#29): "Did you take it?" until "yes"; logged.
        content.Children.Add(_waitForAnswer);
        var asking = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 6, 0, 0) };
        asking.Children.Add(Labelled("Ask a_gain every", _askAgain));
        var askFor = Labelled("Stop asking a_fter", _askFor);
        askFor.Margin = new Thickness(16, 0, 0, 0);
        asking.Children.Add(askFor);
        content.Children.Add(asking);
        void UpdateAsking() => asking.IsEnabled = _waitForAnswer.IsChecked == true;
        _waitForAnswer.Click += (_, _) =>
        {
            UpdateAsking();
            Voice.Say(_waitForAnswer.IsChecked == true
                ? "Wait for an answer: on. Keeper will ask did you take it, until you say yes, and keep a log."
                : "Wait for an answer: off.");
        };
        UpdateAsking();

        var audioControls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        var chooseAudio = new Button { Content = "_Choose sound…", Padding = new Thickness(10, 5, 10, 5) };
        chooseAudio.Click += ChooseAudio_Click;
        audioControls.Children.Add(chooseAudio);
        var previewAudio = new Button { Content = "Pre_view", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) };
        previewAudio.Click += PreviewAudio_Click;
        audioControls.Children.Add(previewAudio);
        var sayIt = new Button { Content = "Say it _now", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) };
        sayIt.Click += (_, _) => Voice.Say(string.IsNullOrWhiteSpace(_message.Text) ? "The message is empty." : _message.Text.Trim());
        audioControls.Children.Add(sayIt);
        content.Children.Add(audioControls);
        content.Children.Add(_audioName);
        content.Children.Add(_audioStatus);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(cancel);
        var save = new Button { Content = "Save", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), IsDefault = true };
        save.Click += Save_Click;
        buttons.Children.Add(save);
        content.Children.Add(buttons);
        Content = content;
    }

    // A label above a field; the label's access key moves focus to it and
    // screen readers announce it as the field's name.
    private static StackPanel Labelled(string label, Control field, double top = 0)
    {
        var panel = new StackPanel { Margin = new Thickness(0, top, 0, 0) };
        var text = new Label { Content = label, Target = field, Padding = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(text);
        panel.Children.Add(field);
        AutomationProperties.SetLabeledBy(field, text);
        return panel;
    }

    private void UpdateAudioName()
    {
        _audioName.Text = _audioPath is null ? "No sound file chosen" : Path.GetFileName(_audioPath);
        _audioName.ToolTip = _audioPath;
    }

    private void ChooseAudio_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose a reminder sound on this PC",
            Filter = "Audio files (*.mp3;*.wav)|*.mp3;*.wav",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (picker.ShowDialog(this) != true) return;
        if (!LocalAudioPlayer.IsPlayable(picker.FileName))
        {
            _audioStatus.Text = "Choose a readable MP3 or WAV file on this PC, up to 30 MB.";
            return;
        }
        _audioPath = picker.FileName;
        _audioStatus.Text = "Sound chosen.";
        _playAudio.IsChecked = true;
        UpdateAudioName();
    }

    private void PreviewAudio_Click(object sender, RoutedEventArgs e)
    {
        _audioStatus.Text = _preview.TryPlay(_audioPath)
            ? "Playing the sound. Close this window to stop it."
            : "Choose a sound file first: an MP3 or WAV file of up to 30 MB.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_message.Text) || _message.Text.Any(char.IsControl))
        {
            _audioStatus.Text = "Type a message for the reminder.";
            _message.Focus();
            return;
        }

        if (_interval.SelectedIndex < 0 || _startHour.SelectedIndex < 0 || _endHour.SelectedIndex < 0)
        {
            _audioStatus.Text = "Choose how often, and both active hours.";
            return;
        }
        var interval = CustomReminderSettings.IntervalChoices[_interval.SelectedIndex];
        var start = _startHour.SelectedIndex;
        var end = _endHour.SelectedIndex;
        if (start == end)
        {
            _audioStatus.Text = "Choose different start and end hours.";
            _endHour.Focus();
            return;
        }
        if (_playAudio.IsChecked == true && !LocalAudioPlayer.IsPlayable(_audioPath))
        {
            _audioStatus.Text = "Choose a readable MP3 or WAV file, up to 30 MB, before turning the sound on.";
            return;
        }

        Reminder = new CustomReminderSettings
        {
            Message = _message.Text.Trim(),
            IntervalMinutes = interval,
            StartHour = start,
            EndHour = end,
            Enabled = _enabled.IsChecked == true,
            Speak = _speak.IsChecked == true,
            PlayAudio = _playAudio.IsChecked == true,
            AudioPath = _audioPath,
            WaitForAnswer = _waitForAnswer.IsChecked == true,
            AskAgainMinutes = CustomReminderSettings.AskAgainChoices[System.Math.Max(0, _askAgain.SelectedIndex)],
            AskForMinutes = CustomReminderSettings.AskForChoices[System.Math.Max(0, _askFor.SelectedIndex)],
            // Same times: keep its place (#39); new times start afresh.
            LastFired = interval == Reminder.IntervalMinutes && start == Reminder.StartHour && end == Reminder.EndHour ? Reminder.LastFired : null,
        };
        DialogResult = true;
    }
}

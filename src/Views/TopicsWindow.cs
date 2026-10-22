using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MiniCloClo;

// Questions and answers (#30): topics on the left, the selected topic's
// actions on the right. An action is a short word that is said, and what
// Keeper answers - text as entered, calendar dates, or a Hijri date. Fully
// usable with the keyboard and a screen reader; changes are kept only with
// "Save and close".
internal sealed class TopicsWindow : Window
{
    private readonly ObservableCollection<TopicSettings> _topics;
    private readonly ObservableCollection<TopicAction> _actions = new();
    private readonly ListBox _topicList = new() { MinHeight = 220 };
    private readonly ListBox _actionList = new() { MinHeight = 220 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _addTopic = Small("Add _topic…"), _renameTopic = Small("Re_name…"), _removeTopic = Small("Remove topi_c");
    private readonly Button _addAction = Small("_Add…"), _editAction = Small("_Edit…"), _removeAction = Small("Re_move"), _sayAction = Small("_Say the answer");

    public TopicSettings[] Topics => _topics.Select(t => t.Copy()).ToArray();
    // What the actions list shows now (debug test).
    internal string[] ShownActions => _actions.Select(a => a.Label).ToArray();
    internal string? ShownTopic => (_topicList.SelectedItem as TopicSettings)?.Name;

    public TopicsWindow(IEnumerable<TopicSettings> topics)
    {
        Title = "CloClo — Questions and answers";
        Width = 760;
        Height = 620;
        MinWidth = 600;
        MinHeight = 480;
        FontSize = 15;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = SystemColors.WindowBrush;
        _topics = new ObservableCollection<TopicSettings>(topics.Select(t => t.Copy()));
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        Configure(_topicList, _topics, nameof(TopicSettings.Label));
        Configure(_actionList, _actions, nameof(TopicAction.Label));
        _topicList.SelectionChanged += (_, _) => ShowActions();
        _actionList.SelectionChanged += (_, _) => UpdateButtons();
        _actionList.MouseDoubleClick += (_, _) => EditAction();

        _addTopic.Click += (_, _) => AddTopic();
        _renameTopic.Click += (_, _) => RenameTopic();
        _removeTopic.Click += (_, _) => RemoveTopic();
        _addAction.Click += (_, _) => AddAction();
        _editAction.Click += (_, _) => EditAction();
        _removeAction.Click += (_, _) => RemoveAction();
        _sayAction.Click += (_, _) => SayAction();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete) return;
            if (_actionList.IsKeyboardFocusWithin) { RemoveAction(); e.Handled = true; }
            else if (_topicList.IsKeyboardFocusWithin) { RemoveTopic(); e.Handled = true; }
        };

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        var left = Column("T_opics:", _topicList, _addTopic, _renameTopic, _removeTopic);
        var right = Column("What you say, and the answer — Enter edits:", _actionList, _addAction, _editAction, _removeAction, _sayAction);
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(_status);
        var closeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var export = Small("E_xport…");
        export.Click += (_, _) => Export();
        var import = Small("_Import…");
        import.Click += (_, _) => Import();
        var cancel = new Button { Content = "Cancel", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0), IsCancel = true };
        var save = new Button { Content = "Sa_ve and close", MinWidth = 120, Padding = new Thickness(10, 5, 10, 5) };
        save.Click += (_, _) => DialogResult = true;
        foreach (var button in new[] { export, import, cancel, save })
        {
            button.Margin = new Thickness(0, 0, 6, 0);
            closeRow.Children.Add(button);
        }
        bottom.Children.Add(closeRow);

        var content = new DockPanel { Margin = new Thickness(18) };
        var intro = new TextBlock
        {
            Text = "Say an action's word — \"Ramadan\", \"when is Civic\", \"رمضان امتى\" — and Keeper answers with what you entered here, or the next date. Say a topic's name to hear what's in it.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        };
        DockPanel.SetDock(intro, Dock.Top);
        content.Children.Add(intro);
        content.Children.Add(bottom);
        content.Children.Add(columns);
        Content = content;

        _topicList.SelectedIndex = _topics.Count > 0 ? 0 : -1;
        ShowActions();
        Loaded += (_, _) =>
        {
            (_topics.Count == 0 ? _addTopic : (UIElement)_topicList).Focus();
            Voice.Say(_topics.Count == 0
                    ? "Questions and answers. No topics yet. Press Alt T to add one."
                    : $"Questions and answers. {_topics.Count} topics. Choose one, then tab to its actions.",
                _topics.Count == 0 ? "الأسئلة والأجوبة. مفيش مواضيع لسه. دوس ألت T عشان تضيف." : $"الأسئلة والأجوبة. فيه {_topics.Count} مواضيع.");
        };
    }

    private static Button Small(string text) => new() { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6) };

    private static void Configure(ListBox list, System.Collections.IEnumerable items, string label)
    {
        list.ItemsSource = items;
        list.DisplayMemberPath = label;
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(label)));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 4, 6, 4)));
        list.ItemContainerStyle = style;
    }

    private static DockPanel Column(string label, ListBox list, params Button[] buttons)
    {
        var panel = new DockPanel();
        var text = new Label { Content = label, Target = list, Padding = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetLabeledBy(list, text);
        DockPanel.SetDock(text, Dock.Top);
        panel.Children.Add(text);
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var button in buttons) row.Children.Add(button);
        DockPanel.SetDock(row, Dock.Bottom);
        panel.Children.Add(row);
        panel.Children.Add(list);
        return panel;
    }

    private TopicSettings? Topic => _topicList.SelectedItem as TopicSettings;

    private void ShowActions()
    {
        _actions.Clear();
        if (Topic != null) foreach (var action in Topic.Actions) _actions.Add(action);
        _actionList.SelectedIndex = _actions.Count > 0 ? 0 : -1;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var topic = Topic != null;
        var action = _actionList.SelectedItem is TopicAction;
        _renameTopic.IsEnabled = _removeTopic.IsEnabled = topic;
        _addTopic.IsEnabled = _topics.Count < TopicSettings.MaxTopics;
        _addAction.IsEnabled = topic && Topic!.Actions.Count < TopicSettings.MaxActions;
        _editAction.IsEnabled = _removeAction.IsEnabled = _sayAction.IsEnabled = action;
        _status.Text = !topic ? "Add a topic, for example Vacations or Doctor." : _actions.Count == 0 ? "Add what you'll say, and its answer." : "";
    }

    // The words already used by other topics and actions, so a word is used once.
    private string? UsedElsewhere(string word, object? except)
    {
        var key = TranscriptCleanup.MatchingKey(word);
        foreach (var topic in _topics)
        {
            if (!ReferenceEquals(topic, except) && Keys(topic.Name, topic.OtherName).Contains(key)) return $"\"{word}\" is already the topic {topic.Name}.";
            foreach (var action in topic.Actions)
                if (!ReferenceEquals(action, except) && Keys(action.Word, action.OtherWord).Contains(key)) return $"\"{word}\" is already used in {topic.Name}.";
        }
        return null;
    }

    private static IEnumerable<string> Keys(string first, string? second) =>
        new[] { first, second }.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => TranscriptCleanup.MatchingKey(w!));

    private void AddTopic()
    {
        var editor = new TopicNameWindow(new TopicSettings(), "New topic", word => TopicVoiceCommand.Clash(word) ?? UsedElsewhere(word, null)) { Owner = this };
        if (editor.ShowDialog() != true) return;
        _topics.Add(editor.Topic);
        _topicList.SelectedItem = editor.Topic;
        Voice.Say($"{editor.Topic.Name} added. Now add what you'll say in it.", $"اتضاف {editor.Topic.OtherName ?? editor.Topic.Name}.");
    }

    private void RenameTopic()
    {
        if (Topic is not { } topic) return;
        var editor = new TopicNameWindow(topic.Copy(), "Rename topic", word => TopicVoiceCommand.Clash(word) ?? UsedElsewhere(word, topic)) { Owner = this };
        if (editor.ShowDialog() != true) return;
        topic.Name = editor.Topic.Name;
        topic.OtherName = editor.Topic.OtherName;
        var index = _topicList.SelectedIndex;
        _topics[index] = topic; // refreshes the label
        _topicList.SelectedIndex = index;
    }

    private void RemoveTopic()
    {
        if (Topic is not { } topic) return;
        if (MessageBox.Show(this, $"Remove the topic {topic.Name} and everything in it?", "Remove topic", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var index = _topicList.SelectedIndex;
        _topics.Remove(topic);
        _topicList.SelectedIndex = _topics.Count == 0 ? -1 : Math.Min(index, _topics.Count - 1);
        Voice.Say($"{topic.Name} removed.", $"اتشال {topic.OtherName ?? topic.Name}.");
    }

    private void AddAction()
    {
        if (Topic is not { } topic) return;
        var editor = new TopicActionWindow(new TopicAction(), isNew: true, word => TopicVoiceCommand.Clash(word) ?? UsedElsewhere(word, null)) { Owner = this };
        if (editor.ShowDialog() != true) return;
        topic.Actions.Add(editor.Action);
        ShowActions();
        _actionList.SelectedItem = editor.Action;
        Voice.Say($"{editor.Action.Word} added. Choose Save and close to keep it.", $"اتضاف {editor.Action.OtherWord ?? editor.Action.Word}.");
    }

    private void EditAction()
    {
        if (Topic is not { } topic || _actionList.SelectedItem is not TopicAction action) return;
        var editor = new TopicActionWindow(action.Copy(), isNew: false, word => TopicVoiceCommand.Clash(word) ?? UsedElsewhere(word, action)) { Owner = this };
        if (editor.ShowDialog() != true) return;
        var index = topic.Actions.IndexOf(action);
        topic.Actions[index] = editor.Action;
        ShowActions();
        _actionList.SelectedIndex = index;
    }

    private void RemoveAction()
    {
        if (Topic is not { } topic || _actionList.SelectedItem is not TopicAction action) return;
        var index = _actionList.SelectedIndex;
        topic.Actions.Remove(action);
        ShowActions();
        _actionList.SelectedIndex = _actions.Count == 0 ? -1 : Math.Min(index, _actions.Count - 1);
        Voice.Say($"{action.Word} removed.", $"اتشال {action.OtherWord ?? action.Word}.");
    }

    private void SayAction()
    {
        if (_actionList.SelectedItem is not TopicAction action) return;
        var (english, arabic) = TopicAnswers.Answer(action, DateTime.Today);
        _status.Text = english;
        Voice.Say(english, arabic);
    }

    // Export / import: one JSON file, so a family member can prepare the list
    // on another PC. Imported topics are added, checked like typed ones.
    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CloClo questions (*.json)|*.json", FileName = "CloClo questions.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_topics.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            _status.Text = $"Exported {_topics.Count} topics.";
            Voice.Say("Exported.", "اتصدر.");
        }
        catch (Exception ex) { _status.Text = $"Couldn't export ({ex.GetType().Name})."; }
    }

    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "CloClo questions (*.json)|*.json" };
        if (dialog.ShowDialog(this) == true) ImportFile(dialog.FileName);
    }

    // Also used by the debug test.
    internal void ImportFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 1_000_000) throw new InvalidDataException("too large");
            var imported = JsonSerializer.Deserialize<List<TopicSettings>>(File.ReadAllText(path)) ?? new();
            int added = 0, replaced = 0;
            foreach (var topic in imported.Select(t => t?.ValidateAndNormalize()).Where(t => t != null).Cast<TopicSettings>())
            {
                // A topic with the same name is replaced (fresh dates from
                // tools\Get-CalendarTopics.ps1 replace "Islamic dates").
                var old = _topics.FirstOrDefault(t => string.Equals(t.Name, topic.Name, StringComparison.OrdinalIgnoreCase));
                var index = old != null ? _topics.IndexOf(old) : _topics.Count;
                if (old != null) _topics.Remove(old);
                else if (_topics.Count >= TopicSettings.MaxTopics) break;
                // Words that clash with commands or each other are left out.
                topic.Actions = topic.Actions.Where(a => TopicVoiceCommand.Clash(a.Word) == null && UsedElsewhere(a.Word, null) == null).ToList();
                if (TopicVoiceCommand.Clash(topic.Name) != null || UsedElsewhere(topic.Name, null) != null) topic.Name += " (imported)";
                _topics.Insert(index, topic);
                if (old != null) replaced++; else added++;
            }
            _topicList.SelectedIndex = _topics.Count > 0 ? 0 : -1;
            _status.Text = $"Imported: {added} new topics, {replaced} replaced. Choose Save and close to keep them.";
            Voice.Say($"Imported {added} new topics, and replaced {replaced}. Choose Save and close to keep them.",
                $"اتضاف {added} مواضيع جديدة، واتغير {replaced}. اختار حفظ عشان يتحفظوا.");
        }
        catch (Exception ex) { _status.Text = $"That file couldn't be read ({ex.GetType().Name})."; }
    }
}

// A topic's name, and another name it may be called by.
internal sealed class TopicNameWindow : Window
{
    private readonly TextBox _name = new() { MaxLength = 40, Padding = new Thickness(3) };
    private readonly TextBox _otherName = new() { MaxLength = 40, Padding = new Thickness(3) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Func<string, string?> _clash;

    public TopicSettings Topic { get; private set; }

    public TopicNameWindow(TopicSettings topic, string title, Func<string, string?> clash)
    {
        Topic = topic;
        _clash = clash;
        Title = title;
        Width = 460;
        FontSize = 15;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Assertive);
        _name.Text = topic.Name;
        _otherName.Text = topic.OtherName ?? "";
        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(TopicActionWindow.Labelled("_Name (for example Vacations)", _name, 0));
        content.Children.Add(TopicActionWindow.Labelled("_Other name, for example in Arabic (optional)", _otherName, 12));
        content.Children.Add(_status);
        content.Children.Add(TopicActionWindow.OkCancel(Save));
        Content = content;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    private void Save()
    {
        var name = _name.Text.Trim();
        var other = string.IsNullOrWhiteSpace(_otherName.Text) ? null : _otherName.Text.Trim();
        if (name.Length == 0) { _status.Text = "Type the topic's name."; _name.Focus(); return; }
        foreach (var word in new[] { name, other })
            if (word != null && _clash(word) is { } reason) { _status.Text = reason; return; }
        Topic = new TopicSettings { Name = name, OtherName = other, Actions = Topic.Actions };
        DialogResult = true;
    }
}

// One action: the word that is said, and its answer.
internal sealed class TopicActionWindow : Window
{
    private readonly TextBox _word = new() { MaxLength = 40, Padding = new Thickness(3) };
    private readonly TextBox _otherWord = new() { MaxLength = 40, Padding = new Thickness(3) };
    private readonly RadioButton _isText = new() { Content = "_Text — read exactly as written", Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton _isCalendar = new() { Content = "_Dates — the next one, and how many days away", Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton _isHijri = new() { Content = "_Hijri date — worked out each year", Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBox _text = new() { MaxLength = 500, Padding = new Thickness(3), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
    private readonly TextBox _otherText = new() { MaxLength = 500, Padding = new Thickness(3), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
    private readonly TextBox _dates = new() { Padding = new Thickness(3), AcceptsReturn = true, MinHeight = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox _hijriMonth = new() { MinWidth = 180 };
    private readonly ComboBox _hijriDay = new() { MinWidth = 70 };
    private readonly StackPanel _textPanel = new(), _datesPanel = new(), _hijriPanel = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Func<string, string?> _clash;

    public TopicAction Action { get; private set; }

    public TopicActionWindow(TopicAction action, bool isNew, Func<string, string?> clash)
    {
        Action = action;
        _clash = clash;
        Title = isNew ? "New question" : "Edit question";
        Width = 520;
        FontSize = 15;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Assertive);

        _word.Text = action.Word;
        _otherWord.Text = action.OtherWord ?? "";
        _text.Text = action.Text ?? "";
        _otherText.Text = action.OtherText ?? "";
        _dates.Text = string.Join(Environment.NewLine, action.Dates);
        _hijriMonth.ItemsSource = Enumerable.Range(1, 12).Select(m => $"{m}. {SpokenTime.HijriMonthName(m)} — {SpokenTime.HijriMonthNameArabic(m)}").ToList();
        _hijriMonth.SelectedIndex = action.HijriMonth - 1;
        _hijriDay.ItemsSource = Enumerable.Range(1, 30).ToList();
        _hijriDay.SelectedItem = action.HijriDay;
        (action.Kind switch { TopicAction.CalendarKind => _isCalendar, TopicAction.HijriKind => _isHijri, _ => _isText }).IsChecked = true;
        foreach (var radio in new[] { _isText, _isCalendar, _isHijri }) radio.Checked += (_, _) => ShowKind();

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(Labelled("What you _say (one or two words, for example Ramadan or Civic)", _word, 0));
        content.Children.Add(Labelled("_Other way to say it, for example in Arabic (optional)", _otherWord, 12));
        var kindLabel = new TextBlock { Text = "The answer is:", Margin = new Thickness(0, 12, 0, 4) };
        content.Children.Add(kindLabel);
        content.Children.Add(_isText);
        content.Children.Add(_isCalendar);
        content.Children.Add(_isHijri);
        _textPanel.Children.Add(Labelled("Answer in _English (or any language)", _text, 8));
        _textPanel.Children.Add(Labelled("Answer in _Arabic (optional)", _otherText, 8));
        _datesPanel.Children.Add(Labelled("Dates, one per line, as year-month-day (for example 2027-08-02)", _dates, 8));
        var hijriRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var dayLabel = new Label { Content = "Da_y", Target = _hijriDay, Padding = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var monthLabel = new Label { Content = "_Month", Target = _hijriMonth, Padding = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLabeledBy(_hijriDay, dayLabel);
        AutomationProperties.SetLabeledBy(_hijriMonth, monthLabel);
        hijriRow.Children.Add(dayLabel);
        hijriRow.Children.Add(_hijriDay);
        hijriRow.Children.Add(monthLabel);
        hijriRow.Children.Add(_hijriMonth);
        _hijriPanel.Children.Add(hijriRow);
        _hijriPanel.Children.Add(new TextBlock
        {
            Text = "Worked out with Windows' Umm al-Qura calendar; it may differ by a day from a local moon sighting.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = SystemColors.GrayTextBrush,
        });
        content.Children.Add(_textPanel);
        content.Children.Add(_datesPanel);
        content.Children.Add(_hijriPanel);
        content.Children.Add(_status);
        var say = new Button { Content = "Say the ans_wer", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        say.Click += (_, _) =>
        {
            if (Build() is { } built)
            {
                var (english, arabic) = TopicAnswers.Answer(built, DateTime.Today);
                _status.Text = english;
                Voice.Say(english, arabic);
            }
        };
        content.Children.Add(OkCancel(Save, say));
        Content = content;
        ShowKind();
        Loaded += (_, _) => { _word.Focus(); _word.SelectAll(); };
    }

    private void ShowKind()
    {
        _textPanel.Visibility = _isText.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _datesPanel.Visibility = _isCalendar.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _hijriPanel.Visibility = _isHijri.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    internal static StackPanel Labelled(string label, Control field, double top)
    {
        var panel = new StackPanel { Margin = new Thickness(0, top, 0, 0) };
        var text = new Label { Content = label, Target = field, Padding = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(text);
        panel.Children.Add(field);
        AutomationProperties.SetLabeledBy(field, text);
        return panel;
    }

    internal static StackPanel OkCancel(System.Action save, params Button[] extra)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var button in extra) buttons.Children.Add(button);
        var cancel = new Button { Content = "Cancel", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Save", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), IsDefault = true };
        ok.Click += (_, _) => save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        return buttons;
    }

    // The action as typed, or null (with the reason shown) when something is missing.
    private TopicAction? Build()
    {
        var word = _word.Text.Trim();
        var otherWord = string.IsNullOrWhiteSpace(_otherWord.Text) ? null : _otherWord.Text.Trim();
        if (word.Length == 0) { Fail("Type what you'll say, for example Ramadan.", _word); return null; }
        if (word.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 4) { Fail("Keep what you say short: up to four words.", _word); return null; }
        foreach (var w in new[] { word, otherWord })
            if (w != null && _clash(w) is { } reason) { Fail(reason + " Choose another word.", _word); return null; }
        var action = new TopicAction { Word = word, OtherWord = otherWord };
        if (_isCalendar.IsChecked == true)
        {
            action.Kind = TopicAction.CalendarKind;
            var lines = _dates.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var bad = lines.FirstOrDefault(l => TopicAction.ParseDate(l) == null);
            if (bad != null) { Fail($"\"{bad}\" isn't a date. Write it as year-month-day, for example 2027-08-02.", _dates); return null; }
            if (lines.Length == 0) { Fail("Add at least one date.", _dates); return null; }
            action.Dates = lines.ToList();
        }
        else if (_isHijri.IsChecked == true)
        {
            action.Kind = TopicAction.HijriKind;
            action.HijriMonth = _hijriMonth.SelectedIndex + 1;
            action.HijriDay = _hijriDay.SelectedItem is int day ? day : 1;
        }
        else
        {
            action.Kind = TopicAction.TextKind;
            action.Text = string.IsNullOrWhiteSpace(_text.Text) ? null : _text.Text.Trim();
            action.OtherText = string.IsNullOrWhiteSpace(_otherText.Text) ? null : _otherText.Text.Trim();
            if (action.Text == null && action.OtherText == null) { Fail("Type the answer Keeper should say.", _text); return null; }
        }
        return action.ValidateAndNormalize() ?? Fail("Something in this question isn't valid.", _word);
    }

    private TopicAction? Fail(string message, Control focus)
    {
        _status.Text = message;
        Voice.Say(message);
        focus.Focus();
        return null;
    }

    private void Save()
    {
        if (Build() is not { } action) return;
        Action = action;
        DialogResult = true;
    }
}

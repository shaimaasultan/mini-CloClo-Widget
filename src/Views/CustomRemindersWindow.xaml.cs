using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MiniCloClo;

public partial class CustomRemindersWindow : Window
{
    private readonly ObservableCollection<CustomReminderSettings> _reminders;
    public CustomReminderSettings[] Reminders => _reminders.Select(reminder => reminder.Copy()).ToArray();

    public CustomRemindersWindow(IEnumerable<CustomReminderSettings> reminders)
    {
        InitializeComponent();
        _reminders = new ObservableCollection<CustomReminderSettings>(reminders.Select(reminder => reminder.Copy()));
        ReminderList.ItemsSource = _reminders;
        SortByNext();
        ReminderList.SelectedIndex = _reminders.Count > 0 ? 0 : -1;
        UpdateButtons();
        // Keep the "Next:" times current while the window is open.
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(30) };
        // Only when one changed, and keeping the focus on the selected one.
        var shown = string.Join("|", _reminders.Select(r => r.NextTime));
        clock.Tick += (_, _) =>
        {
            var now = string.Join("|", _reminders.Select(r => r.NextTime));
            if (now == shown) return;
            shown = now;
            var focused = ReminderList.IsKeyboardFocusWithin;
            SortByNext();
            ReminderList.Items.Refresh();
            if (focused && ReminderList.ItemContainerGenerator.ContainerFromIndex(ReminderList.SelectedIndex) is ListBoxItem item) item.Focus();
        };
        clock.Start();
        Closed += (_, _) => clock.Stop();
        Loaded += (_, _) =>
        {
            if (_reminders.Count == 0) AddButton.Focus();
            else if (ReminderList.ItemContainerGenerator.ContainerFromIndex(ReminderList.SelectedIndex) is ListBoxItem item) item.Focus();
            else ReminderList.Focus();
        };
    }

    // The one that fires soonest at the top, then later and later; those
    // turned off at the bottom. The selected one stays selected.
    private void SortByNext()
    {
        var selected = ReminderList.SelectedItem;
        var now = System.DateTime.Now;
        var order = _reminders.OrderBy(r => ReminderSchedule.Next(r, now) ?? System.DateTime.MaxValue).ToList();
        for (var i = 0; i < order.Count; i++)
        {
            var from = _reminders.IndexOf(order[i]);
            if (from != i) _reminders.Move(from, i);
        }
        if (selected != null) ReminderList.SelectedItem = selected;
    }

    private void UpdateButtons()
    {
        var selected = ReminderList.SelectedItem as CustomReminderSettings;
        ReminderDetails.Text = selected?.Describe()
            ?? (_reminders.Count == 0 ? "No reminders yet. Choose Add to make one." : "Select a reminder to change it.");
        EditButton.IsEnabled = ToggleButton.IsEnabled = RemoveButton.IsEnabled = TestButton.IsEnabled = selected != null;
        ToggleButton.Content = selected?.Enabled == false ? "_Turn on" : "_Turn off";
        AddButton.IsEnabled = _reminders.Count < WidgetSettings.MaxCustomReminders;
    }

    private void ReminderList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new CustomReminderEditorWindow(new CustomReminderSettings(), isNew: true) { Owner = this };
        if (editor.ShowDialog() != true) return;
        _reminders.Add(editor.Reminder);
        ReminderList.SelectedItem = editor.Reminder;
        SortByNext();
        ReminderList.ScrollIntoView(editor.Reminder);
        UpdateButtons();
        Voice.Say("Reminder added. Choose Save reminders to keep it.", "اتضاف التذكير. اختار حفظ التذكيرات عشان يتحفظ.");
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReminderList.SelectedItem is not CustomReminderSettings selected) return;
        var index = ReminderList.SelectedIndex;
        var editor = new CustomReminderEditorWindow(selected.Copy(), isNew: false) { Owner = this };
        if (editor.ShowDialog() != true) return;
        _reminders[index] = editor.Reminder;
        ReminderList.SelectedItem = editor.Reminder;
        SortByNext();
        ReminderList.ScrollIntoView(editor.Reminder);
        UpdateButtons();
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReminderList.SelectedItem is CustomReminderSettings selected) Voice.Say(selected.Message);
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReminderList.SelectedItem is not CustomReminderSettings selected) return;
        selected.Enabled = !selected.Enabled;
        SortByNext();
        ReminderList.Items.Refresh();
        ReminderList.ScrollIntoView(selected);
        UpdateButtons();
        Voice.Say(selected.Enabled ? "Reminder turned on." : "Reminder turned off.",
            selected.Enabled ? "التذكير اشتغل." : "التذكير اتقفل.");
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReminderList.SelectedItem is not CustomReminderSettings selected) return;
        var index = ReminderList.SelectedIndex;
        _reminders.Remove(selected);
        ReminderList.SelectedIndex = _reminders.Count == 0 ? -1 : System.Math.Min(index, _reminders.Count - 1);
        UpdateButtons();
        Voice.Say("Reminder removed.", "التذكير اتشال.");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

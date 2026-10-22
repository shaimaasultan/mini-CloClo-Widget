using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MiniCloClo;

// Pull tools (#35): turn pull commands on or off (they go online), and set
// each tool's settings - such as the country for the dates tool - without
// opening its file. Changes are kept with "Save and close".
internal sealed class PullToolsWindow : Window
{
    private readonly CheckBox _allow = new() { Content = "_Allow pull commands (they go online)", Margin = new Thickness(0, 0, 0, 4) };
    private readonly ListBox _list = new() { MinHeight = 120 };
    private readonly StackPanel _settingsPanel = new();
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly List<PullTool> _tools;
    private readonly Dictionary<string, Dictionary<string, string>> _values;

    public bool AllowPullCommands => _allow.IsChecked == true;
    public Dictionary<string, Dictionary<string, string>> ToolSettings => _values.ToDictionary(t => t.Key, t => new Dictionary<string, string>(t.Value));

    public PullToolsWindow(List<PullTool> tools, bool allow, IReadOnlyDictionary<string, Dictionary<string, string>> own)
    {
        _tools = tools;
        _values = tools.ToDictionary(t => t.Id, t => PullTools.EffectiveSettings(t, own));
        Title = "CloClo — Pull tools";
        Width = 600;
        Height = 560;
        MinWidth = 480;
        MinHeight = 420;
        FontSize = 15;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = SystemColors.WindowBrush;

        _allow.IsChecked = allow;
        _allow.Click += (_, _) => Voice.Say(
            AllowPullCommands ? "Pull commands: on. Each pull asks you first, because it goes online." : "Pull commands: off.",
            AllowPullCommands ? "أوامر السحب: شغالة. كل سحبة هسألك الأول." : "أوامر السحب: مقفولة.");
        var allowNote = new TextBlock
        {
            Text = "Saying \"pull dates\" runs that tool: it goes online, then its result is added to Questions and answers or read aloud. Mini CloClo itself never goes online; each pull asks you first.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 0, 0, 12), Foreground = SystemColors.GrayTextBrush,
        };
        AutomationProperties.SetHelpText(_allow, allowNote.Text);

        _list.ItemsSource = tools.Select(t => $"{t.Name}{(t.OtherName != null ? " — " + t.OtherName : "")}").ToList();
        _list.SelectionChanged += (_, _) => ShowTool();
        var listLabel = new Label { Content = "_Tools:", Target = _list, Padding = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetLabeledBy(_list, listLabel);

        var content = new DockPanel { Margin = new Thickness(18) };
        var top = new StackPanel();
        top.Children.Add(_allow);
        top.Children.Add(allowNote);
        top.Children.Add(listLabel);
        top.Children.Add(_list);
        DockPanel.SetDock(top, Dock.Top);
        content.Children.Add(top);

        var closeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0), IsCancel = true };
        var save = new Button { Content = "Sa_ve and close", MinWidth = 120, Padding = new Thickness(10, 5, 10, 5) };
        save.Click += (_, _) => DialogResult = true;
        closeRow.Children.Add(cancel);
        closeRow.Children.Add(save);
        DockPanel.SetDock(closeRow, Dock.Bottom);
        content.Children.Add(closeRow);

        var details = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        details.Children.Add(_description);
        details.Children.Add(_settingsPanel);
        content.Children.Add(new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = content;

        _list.SelectedIndex = tools.Count > 0 ? 0 : -1;
        if (tools.Count == 0) _description.Text = "No pull tools are installed.";
        Loaded += (_, _) =>
        {
            _allow.Focus();
            Voice.Say($"Pull tools. Pull commands are {(allow ? "on" : "off")}. {tools.Count} tools.",
                $"أدوات السحب. أوامر السحب {(allow ? "شغالة" : "مقفولة")}. فيه {tools.Count} أدوات.");
        };
    }

    private void ShowTool()
    {
        _settingsPanel.Children.Clear();
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _tools.Count) return;
        var tool = _tools[_list.SelectedIndex];
        _description.Text = $"\"{tool.Name}\" — goes online to {tool.Description}. Result: {(tool.Result == PullTool.ReadResult ? "read aloud" : "added to Questions and answers")}.";
        var values = _values[tool.Id];
        foreach (var key in tool.Settings.Keys)
        {
            var box = new TextBox { Text = values.GetValueOrDefault(key, ""), MaxLength = 100, Padding = new Thickness(3) };
            box.TextChanged += (_, _) => values[key] = box.Text.Trim();
            var label = new Label { Content = tool.SettingLabels.GetValueOrDefault(key, key), Target = box, Padding = new Thickness(0, 8, 0, 4) };
            AutomationProperties.SetLabeledBy(box, label);
            _settingsPanel.Children.Add(label);
            _settingsPanel.Children.Add(box);
        }
    }
}

using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace MiniCloClo;

// Learned phrases (#37): each mishearing Mini CloClo remembered, and the
// command it stands for - "Paul dates → pull dates". Remove one (or Delete),
// or all of them. Changes are kept at once. Keyboard and screen-reader
// friendly.
internal sealed class LearnedPhrasesWindow : Window
{
    private readonly ObservableCollection<LearnedPhrase> _phrases = new(LearnedPhrases.Load());
    private readonly ListBox _list = new() { MinHeight = 260 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _remove = new() { Content = "Re_move", MinWidth = 90, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _removeAll = new() { Content = "Remove _all", MinWidth = 90, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };

    public LearnedPhrasesWindow()
    {
        Title = "CloClo — Learned phrases";
        Width = 620;
        Height = 520;
        MinWidth = 460;
        MinHeight = 380;
        FontSize = 15;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = SystemColors.WindowBrush;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        _list.ItemsSource = _phrases;
        _list.DisplayMemberPath = null;
        _list.ItemTemplate = PhraseTemplate();
        AutomationProperties.SetName(_list, "Learned phrases");
        _list.SelectionChanged += (_, _) => Update();
        _remove.Click += (_, _) => Remove();
        _removeAll.Click += (_, _) => RemoveAll();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && _list.IsKeyboardFocusWithin)
            {
                Remove();
                e.Handled = true;
            }
        };

        var heading = new Label
        {
            Target = _list, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 10),
            Content = new AccessText
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "What the speech model _misheard, and the command it means. Learned when a command wasn't understood and the next one worked, and you said yes.",
            },
        };
        var close = new Button { Content = "Close", MinWidth = 90, Padding = new Thickness(10, 5, 10, 5), IsCancel = true, IsDefault = true };
        var buttons = new DockPanel { LastChildFill = false };
        buttons.Children.Add(_remove);
        buttons.Children.Add(_removeAll);
        DockPanel.SetDock(close, Dock.Right);
        buttons.Children.Add(close);

        var layout = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        layout.Children.Add(heading);
        layout.Children.Add(buttons);
        layout.Children.Add(_status);
        layout.Children.Add(_list);
        Content = layout;

        if (_phrases.Count > 0) _list.SelectedIndex = 0;
        Update();
        Loaded += (_, _) =>
        {
            if (_list.ItemContainerGenerator.ContainerFromIndex(_list.SelectedIndex) is ListBoxItem item) item.Focus();
            else close.Focus();
        };
    }

    // "Paul dates → pull dates", with when it was last used.
    private static DataTemplate PhraseTemplate()
    {
        var template = new DataTemplate(typeof(LearnedPhrase));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.MultiBinding
        {
            StringFormat = "{0}  →  {1}",
            Bindings =
            {
                new System.Windows.Data.Binding(nameof(LearnedPhrase.Heard)),
                new System.Windows.Data.Binding(nameof(LearnedPhrase.Name)),
            },
        });
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        template.VisualTree = text;
        return template;
    }

    private void Update()
    {
        _remove.IsEnabled = _list.SelectedItem != null;
        _removeAll.IsEnabled = _phrases.Count > 0;
        _status.Text = _phrases.Count == 0
            ? "Nothing learned yet."
            : _list.SelectedItem is LearnedPhrase p
                ? $"Learned {p.Learned:d}; used {p.Uses} time{(p.Uses == 1 ? "" : "s")}{(p.Uses > 0 ? $", last on {p.LastUsed:d}" : "")}."
                : $"{_phrases.Count} learned.";
        foreach (var phrase in _phrases)
            if (_list.ItemContainerGenerator.ContainerFromItem(phrase) is ListBoxItem item)
                AutomationProperties.SetName(item, $"{phrase.Heard} means {phrase.Name}");
    }

    private void Remove()
    {
        if (_list.SelectedItem is not LearnedPhrase phrase) return;
        var index = _list.SelectedIndex;
        LearnedPhrases.Remove(phrase.Key);
        _phrases.Remove(phrase);
        _list.SelectedIndex = _phrases.Count == 0 ? -1 : System.Math.Min(index, _phrases.Count - 1);
        Update();
        Voice.Say("Removed.", "اتشال.");
    }

    private void RemoveAll()
    {
        if (_phrases.Count == 0) return;
        if (MessageBox.Show(this, "Forget every learned phrase?", "Learned phrases", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        LearnedPhrases.RemoveAll();
        _phrases.Clear();
        Update();
        Voice.Say("All learned phrases removed.", "كل العبارات اللي اتعلمتها اتشالت.");
    }
}

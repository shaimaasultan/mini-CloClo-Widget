using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MiniCloClo;

// Contacts and calls: the people Mini CloClo may call, and which paired
// phone places the calls. Enter (or Call) on a contact calls them; the call
// itself is made by MainWindow once this window has closed. Every control
// has a label and an access key, and changes are spoken.
internal sealed class ContactsWindow : Window
{
    private readonly ObservableCollection<ContactSettings> _contacts;
    private readonly ListBox _list = new() { MinHeight = 200 };
    private readonly ComboBox _phone = new() { MinWidth = 220 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _call = new() { Content = "_Call", MinWidth = 90, Padding = new Thickness(12, 5, 12, 5), IsDefault = true, FontWeight = FontWeights.Bold };
    private readonly Button _edit = new() { Content = "_Edit…", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5) };
    private readonly Button _remove = new() { Content = "Re_move", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5) };
    private readonly Button _sayNumber = new() { Content = "Say the _number", Padding = new Thickness(10, 5, 10, 5) };
    private readonly Button _add = new() { Content = "_Add…", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5) };

    public ContactSettings[] Contacts => _contacts.Select(c => c.Copy()).ToArray();
    public BluetoothDialer.PairedDevice? Phone => _phone.SelectedItem as BluetoothDialer.PairedDevice;
    // Set when the window was closed with Call.
    public ContactSettings? CallContact { get; private set; }

    public ContactsWindow(IEnumerable<ContactSettings> contacts, string? phoneId, string? phoneName)
    {
        Title = "CloClo — Contacts and calls";
        Width = 560;
        Height = 600;
        MinWidth = 460;
        MinHeight = 480;
        FontSize = 15;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = SystemColors.WindowBrush;
        _contacts = new ObservableCollection<ContactSettings>(contacts.Select(c => c.Copy()));
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        // The phone: the saved one first, until the paired list has loaded.
        if (phoneId != null) _phone.ItemsSource = new List<BluetoothDialer.PairedDevice> { new(phoneId, phoneName ?? "My phone") };
        _phone.SelectedIndex = phoneId != null ? 0 : -1;
        _phone.DisplayMemberPath = nameof(BluetoothDialer.PairedDevice.Name);
        Loaded += async (_, _) =>
        {
            try
            {
                var paired = await BluetoothDialer.PairedDevicesAsync();
                var selected = Phone?.Id;
                _phone.ItemsSource = paired;
                _phone.SelectedItem = paired.FirstOrDefault(p => p.Id == selected);
            }
            catch { /* No Bluetooth: the saved phone stays shown. */ }
        };

        _list.ItemsSource = _contacts;
        _list.DisplayMemberPath = nameof(ContactSettings.Label);
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(ContactSettings.Spoken))));
        itemStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 4, 6, 4)));
        _list.ItemContainerStyle = itemStyle;
        _list.SelectionChanged += (_, _) => UpdateButtons();
        _list.MouseDoubleClick += (_, _) => Call();
        _list.SelectedIndex = _contacts.Count > 0 ? 0 : -1;

        var find = new Button { Content = "_Find my phone", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) };
        find.Click += async (_, _) => await FindPhoneAsync(find);
        _call.Click += (_, _) => Call();
        _add.Click += (_, _) => Add();
        _edit.Click += (_, _) => Edit();
        _remove.Click += (_, _) => Remove();
        _sayNumber.Click += (_, _) =>
        {
            if (_list.SelectedItem is ContactSettings c) Voice.Say($"{c.Name}: {ContactSettings.SpokenNumber(c.Number)}.");
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && _list.IsKeyboardFocusWithin) { Remove(); e.Handled = true; }
        };

        var content = new DockPanel { Margin = new Thickness(18) };

        var phoneRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var phoneLabel = new Label { Content = "_Phone that places calls:", Target = _phone, Padding = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLabeledBy(_phone, phoneLabel);
        phoneRow.Children.Add(phoneLabel);
        phoneRow.Children.Add(_phone);
        phoneRow.Children.Add(find);
        DockPanel.SetDock(phoneRow, Dock.Top);
        content.Children.Add(phoneRow);

        var listLabel = new Label { Content = "Con_tacts — Enter calls the one selected:", Target = _list, Padding = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetLabeledBy(_list, listLabel);
        DockPanel.SetDock(listLabel, Dock.Top);
        content.Children.Add(listLabel);

        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(_status);
        var actions = new WrapPanel();
        foreach (var button in new[] { _call, _add, _edit, _remove, _sayNumber })
        {
            button.Margin = new Thickness(0, 0, 6, 6);
            actions.Children.Add(button);
        }
        bottom.Children.Add(actions);
        var closeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0), IsCancel = true };
        var save = new Button { Content = "Sa_ve and close", MinWidth = 120, Padding = new Thickness(10, 5, 10, 5) };
        save.Click += (_, _) => DialogResult = true;
        closeRow.Children.Add(cancel);
        closeRow.Children.Add(save);
        bottom.Children.Add(closeRow);
        content.Children.Add(bottom);
        content.Children.Add(_list);
        Content = content;

        UpdateButtons();
        Loaded += (_, _) =>
        {
            if (_contacts.Count == 0) _add.Focus();
            else if (_list.ItemContainerGenerator.ContainerFromIndex(_list.SelectedIndex) is ListBoxItem item) item.Focus();
            else _list.Focus();
            Voice.Say(_contacts.Count == 0
                    ? "Contacts and calls. No contacts yet. Press Alt A to add one."
                    : $"Contacts and calls. {_contacts.Count} contacts. Choose one and press Enter to call.",
                _contacts.Count == 0 ? "جهات الاتصال. مفيش أسماء لسه. دوس ألت A عشان تضيف." : $"جهات الاتصال. فيه {_contacts.Count}. اختار اسم ودوس إنتر عشان تتصل.");
        };
    }

    private void UpdateButtons()
    {
        var selected = _list.SelectedItem is ContactSettings;
        _call.IsEnabled = _edit.IsEnabled = _remove.IsEnabled = _sayNumber.IsEnabled = selected;
        _add.IsEnabled = _contacts.Count < WidgetSettings.MaxContacts;
        _status.Text = _contacts.Count == 0
            ? "No contacts yet. Choose Add to save someone's name and number."
            : Phone == null ? "Choose the phone that places calls, or use Find my phone." : "";
    }

    private void Call()
    {
        if (_list.SelectedItem is not ContactSettings contact) return;
        if (Phone == null)
        {
            _status.Text = "Choose the phone that places calls first, or use Find my phone.";
            Voice.Say("Choose your phone first. Press Alt F to find it.", "اختار تليفونك الأول. دوس ألت F عشان ألاقيه.");
            return;
        }
        CallContact = contact.Copy();
        DialogResult = true;
    }

    private void Add()
    {
        var editor = new ContactEditorWindow(new ContactSettings(), isNew: true) { Owner = this };
        if (editor.ShowDialog() != true) return;
        _contacts.Add(editor.Contact);
        _list.SelectedItem = editor.Contact;
        UpdateButtons();
        Voice.Say($"{editor.Contact.Name} added. Choose Save and close to keep it.", $"اتضاف {editor.Contact.Name}. اختار حفظ عشان يتحفظ.");
    }

    private void Edit()
    {
        if (_list.SelectedItem is not ContactSettings contact) return;
        var index = _list.SelectedIndex;
        var editor = new ContactEditorWindow(contact.Copy(), isNew: false) { Owner = this };
        if (editor.ShowDialog() != true) return;
        _contacts[index] = editor.Contact;
        _list.SelectedIndex = index;
        UpdateButtons();
    }

    private void Remove()
    {
        if (_list.SelectedItem is not ContactSettings contact) return;
        var index = _list.SelectedIndex;
        _contacts.Remove(contact);
        _list.SelectedIndex = _contacts.Count == 0 ? -1 : Math.Min(index, _contacts.Count - 1);
        UpdateButtons();
        Voice.Say($"{contact.Name} removed.", $"اتشال {contact.Name}.");
    }

    // Asks each paired device whether it can place calls; headsets can't.
    private async System.Threading.Tasks.Task FindPhoneAsync(Button button)
    {
        button.IsEnabled = false;
        _status.Text = "Looking for your phone over Bluetooth… keep it nearby and unlocked.";
        Voice.Say("Looking for your phone. Keep it nearby and unlocked.", "بدور على تليفونك. خليه قريب ومفتوح.");
        try
        {
            var phones = await BluetoothDialer.FindPhonesAsync();
            if (phones.Count == 0)
            {
                _status.Text = "No paired phone answered. Pair it in Windows Settings > Bluetooth & devices, turn on its Bluetooth, and try again.";
                Voice.Say("I couldn't find a paired phone. Pair it in Windows Bluetooth settings, then try again.",
                    "ما لقيتش تليفون. اعمل له اقتران من إعدادات البلوتوث وجرب تاني.");
                return;
            }
            var paired = await BluetoothDialer.PairedDevicesAsync();
            _phone.ItemsSource = paired;
            _phone.SelectedItem = paired.FirstOrDefault(p => p.Id == phones[0].Id);
            _status.Text = $"Found {phones[0].Name}. Calls will be placed on it.";
            Voice.Say($"Found your phone, {phones[0].Name}.", $"لقيت تليفونك، {phones[0].Name}.");
            UpdateButtons();
        }
        catch (Exception ex)
        {
            _status.Text = $"Bluetooth isn't available ({ex.GetType().Name}).";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}

// One contact: a name, another name it may be called by, and the number.
internal sealed class ContactEditorWindow : Window
{
    private readonly TextBox _name = new() { MaxLength = 40, Padding = new Thickness(3) };
    private readonly TextBox _otherName = new() { MaxLength = 40, Padding = new Thickness(3) };
    private readonly TextBox _number = new() { MaxLength = 30, Padding = new Thickness(3) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public ContactSettings Contact { get; private set; }

    public ContactEditorWindow(ContactSettings contact, bool isNew)
    {
        Contact = contact.Copy();
        Title = isNew ? "New contact" : "Edit contact";
        Width = 460;
        FontSize = 15;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Assertive);
        _name.Text = Contact.Name;
        _otherName.Text = Contact.OtherName ?? "";
        _number.Text = Contact.Number;

        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(Labelled("_Name (what you say: \"call Mom\")", _name, 0));
        content.Children.Add(Labelled("_Other name, for example in Arabic (optional)", _otherName, 12));
        content.Children.Add(Labelled("Phone n_umber", _number, 12));
        content.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var sayIt = new Button { Content = "Say the num_ber", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        sayIt.Click += (_, _) =>
        {
            var number = ContactSettings.CleanNumber(_number.Text);
            Voice.Say(ContactSettings.IsDialable(number) ? ContactSettings.SpokenNumber(number) : "There is no number yet.");
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Save", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), IsDefault = true };
        save.Click += (_, _) => Save();
        buttons.Children.Add(sayIt);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        content.Children.Add(buttons);
        Content = content;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    private static StackPanel Labelled(string label, Control field, double top)
    {
        var panel = new StackPanel { Margin = new Thickness(0, top, 0, 0) };
        var text = new Label { Content = label, Target = field, Padding = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(text);
        panel.Children.Add(field);
        AutomationProperties.SetLabeledBy(field, text);
        return panel;
    }

    private void Save()
    {
        var number = ContactSettings.CleanNumber(_number.Text);
        var candidate = new ContactSettings
        {
            Name = _name.Text.Trim(),
            OtherName = string.IsNullOrWhiteSpace(_otherName.Text) ? null : _otherName.Text.Trim(),
            Number = number,
        };
        if (string.IsNullOrWhiteSpace(candidate.Name))
        {
            _status.Text = "Type the name you will say to call them.";
            _name.Focus();
            return;
        }
        if (!ContactSettings.IsDialable(number))
        {
            _status.Text = "Type the phone number: digits, with + at the start for an international number.";
            _number.Focus();
            return;
        }
        if (!candidate.IsValid())
        {
            _status.Text = "Names can be up to 40 characters.";
            return;
        }
        Contact = candidate;
        DialogResult = true;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MiniCloClo;

// Ctrl+Alt shortcuts that work from any app, so nobody has to find the
// widget on the screen: T the time, D the date, P the next prayer, A all of
// today's prayer times, Q the Quran, R reminders, K contacts and calls, V
// talk (press again to finish), L read the selected text aloud, S stop
// sounds, M the menu, H help.
// Registered with Windows'
// RegisterHotKey, which only reports these exact key combinations - no
// other keystroke is seen. A combination another app already uses is
// skipped, and the help says which ones work. Each one can be turned off on
// its own in the menu (Voice and speech, Keyboard shortcuts), or all of them.
public partial class MainWindow
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    private HwndSource? _hotkeySource;
    private bool _hotkeysRegistered;
    private readonly List<int> _registeredHotkeys = [];

    // Each shortcut, with what the menu and the spoken help call it.
    private (char Key, string English, string Arabic, Action Run)[] Hotkeys =>
    [
        ('T', "the time", "الساعة", SayTime),
        ('D', "the date", "التاريخ", SayDate),
        ('P', "the next prayer", "الصلاة الجاية", SayNextPrayer),
        ('A', "all prayer times", "كل المواقيت", SayPrayerTimes),
        ('Q', "the Quran", "القرآن", OpenQuran),
        ('R', "reminders", "التذكيرات", OpenCustomReminders),
        ('K', "contacts and calls", "جهات الاتصال", OpenContacts),
        ('I', "questions and answers", "الأسئلة والأجوبة", OpenTopics),
        ('Y', "medicine taken", "خدت الدوا", MedicineTaken),
        ('V', "talk", "الكلام", ToggleKeyboardVoiceCommand),
        ('L', "read the selected text", "قراية الكلام المتحدد", ReadSelection),
        ('S', "stop sounds", "إيقاف الصوت", () => { if (!StopSounds()) Voice.Say("Nothing is playing.", "مفيش حاجة شغالة."); }),
        ('M', "the menu", "القائمة", OpenMenu),
        ('H', "help", "المساعدة", SayHelp),
    ];

    private bool ShortcutOn(char key) => !_settings.ShortcutsOff.Contains(key.ToString());

    // The shortcuts working now, as the help says them: "T the time, D the date".
    private (string English, string Arabic) WorkingShortcuts()
    {
        var keys = Hotkeys;
        var working = _registeredHotkeys.Order().Select(id => keys[id - 1]).ToList();
        return (string.Join(", ", working.Select(k => $"{k.Key} {k.English}")),
                string.Join("، ", working.Select(k => $"{k.Key} {k.Arabic}")));
    }

    // One shortcut on or off, from the menu.
    private void ToggleShortcut(char key)
    {
        var letter = key.ToString();
        var on = !ShortcutOn(key);
        if (on) _settings.ShortcutsOff.Remove(letter);
        else _settings.ShortcutsOff.Add(letter);
        _settings.Save();
        UpdateHotkeys();
        var (_, english, arabic, _) = Hotkeys.First(k => k.Key == key);
        Voice.Say($"Control Alt {key}, {english}, {(on ? "on" : "off")}.", $"كنترول ألت {key}، {arabic}، {(on ? "شغال" : "مقفول")}.");
    }

    private void UpdateHotkeys()
    {
        UnregisterHotkeys();
        if (!_settings.KeyboardShortcutsEnabled || _closing) return;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _hotkeySource ??= HwndSource.FromHwnd(hwnd);
        _hotkeySource?.AddHook(HotkeyHook);
        var keys = Hotkeys;
        var failed = new List<char>();
        for (var id = 0; id < keys.Length; id++)
        {
            if (!ShortcutOn(keys[id].Key)) continue;
            if (RegisterHotKey(hwnd, id + 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, keys[id].Key)) _registeredHotkeys.Add(id + 1);
            else failed.Add(keys[id].Key);
        }
        _hotkeysRegistered = _registeredHotkeys.Count > 0;
        if (failed.Count > 0)
            ShowLabel("Some shortcuts are taken", "Another app uses Ctrl+Alt+" + string.Join(", ", failed), "⌨");
    }

    private void UnregisterHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        foreach (var id in _registeredHotkeys) UnregisterHotKey(hwnd, id);
        _registeredHotkeys.Clear();
        _hotkeysRegistered = false;
        _hotkeySource?.RemoveHook(HotkeyHook);
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        var id = wParam.ToInt32();
        var keys = Hotkeys;
        if (id >= 1 && id <= keys.Length && _registeredHotkeys.Contains(id))
        {
            handled = true;
            // Run after the hook returns, so a dialog opened by it doesn't
            // nest inside the message handler.
            var run = keys[id - 1].Run;
            Dispatcher.BeginInvoke(run);
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MiniCloClo;

// Ctrl+Alt shortcuts that work from any app, so nobody has to find the
// widget on the screen: T the time, D the date, P the next prayer, A all of
// today's prayer times, Q the Quran, R reminders, K contacts and calls, V
// talk (press again to finish), S stop sounds, M the menu, H help.
// Registered with Windows'
// RegisterHotKey, which only reports these exact key combinations - no
// other keystroke is seen. A combination another app already uses is
// skipped, and the help says which ones work.
public partial class MainWindow
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    private HwndSource? _hotkeySource;
    private bool _hotkeysRegistered;
    private readonly List<int> _registeredHotkeys = [];

    private (char Key, Action Run)[] Hotkeys =>
    [
        ('T', SayTime),
        ('D', SayDate),
        ('P', SayNextPrayer),
        ('A', SayPrayerTimes),
        ('Q', OpenQuran),
        ('R', OpenCustomReminders),
        ('K', OpenContacts),
        ('I', OpenTopics),
        ('Y', MedicineTaken),
        ('V', ToggleKeyboardVoiceCommand),
        ('S', () => { if (!StopSounds()) Voice.Say("Nothing is playing.", "مفيش حاجة شغالة."); }),
        ('M', OpenMenu),
        ('H', SayHelp),
    ];

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

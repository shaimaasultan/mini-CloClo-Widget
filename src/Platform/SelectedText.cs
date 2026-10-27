using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace MiniCloClo;

// The text selected in another app, to read it aloud - on this PC only.
// First the way Narrator does it, through Windows UI Automation (the app's
// own selection: Word, Edge, Notepad...); then, for apps that don't share it
// that way, by copying it (Ctrl+C) and putting back what the clipboard held.
// Clicking the widget makes it the active window, so the last app used
// before it is kept to read from.
internal static class SelectedText
{
    // More than this is cut (a select-all of a long document).
    public const int MaxChars = 20000;

    private static IntPtr _lastOtherWindow;
    private static IntPtr _hook;
    private static WinEventDelegate? _onForeground; // kept alive while hooked

    // Follows the active window, keeping the last one that isn't Mini
    // CloClo's own (nor the taskbar or desktop). Call on the UI thread.
    public static void StartTracking()
    {
        if (_hook != IntPtr.Zero) return;
        _onForeground = (_, _, hwnd, _, _, _, _) => Remember(hwnd);
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _onForeground, 0, 0, WinEventOutOfContext);
        Remember(GetForegroundWindow());
    }

    public static void StopTracking()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private static void Remember(IntPtr hwnd)
    {
        if (IsOtherApp(hwnd)) _lastOtherWindow = hwnd;
    }

    private static bool IsOtherApp(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return false;
        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW");
    }

    // The app to read from: the active one, or - when Mini CloClo is active
    // (its icon or menu was clicked) - the last one used before it.
    public static IntPtr TargetWindow(out bool active)
    {
        var foreground = GetForegroundWindow();
        active = IsOtherApp(foreground);
        if (active) return foreground;
        return IsWindow(_lastOtherWindow) ? _lastOtherWindow : IntPtr.Zero;
    }

    // Short notes on how the text was found, and a way to try copying
    // alone - for the debug test.
    internal static Action<string>? Trace;
    internal static bool SkipAutomation;

    // The selected text (null: nothing is selected, or it can't be read).
    // Call on the UI thread: the clipboard needs it.
    public static async Task<string?> ReadAsync(IntPtr window, bool active)
    {
        if (window == IntPtr.Zero) return null;
        var text = SkipAutomation ? null : await Task.Run(() => FromAutomation(window, active));
        if (Clean(text) is { } found)
        {
            Trace?.Invoke("from UI Automation");
            return found;
        }
        text = await CopyAsync(window, active);
        if (Clean(text) is { } copied)
        {
            Trace?.Invoke("by copying");
            return copied;
        }
        return null;
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return text.Length > MaxChars ? text[..MaxChars] : text;
    }

    // ---- UI Automation ----

    private static string? FromAutomation(IntPtr window, bool active)
    {
        try
        {
            // The focused element when the app is active - a text box, a
            // document - or one of its parents that holds the text.
            if (active)
            {
                for (var element = AutomationElement.FocusedElement; element != null; element = TreeWalker.ControlViewWalker.GetParent(element))
                {
                    if (Selection(element) is { } selected) return selected;
                    if (element.Current.NativeWindowHandle == window.ToInt32()) break;
                }
            }
            // Otherwise any of the window's text parts with a selection.
            var root = AutomationElement.FromHandle(window);
            if (Selection(root) is { } inRoot) return inRoot;
            foreach (AutomationElement part in root.FindAll(TreeScope.Descendants,
                         new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true)))
                if (Selection(part) is { } selected) return selected;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
        {
            Trace?.Invoke($"UI Automation: {ex.GetType().Name}");
        }
        return null;
    }

    private static string? Selection(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
        var ranges = ((TextPattern)pattern).GetSelection();
        var text = string.Join("\n", ranges.Select(r => r.GetText(MaxChars)).Where(t => !string.IsNullOrWhiteSpace(t)));
        return text.Length > 0 ? text : null;
    }

    // ---- Copying, for apps that don't share their selection ----

    private static async Task<string?> CopyAsync(IntPtr window, bool active)
    {
        if (!active)
        {
            // Back to the app, so Ctrl+C reaches it; it keeps its selection.
            SetForegroundWindow(window);
            await Task.Delay(200);
            if (GetForegroundWindow() != window) return null;
        }
        // Ctrl+Alt+L may still be held: Ctrl+C must go alone.
        for (var waited = 0; waited < 15 && (Down(VkControl) || Down(VkMenu) || Down(VkShift)); waited++) await Task.Delay(100);
        var saved = SaveClipboard();
        var before = GetClipboardSequenceNumber();
        Press(VkControl, down: true);
        Press(VkC, down: true);
        Press(VkC, down: false);
        Press(VkControl, down: false);
        string? text = null;
        for (var waited = 0; waited < 15 && GetClipboardSequenceNumber() == before; waited++) await Task.Delay(50);
        if (GetClipboardSequenceNumber() != before)
        {
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (COMException) { }
            RestoreClipboard(saved);
        }
        return text;
    }

    // What the clipboard holds, format by format, to put back after copying.
    private static DataObject? SaveClipboard()
    {
        try
        {
            if (Clipboard.GetDataObject() is not { } data) return null;
            var copy = new DataObject();
            var any = false;
            foreach (var format in data.GetFormats(false))
            {
                try
                {
                    if (data.GetData(format, false) is { } value)
                    {
                        copy.SetData(format, value, false);
                        any = true;
                    }
                }
                catch (Exception ex) when (ex is COMException or OutOfMemoryException or InvalidOperationException) { }
            }
            return any ? copy : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static void RestoreClipboard(DataObject? saved)
    {
        try
        {
            if (saved == null)
            {
                Clipboard.Clear();
                return;
            }
            // Not added to the clipboard history (Win+V) a second time.
            saved.SetData("ExcludeClipboardContentFromMonitorProcessing", new System.IO.MemoryStream(new byte[] { 0 }), false);
            Clipboard.SetDataObject(saved, copy: true);
        }
        catch (COMException) { }
    }

    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkC = 0x43;
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static void Press(int key, bool down) => keybd_event((byte)key, 0, down ? 0u : 2u, UIntPtr.Zero);

    private const uint EventSystemForeground = 3, WinEventOutOfContext = 0;
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}

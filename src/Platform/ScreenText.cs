using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace MiniCloClo;

// Text in a picture on the screen, read with Windows' own text recognition
// (Windows.Media.Ocr) - on this PC, with the recognition languages Windows
// has (English, and Arabic once the Arabic language is added). The pixels are
// captured in memory, recognized, and let go: nothing is saved or sent.
internal static class ScreenText
{
    // Short notes on what was read, for the debug test.
    internal static Action<string>? Trace;

    public static bool CanRecognize => Engine("en") != null || Engine("ar") != null;

    // The area to read when nothing is selected: the picture under the mouse
    // pointer, when it's over one in that window; otherwise the window's page
    // (a web page, a document) or the whole window. Physical pixels.
    public static Rect? AreaToRead(IntPtr window, bool underPointer)
    {
        try
        {
            if (underPointer && GetCursorPos(out var pointer) && GetAncestor(WindowFromPoint(pointer), GaRoot) == window)
            {
                var element = AutomationElement.FromPoint(new Point(pointer.X, pointer.Y));
                if (element.Current.ControlType == ControlType.Image && Usable(element.Current.BoundingRectangle) is { } picture)
                {
                    Trace?.Invoke("the picture under the pointer");
                    return picture;
                }
            }
            var root = AutomationElement.FromHandle(window);
            var page = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            if (page != null && Usable(page.Current.BoundingRectangle) is { } document)
            {
                Trace?.Invoke("the window's page");
                return document;
            }
            Trace?.Invoke("the whole window");
            return Usable(root.Current.BoundingRectangle);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
        {
            Trace?.Invoke($"UI Automation: {ex.GetType().Name}");
            return GetWindowRect(window, out var r) ? Usable(new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)) : null;
        }
    }

    // On the screen, and big enough to hold a word.
    private static Rect? Usable(Rect area)
    {
        if (area.IsEmpty) return null;
        var screen = new Rect(GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen), GetSystemMetrics(SmCyVirtualScreen));
        area.Intersect(screen);
        return area.IsEmpty || area.Width < 24 || area.Height < 12 ? null : area;
    }

    // The text in that area, line by line (null: none found). Each line is
    // recognized in English and in Arabic, and the reading with more letters
    // of its own script wins - so an Arabic page is read as Arabic and an
    // English one as English.
    public static async Task<string?> ReadAsync(Rect area)
    {
        var english = Engine("en");
        var arabic = Engine("ar");
        if (english == null && arabic == null) return null;
        using var bitmap = Capture(area);
        if (bitmap == null) return null;
        var englishText = english != null ? Lines(await english.RecognizeAsync(bitmap), rightToLeft: false) : "";
        var arabicText = arabic != null ? Lines(await arabic.RecognizeAsync(bitmap), rightToLeft: true) : "";
        var latin = englishText.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        var arabicLetters = arabicText.Count(c => c is >= 'ء' and <= 'ي');
        Trace?.Invoke($"English reading {latin} letters, Arabic reading {arabicLetters} letters");
        var text = arabicLetters > latin ? arabicText : englishText;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    // Each line's words in reading order: Arabic recognition gives them as
    // they sit on the screen, left to right, so an Arabic line is put back
    // together from the right.
    private static string Lines(OcrResult result, bool rightToLeft) => string.Join("\n", result.Lines.Select(line =>
        rightToLeft ? string.Join(" ", line.Words.OrderByDescending(word => word.BoundingRect.X).Select(word => word.Text)) : line.Text));

    private static OcrEngine? Engine(string language)
    {
        var tag = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l => l.LanguageTag.StartsWith(language, StringComparison.OrdinalIgnoreCase));
        return tag == null ? null : OcrEngine.TryCreateFromLanguage(tag);
    }

    // The area's pixels, in memory. Small areas are doubled - recognition
    // reads small text better larger - and big ones kept within what it takes.
    private static SoftwareBitmap? Capture(Rect area)
    {
        var x = (int)area.X;
        var y = (int)area.Y;
        var width = (int)area.Width;
        var height = (int)area.Height;
        var scale = Math.Max(width, height) < 1200 ? 2.0 : 1.0;
        scale = Math.Min(scale, (OcrEngine.MaxImageDimension - 1) / (double)Math.Max(width, height));
        var outWidth = Math.Max(1, (int)(width * scale));
        var outHeight = Math.Max(1, (int)(height * scale));

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var picture = CreateCompatibleBitmap(screen, outWidth, outHeight);
        var old = SelectObject(memory, picture);
        try
        {
            SetStretchBltMode(memory, Halftone);
            if (!StretchBlt(memory, 0, 0, outWidth, outHeight, screen, x, y, width, height, SrcCopy | CaptureBlt)) return null;
            var info = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = outWidth, biHeight = -outHeight, // top-down
                biPlanes = 1, biBitCount = 32,
            };
            var pixels = new byte[outWidth * outHeight * 4];
            SelectObject(memory, old);
            if (GetDIBits(memory, picture, 0, (uint)outHeight, pixels, ref info, 0) == 0) return null;
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, outWidth, outHeight, BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(picture);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    private const uint GaRoot = 2;
    private const int SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000, Halftone = 4;
    private const int SmXVirtualScreen = 76, SmYVirtualScreen = 77, SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sWidth, int sHeight, int rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
}

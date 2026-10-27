using System;
using System.Threading.Tasks;

namespace MiniCloClo;

// Read aloud (#42): select text in any app, then click the 🔊 icon, press
// Ctrl+Alt+L or say "read" / "اقرا". Arabic is read with the Arabic voice and
// English with the English one - a text with both is read with both - and
// everything stays on this PC. With nothing selected, the text in the
// picture under the mouse pointer - or on the window's page - is recognized
// (ScreenText, Windows' own offline text recognition) and read; "read the
// screen" / "اقرا الشاشة" (Ctrl+Alt+W) reads the whole window that way.
// "Stop" stops it; "again" reads it once more; a reminder pauses it and it
// carries on.
public partial class MainWindow
{
    private bool _readingAloud;

    internal void ReadSelection() => _ = ReadAloudAsync(screen: false);
    internal void ReadScreen() => _ = ReadAloudAsync(screen: true);

    private async Task ReadAloudAsync(bool screen)
    {
        if (_readingAloud) return;
        _readingAloud = true;
        try
        {
            var window = SelectedText.TargetWindow(out var active);
            var text = screen ? null : await SelectedText.ReadAsync(window, active);
            if (text == null && window != IntPtr.Zero && ScreenText.CanRecognize)
            {
                // A picture under the pointer counts only when the pointer is
                // on that app - not on the widget's own 🔊 icon.
                var area = await Task.Run(() => ScreenText.AreaToRead(window, underPointer: !screen));
                if (area is { } found)
                {
                    ShowLabel("Reading the screen…", "Recognizing the text", "🔊", seconds: 10);
                    text = await RecognizeWithoutTheWidgetAsync(found);
                }
            }
            if (_closing) return;
            if (text == null)
            {
                ShowLabel(screen ? "No text found" : "Nothing to read", screen
                    ? "No text could be recognized in that window"
                    : "Select some text, or point at a picture, then click 🔊 or press Ctrl+Alt+L", "🔊", seconds: 20);
                Voice.Say(screen
                        ? "I can't find any text to read in that window."
                        : "I can't find anything to read. Select some text, or point at a picture with the mouse, then click the read icon, or say read.",
                    screen
                        ? "مش لاقي كلام أقراه في الشباك ده."
                        : "مش لاقي حاجة أقراها. حدد الكلام، أو حط الماوس على الصورة، وبعدين دوس على أيقونة القراية، أو قول اقرا.");
                return;
            }
            var cut = text.Length >= SelectedText.MaxChars;
            if (cut) text = text[..SelectedText.MaxChars];
            Answer("Reading aloud",
                (cut ? "It's long, so I'll read the beginning. " : "") + text,
                (cut ? "الكلام طويل، فهقرا أوله. " : "") + text, 30);
        }
        finally
        {
            _readingAloud = false;
        }
    }

    // The widget is hidden for the moment the screen is captured, so its own
    // clock and labels aren't read as part of the page.
    private async Task<string?> RecognizeWithoutTheWidgetAsync(System.Windows.Rect area)
    {
        var opacity = Opacity;
        Opacity = 0;
        try
        {
            await Task.Delay(120);
            return await ScreenText.ReadAsync(area);
        }
        finally
        {
            Opacity = opacity;
        }
    }
}

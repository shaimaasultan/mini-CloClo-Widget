using System;

namespace MiniCloClo;

// Read the selected text aloud: select text in any app, then click the 🔊
// icon, press Ctrl+Alt+L or say "read this" / "اقرا ده". Arabic is read with
// the Arabic voice and English with the English one - a text with both is
// read with both - and everything stays on this PC (SelectedText). "Stop"
// stops it; "again" reads it once more; a reminder pauses it and it carries
// on.
public partial class MainWindow
{
    private bool _readingSelection;

    internal async void ReadSelection()
    {
        if (_readingSelection) return;
        _readingSelection = true;
        try
        {
            var window = SelectedText.TargetWindow(out var active);
            var text = await SelectedText.ReadAsync(window, active);
            if (_closing) return;
            if (text == null)
            {
                ShowLabel("Nothing selected", "Select some text, then click 🔊 or press Ctrl+Alt+L", "🔊", seconds: 20);
                Voice.Say("I can't find any selected text. Select some text first, then click the read icon, or press Control Alt L.",
                    "مش لاقي كلام متحدد. حدد الكلام الأول، وبعدين دوس على أيقونة القراية، أو كنترول ألت L.");
                return;
            }
            var cut = text.Length >= SelectedText.MaxChars;
            Answer("Reading aloud",
                (cut ? "It's long, so I'll read the beginning. " : "") + text,
                (cut ? "الكلام طويل، فهقرا أوله. " : "") + text, 30);
        }
        finally
        {
            _readingSelection = false;
        }
    }
}

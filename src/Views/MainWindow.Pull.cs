using System;
using System.Linq;
using System.Threading.Tasks;

namespace MiniCloClo;

// "Pull dates" (#35): confirm, run the tool's script with its settings,
// then import its result into Questions and answers - and say the topics
// added - or read its items aloud. Pull commands are off until turned on
// (they go online), every pull is confirmed with "yes", and only fixed
// sentences plus the tool's and topics' names are spoken, apart from a
// "read" tool's items, which are what was asked for.
public partial class MainWindow
{
    private static readonly TimeSpan PullConfirmTime = TimeSpan.FromSeconds(45);
    private PullTool? _pendingPull;
    private DateTime _pendingPullUntil;
    private bool _pulling;

    private static string Said(PullTool tool) => tool.Name;
    private static string SaidArabic(PullTool tool) => tool.OtherName ?? tool.Name;

    // "Pull ..." heard: true when it was a pull command (handled here).
    private bool TryPullCommand(string heard)
    {
        var tools = PullTools.Load();
        if (PullVoiceCommand.TryParse(heard, tools) is not { } request) return false;
        Keeper.SetVoiceStatus("complete");
        var names = string.Join(", ", tools.Select(Said));
        var namesArabic = string.Join("، ", tools.Select(SaidArabic));
        if (tools.Count == 0)
        {
            ShowLabel("No pull tools", "None are installed", "⬇");
            Voice.Say("There are no pull tools installed.", "مفيش أدوات سحب متسطبة.");
            return true;
        }
        if (request is PullVoiceCommand.ListRequest)
        {
            ShowLabel("Pull tools", names, "⬇");
            Voice.Say($"I can pull: {names}. Say pull and a name.", $"أقدر أسحب: {namesArabic}. قول اسحب واسم الأداة.");
            return true;
        }
        if (!_settings.AllowPullCommands)
        {
            ShowLabel("Pull commands are off", "They go online — turn them on in the menu, Pull tools", "⬇", seconds: 20);
            Voice.Say("Pull commands are off, because they go online. To use them, open the menu, choose Pull tools, and turn them on.",
                "أوامر السحب مقفولة لأنها بتدخل على الإنترنت. عشان تشغلها، افتح القائمة واختار أدوات السحب وشغلها.");
            return true;
        }
        var pull = (PullVoiceCommand.PullRequest)request;
        if (pull.Tool is not { } tool)
        {
            // The name heard is never said back: only the tools' own names.
            ShowLabel(pull.Unknown != null ? "No pull tool by that name" : "Which one?", $"Say pull and: {names}", "⬇", seconds: 20);
            Voice.Say(pull.Unknown != null ? $"I don't have a pull tool by that name. I can pull: {names}." : $"Which one? Say pull and a name: {names}.",
                pull.Unknown != null ? $"مفيش أداة سحب بالاسم ده. أقدر أسحب: {namesArabic}." : $"أنهي واحدة؟ قول اسحب واسمها: {namesArabic}.");
            return true;
        }
        if (_pulling)
        {
            Voice.Say("I'm already pulling. Please wait.", "بسحب دلوقتي. استنى شوية.");
            return true;
        }
        AskToPull(tool, null);
        return true;
    }

    private void AskToPull(PullTool tool, string? heard)
    {
        _pendingCall = null;
        _pendingPull = tool;
        _pendingPullUntil = DateTime.UtcNow + PullConfirmTime;
        ShowLabel($"Pull {Said(tool)}?", heard != null ? $"Heard: {heard} — say yes, or cancel" : $"Goes online to {tool.Description} — say yes, or cancel",
            "⬇", seconds: (int)PullConfirmTime.TotalSeconds);
        Voice.Say(
            (heard != null ? "I didn't catch that. " : "") + $"Pull {Said(tool)}? This goes online to {tool.Description}. Talk to me again and say yes, or cancel.",
            (heard != null ? "ما فهمتش. " : "") + $"أسحب {SaidArabic(tool)}؟ ده هيدخل على الإنترنت عشان {tool.OtherDescription ?? tool.Description}. كلمني تاني وقول نعم، أو لا عشان ألغي.");
    }

    private bool PullPending
    {
        get
        {
            if (_pendingPull != null && DateTime.UtcNow > _pendingPullUntil) _pendingPull = null;
            return _pendingPull != null;
        }
    }

    // "Yes" while a pull waits for it.
    private bool TryConfirmPendingPull(string heard)
    {
        if (!PullPending || !CallVoiceCommand.IsYes(heard)) return false;
        var tool = _pendingPull!;
        _pendingPull = null;
        Keeper.SetVoiceStatus("complete");
        _ = RunPullAsync(tool);
        return true;
    }

    // An answer that wasn't understood asks again; the pull keeps waiting.
    private bool AskAgainForPendingPull(string heard)
    {
        if (!PullPending) return false;
        Keeper.SetVoiceStatus("failed");
        AskToPull(_pendingPull!, heard);
        return true;
    }

    private bool CancelPendingPull()
    {
        if (!PullPending) return false;
        _pendingPull = null;
        ShowLabel("Pull cancelled", "", "⬇");
        Voice.Say("Pull cancelled.", "اتلغى السحب.");
        return true;
    }

    private async Task RunPullAsync(PullTool tool)
    {
        if (_pulling) return;
        _pulling = true;
        try
        {
            ShowLabel($"Pulling {Said(tool)}…", "Running the tool", "⬇", seconds: tool.TimeoutSeconds);
            await Voice.SayAndWaitAsync($"Running the {Said(tool)} tool.", $"بشغل أداة {SaidArabic(tool)}.", TimeSpan.FromSeconds(6));
            var (outcome, file) = await PullTools.RunAsync(tool, PullTools.EffectiveSettings(tool, _settings.PullToolSettings), _settings.VoiceCommandsInArabic);
            if (_closing) return;
            if (outcome != PullTools.Outcome.Done || file == null)
            {
                var (english, arabic) = outcome switch
                {
                    PullTools.Outcome.TimedOut => ($"The {Said(tool)} tool took too long, so I stopped it. Try again later.", $"أداة {SaidArabic(tool)} طولت، فوقفتها. جرب تاني بعدين."),
                    PullTools.Outcome.NoResult => ($"The {Said(tool)} tool finished, but it didn't give me anything.", $"أداة {SaidArabic(tool)} خلصت، بس ما ادتنيش حاجة."),
                    _ => ($"The {Said(tool)} tool didn't finish. Check the internet connection and its settings, then try again.", $"أداة {SaidArabic(tool)} ما كملتش. اتأكد من الإنترنت وإعداداتها، وجرب تاني."),
                };
                ShowLabel($"Pull {Said(tool)} didn't work", english, "⬇", seconds: 30);
                Voice.Say(english, arabic);
                return;
            }
            if (tool.Result == PullTool.ReadResult) ReadPulled(tool, file);
            else await ImportPulledAsync(tool, file);
        }
        catch (Exception ex)
        {
            ShowLabel($"Pull {Said(tool)} didn't work", ex.GetType().Name, "⬇", seconds: 30);
            Voice.Say($"The {Said(tool)} tool couldn't run.", $"أداة {SaidArabic(tool)} ما اشتغلتش.");
        }
        finally
        {
            _pulling = false;
        }
    }

    private async Task ImportPulledAsync(PullTool tool, string file)
    {
        ShowLabel($"Pulling {Said(tool)}…", "Importing the file", "⬇", seconds: 30);
        await Voice.SayAndWaitAsync("Importing the file.", "بستورد الملف.", TimeSpan.FromSeconds(5));
        var imported = TopicImport.ReadFile(file);
        if (imported.Count == 0)
        {
            ShowLabel($"Pull {Said(tool)}", "Nothing to import", "⬇", seconds: 20);
            Voice.Say("The file had nothing to import.", "الملف ما كانش فيه حاجة أستوردها.");
            return;
        }
        var topics = _settings.Topics.ToList();
        var (added, replaced, changed) = TopicImport.Merge(topics, imported);
        _settings.Topics = topics;
        _settings.Save();
        await Voice.SayAndWaitAsync("Import done.", "الاستيراد تم.", TimeSpan.FromSeconds(4));
        // The topics added or updated, and their words: ready to ask about.
        var contents = changed.Select(TopicAnswers.TopicContents).ToList();
        var english = $"{Count(added, "new topic")}, {Count(replaced, "updated")}. " + string.Join(" ", contents.Select(c => c.English));
        var arabic = $"{added} مواضيع جديدة، و{replaced} اتحدثت. " + string.Join(" ", contents.Select(c => c.Arabic));
        Answer($"Pulled {Said(tool)}", english, arabic, 30);
    }

    private static string Count(int n, string what) => what == "updated" ? $"{n} updated" : n == 1 ? $"1 {what}" : $"{n} {what}s";

    private void ReadPulled(PullTool tool, string file)
    {
        if (PullTools.ReadReading(file) is not { } reading)
        {
            ShowLabel($"Pull {Said(tool)}", "Nothing to read", "⬇", seconds: 20);
            Voice.Say("The tool didn't give me anything to read.", "الأداة ما ادتنيش حاجة أقراها.");
            return;
        }
        var title = reading.Title.Length > 0 ? reading.Title : Said(tool);
        var english = $"Here it is. {title}. {reading.Items.Count} items. " +
                      string.Join(" ", reading.Items.Select((item, i) => $"{i + 1}: {item.Text}"));
        var arabic = $"اتفضل. {reading.OtherTitle ?? title}. {reading.Items.Count} عناصر. " +
                     string.Join(" ", reading.Items.Select((item, i) => $"{i + 1}: {item.OtherText ?? item.Text}"));
        // "Again" reads it once more; "stop" stops it.
        Answer(title, english, arabic, 30);
    }

    internal void OpenPullTools()
    {
        if (FocusOpenDialog()) return;
        var dialog = new PullToolsWindow(PullTools.Load(), _settings.AllowPullCommands, _settings.PullToolSettings);
        if (ShowDialogWindow(dialog) != true) return;
        var turnedOn = dialog.AllowPullCommands && !_settings.AllowPullCommands;
        _settings.AllowPullCommands = dialog.AllowPullCommands;
        _settings.PullToolSettings = dialog.ToolSettings;
        _settings.Save();
        Voice.Say(turnedOn ? "Pull commands are on. Each pull asks you first, because it goes online." : "Pull tools saved.",
            turnedOn ? "أوامر السحب اشتغلت. كل سحبة هسألك الأول لأنها بتدخل على الإنترنت." : "اتحفظت أدوات السحب.");
    }
}

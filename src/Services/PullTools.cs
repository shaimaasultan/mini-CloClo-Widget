using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MiniCloClo;

// "Pull" tools (#35): each is a folder in pull\ beside the app with its
// settings file (tool.json) and its script (run.ps1). Pulling runs the
// script with two arguments only - a settings file and the result file to
// write - then the result is imported into Questions and answers or read
// aloud. Mini CloClo itself opens no network connection: the tool is a
// separate process that may go online, and only runs when pull commands
// are turned on and the person has said yes. Nothing heard is ever passed
// to a tool; the spoken name only picks a folder.
internal sealed class PullTool
{
    public const string ImportResult = "import", ReadResult = "read";

    public string Folder { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? OtherName { get; init; }
    public string Description { get; init; } = "";
    public string? OtherDescription { get; init; }
    public string Result { get; init; } = ImportResult;
    public int TimeoutSeconds { get; init; } = 120;
    // The tool's own options and their defaults, from tool.json.
    public Dictionary<string, string> Settings { get; init; } = new();
    // What each setting is, shown in the Pull tools window.
    public Dictionary<string, string> SettingLabels { get; init; } = new();

    public string Script => Path.Combine(Folder, "run.ps1");
}

// What a "read" tool writes: a title and up to ten short items.
internal sealed record PullReading(string Title, string? OtherTitle, List<(string Text, string? OtherText)> Items);

internal static class PullTools
{
    public const int MaxItems = 10, MaxItemLength = 300;

    // Debug tests point this at a folder of test tools.
    internal static string? FolderOverride;
    internal static string ToolsFolder => FolderOverride ?? Path.Combine(AppContext.BaseDirectory, "pull");
    private static string WorkFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniCloClo", "pull");

    // tool.json as written by hand.
    private sealed class ToolFile
    {
        public string? Name { get; set; }
        public string? OtherName { get; set; }
        public string? Description { get; set; }
        public string? OtherDescription { get; set; }
        public string? Result { get; set; }
        public int? TimeoutSeconds { get; set; }
        public Dictionary<string, JsonElement>? Settings { get; set; }
        public Dictionary<string, string>? SettingLabels { get; set; }
    }

    private static readonly Regex FolderName = new("^[a-z0-9-]{1,30}$");
    private static readonly Regex SettingName = new("^[A-Za-z][A-Za-z0-9]{0,29}$");

    // The tools found, by name; a folder whose tool.json is missing or wrong
    // is skipped.
    internal static List<PullTool> Load()
    {
        var tools = new List<PullTool>();
        if (!Directory.Exists(ToolsFolder)) return tools;
        foreach (var folder in Directory.GetDirectories(ToolsFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var id = Path.GetFileName(folder).ToLowerInvariant();
                var settingsPath = Path.Combine(folder, "tool.json");
                if (!FolderName.IsMatch(id) || !File.Exists(settingsPath) || !File.Exists(Path.Combine(folder, "run.ps1"))) continue;
                if (new FileInfo(settingsPath).Length > 64_000) continue;
                var file = JsonSerializer.Deserialize<ToolFile>(File.ReadAllText(settingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (file == null || !Short(file.Name, 30) || !Short(file.Description, 200)) continue;
                tools.Add(new PullTool
                {
                    Folder = folder,
                    Id = id,
                    Name = file.Name!.Trim(),
                    OtherName = Short(file.OtherName, 30) ? file.OtherName!.Trim() : null,
                    Description = file.Description!.Trim(),
                    OtherDescription = Short(file.OtherDescription, 200) ? file.OtherDescription!.Trim() : null,
                    Result = file.Result == PullTool.ReadResult ? PullTool.ReadResult : PullTool.ImportResult,
                    TimeoutSeconds = Math.Clamp(file.TimeoutSeconds ?? 120, 10, 600),
                    Settings = (file.Settings ?? new())
                        .Where(s => SettingName.IsMatch(s.Key) && s.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                        .Take(20)
                        .ToDictionary(s => s.Key, s => s.Value.ValueKind == JsonValueKind.String ? s.Value.GetString() ?? "" : s.Value.ToString()),
                    SettingLabels = (file.SettingLabels ?? new())
                        .Where(l => SettingName.IsMatch(l.Key) && Short(l.Value, 120))
                        .ToDictionary(l => l.Key, l => l.Value.Trim()),
                });
            }
            catch
            {
                // An unreadable tool is left out.
            }
        }
        return tools;
    }

    private static bool Short(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max && !value.Any(char.IsControl);

    // A setting's value as the person may set it: short, one line.
    internal static bool IsSettingValue(string? value) => value != null && value.Length <= 100 && !value.Any(char.IsControl);

    // The tool's settings: its defaults, with the person's own values on top.
    internal static Dictionary<string, string> EffectiveSettings(PullTool tool, IReadOnlyDictionary<string, Dictionary<string, string>> own)
    {
        var settings = new Dictionary<string, string>(tool.Settings);
        if (own.TryGetValue(tool.Id, out var values))
            foreach (var (key, value) in values)
                if (settings.ContainsKey(key) && IsSettingValue(value)) settings[key] = value.Trim();
        return settings;
    }

    internal enum Outcome { Done, Failed, TimedOut, NoResult }

    // Mini CloClo's own place: the prayer-times city, its country and, where
    // holidays differ by region, its province or state. Null parts aren't known.
    internal sealed record Place(string? City, string? Country, string? Region);

    // Runs the tool's script, hidden, with its settings file and the result
    // file to write. The settings file also says the voice commands'
    // language ("en" or "ar") and Mini CloClo's place, so a tool can fetch
    // in that language and for that country. Returns the result file's
    // path when it finished well.
    internal static async Task<(Outcome Outcome, string? ResultFile)> RunAsync(PullTool tool, Dictionary<string, string> settings, bool arabic = false,
        Place? place = null, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(WorkFolder);
        var settingsFile = Path.Combine(WorkFolder, tool.Id + ".settings.json");
        var resultFile = Path.Combine(WorkFolder, tool.Id + ".result.json");
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(new
        {
            language = arabic ? "ar" : "en",
            place = new { city = place?.City, country = place?.Country, region = place?.Region },
            settings,
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(resultFile)) File.Delete(resultFile);

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = tool.Folder,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", tool.Script, settingsFile, resultFile })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process == null) return (Outcome.Failed, null);
        // Read the output so a chatty script can't block on a full pipe.
        var output = process.StandardOutput.ReadToEndAsync(cancel);
        var errors = process.StandardError.ReadToEndAsync(cancel);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(tool.TimeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return (Outcome.TimedOut, null);
        }
        try { await Task.WhenAll(output, errors); } catch { /* output isn't needed */ }
        if (process.ExitCode != 0) return (Outcome.Failed, null);
        return File.Exists(resultFile) ? (Outcome.Done, resultFile) : (Outcome.NoResult, null);
    }

    // A "read" result: { "title", "otherTitle", "items": [ { "text", "otherText" } ] }.
    internal static PullReading? ReadReading(string path)
    {
        if (new FileInfo(path).Length > 200_000) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        static string? Text(JsonElement element, string name, int max)
        {
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    var text = new string(property.Value.GetString()!.Where(c => !char.IsControl(c) || c == ' ').ToArray()).Trim();
                    return text.Length == 0 ? null : text.Length > max ? text[..max] : text;
                }
            return null;
        }
        var items = new List<(string, string?)>();
        foreach (var property in root.EnumerateObject())
            if (string.Equals(property.Name, "items", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Array)
                foreach (var item in property.Value.EnumerateArray().Take(MaxItems))
                    if (item.ValueKind == JsonValueKind.Object && Text(item, "text", MaxItemLength) is { } text)
                        items.Add((text, Text(item, "otherText", MaxItemLength)));
        return items.Count == 0 ? null : new PullReading(Text(root, "title", 80) ?? "", Text(root, "otherTitle", 80), items);
    }
}

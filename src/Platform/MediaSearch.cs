using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using Windows.Media.Control;

namespace MiniCloClo;

// One search result read from the app: a video's title, channel and length,
// or a song and its artists.
internal sealed record SearchResult(string Title, string? By, string? Length, string? Address)
{
    // What plays it: the video link or the song's play button.
    internal AutomationElement? Target { get; init; }
}

// Search and play (#38), the way Narrator does it - Windows UI Automation:
// Mini CloClo hands the search address to Edge or Spotify, then finds the
// results in the app's accessibility tree by what they are (YouTube's video
// links, Spotify's song rows), reads the top ones and invokes the chosen one.
// Nothing is watched or left running afterwards: no events are subscribed,
// no Windows setting is changed, and Mini CloClo itself opens no network
// connection - Edge and Spotify go online, as they do when searching by hand.
internal static class MediaSearch
{
    private const string SpotifyPackageFamily = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";
    private const string ChromiumWindowClass = "Chrome_WidgetWin_1";
    private static readonly TimeSpan ReadTime = TimeSpan.FromSeconds(25);
    // The live test's notes on what the reader saw, and when.
    internal static Action<string>? Trace;

    private static string? EdgePath() =>
        new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), @"Microsoft\Edge\Application\msedge.exe"))
            .FirstOrDefault(File.Exists);

    private static string ClassicSpotifyPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");

    private static bool SpotifyFromStore()
    {
        try
        {
            return new Windows.Management.Deployment.PackageManager().FindPackagesForUser(string.Empty, SpotifyPackageFamily).Any();
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsInstalled(SearchApp app) =>
        app == SearchApp.YouTube ? EdgePath() != null : SpotifyFromStore() || File.Exists(ClassicSpotifyPath);

    internal static string YouTubeAddress(string words) => "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(words);
    internal static string SpotifyAddress(string words) => "spotify:search:" + Uri.EscapeDataString(words);

    // Opens the app on its results; null when it opened, else why not.
    internal static async Task<string?> OpenAsync(SearchApp app, string words)
    {
        try
        {
            if (app == SearchApp.YouTube) return StartEdge(YouTubeAddress(words), newWindow: true);
            if (SpotifyFromStore())
            {
                // Only the Spotify package may answer: a protocol handler
                // another program registered can't take the address.
                var options = new Windows.System.LauncherOptions { TargetApplicationPackageFamilyName = SpotifyPackageFamily };
                return await Windows.System.Launcher.LaunchUriAsync(new Uri(SpotifyAddress(words)), options) ? null : "Spotify didn't open.";
            }
            if (!File.Exists(ClassicSpotifyPath)) return "Spotify isn't installed.";
            if (!NativeLibraryCheck.IsSignedBySpotify(ClassicSpotifyPath)) return "Spotify's signature could not be verified.";
            var start = new ProcessStartInfo(ClassicSpotifyPath) { UseShellExecute = false };
            start.ArgumentList.Add("--uri=" + SpotifyAddress(words));
            using var process = Process.Start(start);
            return process == null ? "Spotify didn't open." : null;
        }
        catch (Exception ex)
        {
            return $"{SearchVoiceCommand.Name(app)} didn't open ({ex.GetType().Name}).";
        }
    }

    private static string? StartEdge(string address, bool newWindow)
    {
        if (EdgePath() is not { } edge) return "Microsoft Edge isn't installed.";
        if (!NativeLibraryCheck.IsTrustedMicrosoftExecutable(edge)) return "Edge's signature could not be verified.";
        var start = new ProcessStartInfo(edge) { UseShellExecute = false };
        if (newWindow) start.ArgumentList.Add("--new-window");
        start.ArgumentList.Add(address);
        using var process = Process.Start(start);
        return process == null ? "Edge didn't open." : null;
    }

    // The top results, read from the app once they show (null: none could
    // be read in time). Runs off the UI thread.
    internal static Task<IReadOnlyList<SearchResult>?> ReadAsync(SearchApp app, string words, int count, CancellationToken cancel) =>
        Task.Run(() =>
        {
            try
            {
                return app == SearchApp.YouTube ? ReadYouTube(words, count, cancel) : ReadSpotify(words, count, cancel);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or OperationCanceledException)
            {
                return null;
            }
        }, cancel);

    // Plays one result: invokes its link or button where it is on screen.
    // The app may have redrawn its list while Keeper read it, leaving the
    // element found then gone: it is found again by its name. A YouTube
    // video whose link has gone is opened by its address instead.
    internal static Task<bool> PlayAsync(SearchApp app, SearchResult result) =>
        Task.Run(() =>
        {
            if (TryInvoke(result.Target, "the element read")) return true;
            var again = app == SearchApp.YouTube ? FindYouTubeLink(result) : FindSpotifyButton(result);
            if (TryInvoke(again, "the element found again")) return true;
            Trace?.Invoke("no element to press");
            return app == SearchApp.YouTube && result.Address != null && StartEdge(result.Address, newWindow: false) == null;
        });

    private static bool TryInvoke(AutomationElement? element, string what)
    {
        if (element == null) return false;
        try
        {
            if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)) return false;
            ((InvokePattern)pattern).Invoke();
            Trace?.Invoke($"pressed {what}");
            return true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Trace?.Invoke($"{what} failed: {ex.GetType().Name}");
            return false;
        }
    }

    private static AutomationElement? FindSpotifyButton(SearchResult result)
    {
        try
        {
            return SpotifyWindow()?.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, PlayLead + result.Title + ByWord + result.By)));
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static AutomationElement? FindYouTubeLink(SearchResult result)
    {
        try
        {
            var window = EdgeResultsWindow("", anyResults: true);
            if (window == null || result.Address == null) return null;
            foreach (AutomationElement link in window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink)))
                if (link.TryGetCurrentPattern(ValuePattern.Pattern, out var value) &&
                    ((ValuePattern)value).Current.Value.StartsWith(result.Address, StringComparison.Ordinal) && Duration.IsMatch(link.Current.Name))
                    return link;
            return null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    // ---- YouTube in Edge ----

    private static IReadOnlyList<SearchResult>? ReadYouTube(string words, int count, CancellationToken cancel)
    {
        var started = DateTime.UtcNow;
        var until = started + ReadTime;
        IReadOnlyList<SearchResult> found = [];
        var steady = 0;
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(700);
            cancel.ThrowIfCancellationRequested();
            // The app redraws while it loads: an element can go between
            // finding and reading it. That look is skipped, not the search.
            try
            {
                if (EdgeResultsWindow(words, anyResults: DateTime.UtcNow - started > TimeSpan.FromSeconds(10)) is not { } window) continue;
                var links = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink));
                var named = new List<(string Name, string Address, AutomationElement Element)>();
                foreach (AutomationElement link in links)
                {
                    var address = link.TryGetCurrentPattern(ValuePattern.Pattern, out var value) ? ((ValuePattern)value).Current.Value : "";
                    named.Add((link.Current.Name, address, link));
                }
                var results = YouTubeResults(named.Select(l => (l.Name, l.Address)).ToList(), count);
                // The page fills in as it loads: read once the top ones are
                // there, or the same few twice in a row.
                steady = results.Count > 0 && results.Count == found.Count ? steady + 1 : 0;
                found = results.Select(r => r.Result with { Target = named[r.Index].Element }).ToList();
                if (found.Count >= count || steady >= 2) return found;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                Trace?.Invoke($"look skipped: {ex.GetType().Name}");
            }
        }
        return found.Count > 0 ? found : null;
    }

    // The Edge window showing YouTube's results for these words (its title
    // is the address, then "<words> - YouTube"); after a while, any.
    private static AutomationElement? EdgeResultsWindow(string words, bool anyResults)
    {
        var key = Letters(words);
        AutomationElement? other = null;
        var edge = Process.GetProcessesByName("msedge").Select(p => p.Id).ToHashSet();
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children,
                     new PropertyCondition(AutomationElement.ClassNameProperty, ChromiumWindowClass)))
        {
            var name = window.Current.Name;
            if (edge.Contains(window.Current.ProcessId) &&
                (name.Contains("youtube.com/results", StringComparison.OrdinalIgnoreCase) || name.Contains("- YouTube", StringComparison.Ordinal)))
            {
                if (Letters(name).Contains(key, StringComparison.Ordinal)) return window;
                other ??= window;
            }
        }
        return anyResults ? other : null;
    }

    private static readonly Regex VideoId = new(@"^https://www\.youtube\.com/watch\?(?:[^#]*&)?v=([\w-]{11})", RegexOptions.CultureInvariant);
    private static readonly Regex Duration = new(@"\s(?<len>(?:\d+ hours?, )?(?:\d+ minutes?, )?\d+ (?:hours?|minutes?|seconds?))$", RegexOptions.CultureInvariant);
    private const string ChannelLead = "Go to channel ";

    // From the page's links in order: the videos - a title link ending in
    // its length ("Baba (Official Video) 3 minutes, 19 seconds") that goes
    // to a video, not a playlist ("Mix"), a Short or an ad (ads go through
    // googleadservices) - each with the "Go to channel ..." link after it.
    internal static List<(SearchResult Result, int Index)> YouTubeResults(IReadOnlyList<(string Name, string Address)> links, int count)
    {
        var results = new List<(SearchResult Result, int Index)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < links.Count && results.Count < count; i++)
        {
            var (name, address) = links[i];
            if (VideoId.Match(address) is not { Success: true } id || address.Contains("list=", StringComparison.Ordinal)) continue;
            if (Duration.Match(name) is not { Success: true } length) continue;
            if (!seen.Add(id.Groups[1].Value)) continue;
            string? channel = null;
            for (var j = i + 1; j < links.Count && j <= i + 6; j++)
            {
                if (Duration.IsMatch(links[j].Name) && VideoId.IsMatch(links[j].Address)) break;
                if (links[j].Name.StartsWith(ChannelLead, StringComparison.Ordinal))
                {
                    channel = links[j].Name[ChannelLead.Length..].Trim();
                    break;
                }
            }
            var title = name[..length.Index].Trim();
            // The same song twice (two uploads by one channel) is read once.
            if (title.Length == 0 || !seen.Add($"{title} | {channel}")) continue;
            results.Add((new SearchResult(title, channel, SpokenLength(length.Groups["len"].Value),
                $"https://www.youtube.com/watch?v={id.Groups[1].Value}"), i));
        }
        return results;
    }

    // "3 minutes, 19 seconds" is said "3 minutes"; "1 hour, 2 minutes" as is.
    private static string SpokenLength(string length)
    {
        var parts = length.Split(", ");
        return parts.Length > 1 && parts[^1].Contains("second", StringComparison.Ordinal) ? string.Join(", ", parts[..^1]) : length;
    }

    // ---- Spotify ----

    private static readonly string[] SongsFilterNames = ["Songs", "الأغاني", "أغانٍ", "الاغاني"];
    private const string PlayLead = "Play ";
    private const string ByWord = " by ";

    private static IReadOnlyList<SearchResult>? ReadSpotify(string words, int count, CancellationToken cancel)
    {
        var until = DateTime.UtcNow + ReadTime;
        var reopened = false;
        var songsChosen = false;
        var songsChosenAt = DateTime.MinValue;
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(700);
            cancel.ThrowIfCancellationRequested();
            // The app redraws while it loads: an element can go between
            // finding and reading it. That look is skipped, not the search.
            try
            {
                if (SpotifyWindow() is not { } window)
                {
                    Trace?.Invoke("no Spotify window");
                    continue;
                }
                // Ready once the search box holds these words and the previous
                // search's song list has gone - choosing Songs before that
                // takes Spotify back to the previous search.
                var boxShows = SearchBoxShows(window, words);
                var oldList = OtherSongsList(window, words);
                Trace?.Invoke($"search box shows the words: {boxShows}, old list: {oldList}, songs chosen: {songsChosen}");
                if (songsChosen && (!boxShows || DateTime.UtcNow - songsChosenAt > TimeSpan.FromSeconds(6)))
                {
                    // It went back to the previous search after all: once more.
                    songsChosen = false;
                    if (!boxShows)
                    {
                        Trace?.Invoke("opened the search again");
                        _ = OpenAsync(SearchApp.Spotify, words);
                        continue;
                    }
                }
                if (!songsChosen && boxShows && !oldList)
                {
                    // The Songs filter: just songs, one per row with its artists.
                    var filter = window.FindFirst(TreeScope.Descendants, new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
                        new OrCondition(SongsFilterNames.Select(n => (Condition)new PropertyCondition(AutomationElement.NameProperty, n)).ToArray())));
                    if (filter != null && filter.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
                    {
                        ((InvokePattern)invoke).Invoke();
                        Trace?.Invoke("chose Songs");
                        songsChosen = true;
                        songsChosenAt = DateTime.UtcNow;
                        continue;
                    }
                }
                if (!songsChosen)
                {
                    // Spotify was starting and showed its home page: the search
                    // address once more, now that it is running.
                    if (!reopened && DateTime.UtcNow - started > TimeSpan.FromSeconds(10))
                    {
                        reopened = true;
                        Trace?.Invoke("opened the search again");
                        _ = OpenAsync(SearchApp.Spotify, words);
                    }
                }
                var results = SpotifySongs(window, words, count, allowAnyList: DateTime.UtcNow - started > TimeSpan.FromSeconds(15));
                Trace?.Invoke($"songs found: {results.Count}");
                if (results.Count >= count || results.Count > 0 && songsChosen) return results;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                Trace?.Invoke($"look skipped: {ex.GetType().Name}");
            }
        }
        return null;
    }

    // Spotify's search box holds these words ("Amr+Diab"): its page is
    // this search, not the one before.
    private static bool SearchBoxShows(AutomationElement window, string words)
    {
        var key = Letters(words);
        foreach (AutomationElement box in window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)))
            if (box.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && Letters(((ValuePattern)value).Current.Value) == key)
                return true;
        return false;
    }

    // A song list for other words ("All songs for “Fairuz”"), from the search before.
    private static bool OtherSongsList(AutomationElement window, string words)
    {
        var key = Letters(words);
        foreach (AutomationElement grid in window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid)))
        {
            var name = Letters(grid.Current.Name);
            if (name.StartsWith("allsongsfor", StringComparison.Ordinal) && !name.Contains(key, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static AutomationElement? SpotifyWindow()
    {
        var spotify = Process.GetProcessesByName("Spotify").Select(p => p.Id).ToHashSet();
        if (spotify.Count == 0) return null;
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children,
                     new PropertyCondition(AutomationElement.ClassNameProperty, ChromiumWindowClass)))
            if (spotify.Contains(window.Current.ProcessId) && window.Current.Name.Length > 0) return window;
        return null;
    }

    // The songs' play buttons, "Play Baba by Amr Diab", in the songs list for
    // these words ("All songs for “Amr+Diab”") - not the person's library.
    private static List<SearchResult> SpotifySongs(AutomationElement window, string words, int count, bool allowAnyList)
    {
        var results = new List<SearchResult>();
        var key = Letters(words);
        foreach (AutomationElement grid in window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid)))
        {
            var gridName = grid.Current.Name;
            var gridKey = Letters(gridName);
            var forTheseWords = gridKey.Contains(key, StringComparison.Ordinal);
            // Never the person's library, nor the song list of the search before.
            if (gridName == "Your Library" || gridKey.StartsWith("allsongsfor", StringComparison.Ordinal) && !forTheseWords) continue;
            if (!forTheseWords && !allowAnyList) continue;
            foreach (AutomationElement button in grid.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
            {
                if (SpotifySong(button.Current.Name) is not { } song) continue;
                results.Add(song with { Target = button });
                if (results.Count >= count) return results;
            }
            if (results.Count > 0) return results;
        }
        return results;
    }

    // "Play Lola El Banat by Amr Diab, Orange" -> the song and its artists.
    internal static SearchResult? SpotifySong(string buttonName)
    {
        if (!buttonName.StartsWith(PlayLead, StringComparison.Ordinal)) return null;
        var rest = buttonName[PlayLead.Length..];
        var by = rest.LastIndexOf(ByWord, StringComparison.Ordinal);
        if (by <= 0 || by + ByWord.Length >= rest.Length) return null;
        return new SearchResult(rest[..by].Trim(), rest[(by + ByWord.Length)..].Trim(), null, null);
    }

    private static string Letters(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // ---- Playing ----

    // Pause, resume, next or previous for what plays now (Spotify, or a
    // video in Edge) through Windows' media controls - the same as the
    // keyboard's media keys - on this PC only.
    internal static async Task<bool> ControlAsync(SearchVoiceCommand.Control control)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (manager.GetCurrentSession() is { } session)
            {
                var done = control switch
                {
                    SearchVoiceCommand.Control.Pause => await session.TryPauseAsync(),
                    SearchVoiceCommand.Control.Resume => await session.TryPlayAsync(),
                    SearchVoiceCommand.Control.Next => await session.TrySkipNextAsync(),
                    _ => await session.TrySkipPreviousAsync(),
                };
                if (done) return true;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
        }
        // No media session answered: the media keys. Play/pause is a
        // toggle, so it isn't sent for pause or resume blindly.
        if (control is SearchVoiceCommand.Control.Pause or SearchVoiceCommand.Control.Resume) return false;
        PressMediaKey(control == SearchVoiceCommand.Control.Next ? (byte)0xB0 : (byte)0xB1);
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    private static void PressMediaKey(byte key)
    {
        const uint KeyUp = 2;
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyUp, UIntPtr.Zero);
    }
}

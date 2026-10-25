using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MiniCloClo;

// Search and play by voice (#38): "search" -> "Which app? YouTube or
// Spotify?" -> "What should I search for?" -> the words read back, "yes" ->
// the app opens on its results and Keeper reads the top ones -> a number ->
// "Play number 2, Baba?" -> "yes" plays it. Search is off until turned on in
// the menu (Edge and Spotify go online). The words heard are read back - an
// exception to never speaking a transcript, so a mishearing is caught - and
// become the search only after "yes".
public partial class MainWindow
{
    private enum SearchStep { App, Words, ConfirmWords, Searching, Choose, ConfirmPlay, Played }

    private sealed class SearchState
    {
        public SearchStep Step;
        public SearchApp? App;
        public string? Words;
        public IReadOnlyList<SearchResult> Results = [];
        public int Chosen = -1;
        public DateTime Until;
        public CancellationTokenSource? Cancel;
    }

    private static readonly TimeSpan SearchAnswerTime = TimeSpan.FromSeconds(60);
    // The results can be picked by number, "next" or "again" this long - then
    // they, and the app's elements they point at, are let go.
    private static readonly TimeSpan SearchResultsTime = TimeSpan.FromMinutes(5);
    private SearchState? _search;

    private SearchState? ActiveSearch
    {
        get
        {
            if (_search is { Step: not SearchStep.Searching } search && DateTime.UtcNow > search.Until) _search = null;
            return _search;
        }
    }

    // While Keeper waits for the words to search for: listened to with no
    // command hints, in whichever language they're said.
    private bool SearchWantsWords => ActiveSearch is { Step: SearchStep.Words };

    // "Search", "search YouTube", "ابحث في سبوتيفاي": true when handled.
    private bool TrySearchCommand(string heard)
    {
        if (SearchVoiceCommand.TryParse(heard) is not { } request) return false;
        Keeper.SetVoiceStatus("complete");
        if (!_settings.AllowSearch)
        {
            ShowLabel("Search is off", "It opens Edge or Spotify online — turn it on in the menu, Search YouTube and Spotify", "🔎", seconds: 20);
            Voice.Say("Search is off, because it opens YouTube or Spotify online. To use it, open the menu, choose Search YouTube and Spotify, and turn it on.",
                "البحث مقفول لأنه بيفتح يوتيوب أو سبوتيفاي على الإنترنت. عشان تشغله، افتح القائمة واختار البحث في يوتيوب وسبوتيفاي، وشغله.");
            return true;
        }
        if (_search is { Step: SearchStep.Searching })
        {
            Voice.Say("I'm still searching. Please wait.", "لسه بدور. استنى شوية.");
            return true;
        }
        _pendingCall = null;
        _pendingPull = null;
        _pulledReading = null;
        _search = new SearchState { App = request.App, Words = request.Words is { } words ? SearchVoiceCommand.Words(words) : null };
        AskNextSearchQuestion(caughtLast: true);
        return true;
    }

    private void AskNextSearchQuestion(bool caughtLast)
    {
        if (_search is not { } search) return;
        var notCaught = caughtLast ? "" : "I didn't catch that. ";
        var notCaughtArabic = caughtLast ? "" : "ما فهمتش. ";
        search.Until = DateTime.UtcNow + SearchAnswerTime;
        if (search.App is not { } app)
        {
            search.Step = SearchStep.App;
            ShowLabel("Search: which app?", "Say YouTube or Spotify — or cancel", "🔎", seconds: 60);
            Voice.Say(notCaught + "Which app? YouTube or Spotify?", notCaughtArabic + "أنهي تطبيق؟ يوتيوب ولا سبوتيفاي؟");
            return;
        }
        var name = SearchVoiceCommand.Name(app);
        var arabicName = SearchVoiceCommand.ArabicName(app);
        if (!MediaSearch.IsInstalled(app))
        {
            _search = null;
            var needs = app == SearchApp.YouTube ? "Microsoft Edge" : "the Spotify app";
            ShowLabel($"{name} can't be searched", $"It needs {needs}", "🔎", seconds: 20);
            Voice.Say($"I can't search {name}: it needs {needs}, and it isn't installed.",
                app == SearchApp.YouTube ? "مقدرش أدور في يوتيوب: محتاج مايكروسوفت إيدج، ومش متسطب." : "مقدرش أدور في سبوتيفاي: تطبيق سبوتيفاي مش متسطب.");
            return;
        }
        if (search.Words is not { } words)
        {
            search.Step = SearchStep.Words;
            ShowLabel($"Search {name}", "Say what to search for — or cancel", "🔎", seconds: 60);
            Voice.Say(notCaught + $"What should I search for on {name}?", notCaughtArabic + $"أدور على إيه في {arabicName}؟");
            return;
        }
        search.Step = SearchStep.ConfirmWords;
        ShowLabel($"Search {name} for:", $"{words} — say yes, or no to say it again", "🔎", seconds: 60);
        Voice.Say(notCaught + $"Search {name} for {words}? Say yes, or no to say it again.",
            notCaughtArabic + $"أدور في {arabicName} على {words}؟ قول نعم، أو لا عشان تقولها تاني.");
    }

    // An answer while a search waits for one; true when taken.
    private async Task<bool> TryHandleSearchAnswerAsync(string heard)
    {
        if (ActiveSearch is not { } search) return false;
        // "No" to what Keeper read back asks again - before "no" is taken as
        // cancel ("cancel" itself still cancels).
        var no = search.Step is SearchStep.ConfirmWords or SearchStep.ConfirmPlay && QuranVoiceAnswers.IsBack(heard);
        if (!no && ApprovedVoiceAppLauncher.IsCancelCommand(heard))
        {
            // After it plays, "stop" is for whatever else is playing.
            if (search.Step == SearchStep.Played) return false;
            CancelSearch();
            return true;
        }
        switch (search.Step)
        {
            case SearchStep.App:
                Keeper.SetVoiceStatus("complete");
                search.App = SearchVoiceCommand.App(heard) ?? search.App;
                AskNextSearchQuestion(caughtLast: search.App != null);
                return true;
            case SearchStep.Words:
                Keeper.SetVoiceStatus("complete");
                search.Words = SearchVoiceCommand.Words(heard);
                AskNextSearchQuestion(caughtLast: search.Words != null);
                return true;
            case SearchStep.ConfirmWords:
                Keeper.SetVoiceStatus("complete");
                if (CallVoiceCommand.IsYes(heard)) await RunSearchAsync(search);
                else
                {
                    if (no) search.Words = null;
                    AskNextSearchQuestion(caughtLast: no);
                }
                return true;
            case SearchStep.Searching:
                Keeper.SetVoiceStatus("complete");
                Voice.Say("I'm still searching. Please wait.", "لسه بدور. استنى شوية.");
                return true;
            case SearchStep.ConfirmPlay when CallVoiceCommand.IsYes(heard):
                Keeper.SetVoiceStatus("complete");
                await PlaySearchResultAsync(search);
                return true;
            case SearchStep.ConfirmPlay when no:
                Keeper.SetVoiceStatus("complete");
                search.Step = SearchStep.Choose;
                search.Until = DateTime.UtcNow + SearchResultsTime;
                Voice.Say("Which one? Say the number, or again to hear them.", "أنهي واحد؟ قول الرقم، أو تاني عشان تسمعهم.");
                return true;
        }
        // Choose, ConfirmPlay or Played: a number, "next", "again".
        if (SearchVoiceCommand.IsAgain(heard))
        {
            Keeper.SetVoiceStatus("complete");
            ReadSearchResults(search);
            return true;
        }
        int? number = SearchVoiceCommand.IsNextOne(heard) ? search.Chosen + 2 : SearchVoiceCommand.Choice(heard);
        if (number is not { } n)
        {
            if (search.Step == SearchStep.Played) return false; // some other command
            Keeper.SetVoiceStatus("failed");
            search.Until = DateTime.UtcNow + SearchResultsTime;
            Voice.Say($"I didn't catch that. Say a number from 1 to {search.Results.Count}, or again to hear them, or cancel.",
                $"ما فهمتش. قول رقم من 1 لـ {search.Results.Count}، أو تاني عشان تسمعهم، أو لا عشان ألغي.");
            return true;
        }
        Keeper.SetVoiceStatus("complete");
        if (n < 1 || n > search.Results.Count)
        {
            search.Until = DateTime.UtcNow + SearchResultsTime;
            Voice.Say(n > search.Results.Count && SearchVoiceCommand.IsNextOne(heard)
                    ? "That was the last one. Say a number, or search again."
                    : $"Say a number from 1 to {search.Results.Count}.",
                n > search.Results.Count && SearchVoiceCommand.IsNextOne(heard)
                    ? "ده كان آخر واحد. قول رقم، أو دور تاني."
                    : $"قول رقم من 1 لـ {search.Results.Count}.");
            return true;
        }
        search.Chosen = n - 1;
        // After one has played, "next" plays the next one straight away.
        if (search.Step == SearchStep.Played && SearchVoiceCommand.IsNextOne(heard))
        {
            await PlaySearchResultAsync(search);
            return true;
        }
        search.Step = SearchStep.ConfirmPlay;
        search.Until = DateTime.UtcNow + SearchAnswerTime;
        var chosen = search.Results[search.Chosen];
        ShowLabel($"Play number {n}?", $"{chosen.Title} — say yes, or another number", "🔎", seconds: 60);
        Voice.Say($"Play number {n}, {chosen.Title}? Say yes, or another number.", $"أشغل رقم {n}، {chosen.Title}؟ قول نعم، أو رقم تاني.");
        return true;
    }

    private bool CancelSearch()
    {
        if (_search is not { Step: not SearchStep.Played } search) return false;
        search.Cancel?.Cancel();
        _search = null;
        Keeper.SetVoiceStatus("complete");
        ShowLabel("Search cancelled", "", "🔎");
        Voice.Say("Search cancelled.", "اتلغى البحث.");
        return true;
    }

    private async Task RunSearchAsync(SearchState search)
    {
        var app = search.App!.Value;
        var words = search.Words!;
        var name = SearchVoiceCommand.Name(app);
        var arabicName = SearchVoiceCommand.ArabicName(app);
        search.Step = SearchStep.Searching;
        search.Cancel = new CancellationTokenSource();
        ShowLabel($"Searching {name}…", words, "🔎", seconds: 40);
        await Voice.SayAndWaitAsync($"Searching {name} for {words}.", $"بدور في {arabicName} على {words}.", TimeSpan.FromSeconds(8));
        if (_search != search) return; // cancelled
        if (await MediaSearch.OpenAsync(app, words) is { } error)
        {
            _search = null;
            ShowLabel($"{name} didn't open", error, "🔎", seconds: 20);
            Voice.Say($"{name} didn't open.", $"{arabicName} ما فتحش.");
            return;
        }
        var results = await MediaSearch.ReadAsync(app, words, _settings.SearchResultCount, search.Cancel.Token);
        if (_search != search || _closing) return;
        if (results is not { Count: > 0 })
        {
            _search = null;
            ShowLabel($"Couldn't read {name}'s results", $"{name} is open on them — choose by hand or with Narrator", "🔎", seconds: 30);
            Voice.Say($"I couldn't read the results. {name} is open on them, so someone can choose by hand, or with Narrator.",
                $"مقدرتش أقرا النتايج. {arabicName} مفتوح عليها، فممكن حد يختار بإيده، أو بالراوي.");
            return;
        }
        search.Results = results;
        search.Step = SearchStep.Choose;
        search.Until = DateTime.UtcNow + SearchResultsTime;
        ReadSearchResults(search);
    }

    // "Here are the top 5 on YouTube. 1: Baba, from Rotana, 3 minutes. ..."
    private void ReadSearchResults(SearchState search)
    {
        var app = search.App!.Value;
        var results = search.Results;
        search.Until = DateTime.UtcNow + SearchResultsTime;
        string Item(SearchResult r, bool arabic) =>
            r.Title + (r.By is { } by ? (app == SearchApp.YouTube ? (arabic ? $"، من {by}" : $", from {by}") : (arabic ? $"، {by}" : $", by {by}")) : "") +
            (r.Length is { } length ? (arabic ? $"، {ArabicLength(length)}" : $", {length}") : "");
        var english = $"Here are the top {results.Count} on {SearchVoiceCommand.Name(app)}. " +
                      string.Join(" ", results.Select((r, i) => $"{i + 1}: {Item(r, false)}.")) +
                      " Which one should I play? Say the number.";
        var arabicText = $"دي أول {results.Count} في {SearchVoiceCommand.ArabicName(app)}. " +
                         string.Join(" ", results.Select((r, i) => $"{i + 1}: {Item(r, true)}.")) +
                         " أشغل أنهي واحد؟ قول الرقم.";
        Answer($"{SearchVoiceCommand.Name(app)}: {search.Words}", english, arabicText, 60);
    }

    // "3 minutes" / "1 hour, 2 minutes" for the Arabic voice.
    private static string ArabicLength(string length) => length
        .Replace(" hours", " ساعات").Replace(" hour", " ساعة")
        .Replace(" minutes", " دقايق").Replace(" minute", " دقيقة")
        .Replace(" seconds", " ثانية").Replace(" second", " ثانية")
        .Replace(", ", " و");

    private async Task PlaySearchResultAsync(SearchState search)
    {
        var app = search.App!.Value;
        var result = search.Results[search.Chosen];
        ShowLabel($"Playing {result.Title}", result.By ?? "", "🔎", seconds: 20);
        // Said before it plays, so the music doesn't drown it.
        await Voice.SayAndWaitAsync($"Playing {result.Title}.", $"بشغل {result.Title}.", TimeSpan.FromSeconds(8));
        if (_search != search) return;
        search.Step = SearchStep.Played;
        search.Until = DateTime.UtcNow + SearchResultsTime;
        if (!await MediaSearch.PlayAsync(app, result))
        {
            var name = SearchVoiceCommand.Name(app);
            ShowLabel($"Couldn't play it", $"{name} is open on the results", "🔎", seconds: 30);
            Voice.Say($"I couldn't play it. {name} is open on the results, so someone can choose it by hand.",
                $"مقدرتش أشغله. {SearchVoiceCommand.ArabicName(app)} مفتوح على النتايج، فممكن حد يختاره بإيده.");
        }
    }

    // "Pause the song", "resume", "next song", "previous song".
    private async Task<bool> TrySearchControlAsync(string heard)
    {
        if (SearchVoiceCommand.TryControl(heard) is not { } control) return false;
        var done = await MediaSearch.ControlAsync(control);
        Keeper.SetVoiceStatus(done ? "complete" : "failed");
        if (done)
            ShowLabel(control switch
            {
                SearchVoiceCommand.Control.Pause => "Paused",
                SearchVoiceCommand.Control.Resume => "Playing",
                SearchVoiceCommand.Control.Next => "Next song",
                _ => "Previous song",
            }, "", "🎵");
        else
        {
            ShowLabel("Nothing to control", "No song or video is playing", "🎵");
            Voice.Say("Nothing is playing that I can control.", "مفيش حاجة شغالة أقدر أتحكم فيها.");
        }
        return true;
    }

    private void ToggleAllowSearch()
    {
        _settings.AllowSearch = !_settings.AllowSearch;
        _settings.Save();
        Voice.Say(_settings.AllowSearch
                ? "Search is on. Say search, then YouTube or Spotify. Each search asks you first, because it goes online."
                : "Search is off.",
            _settings.AllowSearch
                ? "البحث اشتغل. قول ابحث، وبعدين يوتيوب أو سبوتيفاي. كل بحث هسألك الأول لأنه بيدخل على الإنترنت."
                : "البحث اتقفل.");
    }

    private void SetSearchResultCount(int count)
    {
        _settings.SearchResultCount = count;
        _settings.Save();
        Voice.Say(count == 1 ? "I'll read the top result." : $"I'll read the top {count} results.",
            count == 1 ? "هقرا أول نتيجة." : $"هقرا أول {count} نتايج.");
    }
}

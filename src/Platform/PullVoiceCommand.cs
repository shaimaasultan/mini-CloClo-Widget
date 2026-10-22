using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniCloClo;

// "Pull dates" / "اسحب التواريخ" / "pull" / "what can I pull?" (#35). The
// name said is only matched against the tools found in the pull folder
// (exact, then one or two sounds away); nothing heard reaches a tool.
internal static class PullVoiceCommand
{
    internal abstract record Request;
    // A tool to run (null: "pull" alone, with more than one tool).
    internal sealed record PullRequest(PullTool? Tool, string? Unknown) : Request;
    internal sealed record ListRequest : Request;

    // MatchingKey spelling (ة→ه, أ→ا, ّ dropped), longest first. "Pull" is
    // often heard as "Paul" or "pool": with a tool's name after it, it counts.
    private static readonly string[] Leads =
    [
        "pull the ", "pull my ", "pull ", "paul the ", "paul ", "pool the ", "pool ", "pol ",
        "update the ", "update my ", "update ", "refresh the ", "refresh ", "اسحب ", "حدث ", "جيب ",
    ];
    // Leads that are only sound-alikes of "pull": an unknown name after them
    // isn't a pull at all ("Paul Simon").
    private static readonly string[] SoundAlikeLeads = ["paul the ", "paul ", "pool the ", "pool ", "pol "];
    private static readonly string[] Alone = ["pull", "pull it", "pull now", "اسحب", "حدث", "تحديث"];
    private static readonly string[] ListPhrases =
    [
        "what can i pull", "what can you pull", "pull tools", "list pull tools", "the pull tools",
        "اسحب ايه", "اقدر اسحب ايه", "ادوات السحب", "ايه اللي اقدر اسحبه",
    ];

    // Whether a phrase belongs to pulling, so it can't be one of the
    // person's own question words.
    internal static bool IsPullPhrase(string word)
    {
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(word);
        return Array.IndexOf(Alone, phrase) >= 0 || Array.IndexOf(ListPhrases, phrase) >= 0 ||
               Leads.Any(l => phrase.StartsWith(l, StringComparison.Ordinal));
    }

    internal static Request? TryParse(string transcript, IReadOnlyList<PullTool> tools)
    {
        if (transcript.Length > 160) return null;
        var phrase = ApprovedVoiceAppLauncher.CommandPhrase(transcript);
        if (Array.IndexOf(ListPhrases, phrase) >= 0) return new ListRequest();
        if (Array.IndexOf(Alone, phrase) >= 0) return new PullRequest(tools.Count == 1 ? tools[0] : null, null);
        foreach (var lead in Leads)
        {
            if (!phrase.StartsWith(lead, StringComparison.Ordinal)) continue;
            var name = phrase[lead.Length..].Trim();
            if (name.Length == 0) return new PullRequest(tools.Count == 1 ? tools[0] : null, null);
            var tool = Match(name, tools);
            if (tool == null && Array.IndexOf(SoundAlikeLeads, lead) >= 0) return null;
            return new PullRequest(tool, tool == null ? name : null);
        }
        return null;
    }

    private static PullTool? Match(string name, IReadOnlyList<PullTool> tools)
    {
        IEnumerable<string> Keys(PullTool tool)
        {
            foreach (var text in new[] { tool.Name, tool.OtherName, tool.Id })
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                var key = TranscriptCleanup.MatchingKey(text);
                yield return key;
                if (key.StartsWith("ال", StringComparison.Ordinal) && key.Length > 3) yield return key[2..];
            }
        }
        var bare = name.StartsWith("ال", StringComparison.Ordinal) && name.Length > 3 ? name[2..] : name;
        foreach (var tool in tools)
            if (Keys(tool).Contains(name) || Keys(tool).Contains(bare)) return tool;

        // One or two sounds away ("pull dades"), when only one tool is that close.
        PullTool? best = null;
        var bestDistance = int.MaxValue;
        var tied = false;
        var heard = ApprovedVoiceAppLauncher.SoundKey(name);
        if (heard.Length < 2) return null;
        foreach (var tool in tools)
            foreach (var key in Keys(tool))
            {
                var target = ApprovedVoiceAppLauncher.SoundKey(key);
                var allowed = target.Length >= 6 ? 2 : target.Length >= 3 ? 1 : 0;
                var distance = ApprovedVoiceAppLauncher.EditDistance(heard, target);
                if (distance > allowed || distance > bestDistance) continue;
                if (distance == bestDistance && !ReferenceEquals(best, tool)) tied = true;
                else if (distance < bestDistance) tied = false;
                bestDistance = distance;
                best = tool;
            }
        return tied ? null : best;
    }
}

using System;
using System.Linq;

namespace MiniCloClo;

// Questions and answers (#30): the topics and actions the person entered
// (TopicsWindow), answered by voice - "Ramadan", "when is Civic", "رمضان
// امتى" - with the text as entered or the next date. Built-in commands are
// tried first; these only ever answer with what was entered or a date.
public partial class MainWindow
{
    internal void OpenTopics()
    {
        if (FocusOpenDialog()) return;
        var dialog = new TopicsWindow(_settings.Topics);
        if (ShowDialogWindow(dialog) != true) return;
        _settings.Topics = dialog.Topics.ToList();
        _settings.Save();
        Voice.Say("Questions and answers saved.", "اتحفظت الأسئلة والأجوبة.");
    }

    // The last answer, for "say it again" - kept 5 minutes.
    private static readonly TimeSpan SayAgainTime = TimeSpan.FromMinutes(5);
    private (string Title, string English, string Arabic, DateTime At)? _lastAnswer;

    private void Answer(string title, string english, string arabic, int seconds)
    {
        _lastAnswer = (title, english, arabic, DateTime.UtcNow);
        ShowLabel(title, english, "💬", seconds: seconds);
        Voice.Say(english, arabic);
    }

    // "Again", "date again", "قولها تاني".
    private void SayLastAnswerAgain()
    {
        if (_lastAnswer is not { } last || DateTime.UtcNow - last.At > SayAgainTime)
        {
            ShowLabel("Nothing to repeat", "Ask a question first, like \"Ramadan date\"", "💬");
            Voice.Say("I have nothing to repeat. Ask me a question first, like Ramadan date.", "مفيش حاجة أعيدها. اسألني الأول، زي تاريخ رمضان.");
            return;
        }
        Answer(last.Title, last.English, last.Arabic, 20);
    }

    // True when what was heard is one of the person's questions (answered).
    private bool TryAnswerTopic(string heard, bool loose, bool sameSound = false)
    {
        if (TopicVoiceCommand.TryMatch(heard, _settings.Topics, loose, sameSound) is not { } match) return false;
        Keeper.SetVoiceStatus("complete");
        var today = DateTime.Today;
        switch (match)
        {
            case TopicVoiceCommand.ActionMatch { Action: var action }:
            {
                var (english, arabic) = TopicAnswers.Answer(action, today);
                Answer(action.Word, english, arabic, 20);
                break;
            }
            case TopicVoiceCommand.ActionsMatch { Actions: var actions }:
            {
                var answers = actions.Select(a => TopicAnswers.Answer(a, today)).ToList();
                var english = string.Join(" ", answers.Select(a => a.English));
                Answer(string.Join(", ", actions.Select(a => a.Word)), english, string.Join(" ", answers.Select(a => a.Arabic)), 25);
                break;
            }
            case TopicVoiceCommand.TopicMatch { Topic: var topic }:
            {
                var (english, arabic) = TopicAnswers.TopicContents(topic);
                Answer(topic.Name, english, arabic, 20);
                break;
            }
            default:
            {
                var (english, arabic) = TopicAnswers.TopicList(_settings.Topics);
                Answer("Your topics", english, arabic, 20);
                break;
            }
        }
        return true;
    }
}

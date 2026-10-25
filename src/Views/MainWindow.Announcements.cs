using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MiniCloClo;

// The announcement queue (#39): what Mini CloClo says on its own - the
// hourly time, custom reminders, the Quran review reminder - is said whole,
// one after another; what is due together is said as one announcement.
// It pauses what is playing - the Quran recitation, the news or other
// speech - plays a chime (for reminders), says it, then carries on: the
// recitation from the same ayah, speech from the sentence that was cut.
// It waits only for the azan, while the person is talking, and during the
// prayer look.
public partial class MainWindow
{
    private readonly List<Announcement> _announcements = new();
    private DispatcherTimer? _announcementTimer;
    private bool _announcing;
    // Set by "stop" (StopSounds) during an announcement: nothing carries on.
    private bool _announcementStopped;

    private void Announce(Announcement item)
    {
        _announcements.Add(item);
        if (_announcementTimer == null)
        {
            _announcementTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _announcementTimer.Tick += (_, _) => _ = SpeakAnnouncementsAsync();
        }
        _announcementTimer.Start();
        _ = SpeakAnnouncementsAsync();
    }

    // The azan isn't interrupted, nor the person while talking (the
    // microphone would hear it), nor the prayer.
    private bool AnnouncementsHeld =>
        _azanPlayer?.IsPlaying == true || _reminderAudioPlayer?.IsPlaying == true || _pushToTalk != null || _activityState == "prayer";

    private async Task SpeakAnnouncementsAsync()
    {
        if (_announcing || _closing) return;
        var now = DateTime.Now;
        _announcements.RemoveAll(a => now - a.Created > a.Expiry);
        if (_announcements.Count == 0)
        {
            _announcementTimer?.Stop();
            return;
        }
        if (AnnouncementsHeld) return;

        var items = _announcements.ToList();
        _announcements.Clear();
        _announcing = true;
        _announcementStopped = false;
        // Pause what is playing, to carry on with it afterwards.
        var speech = Voice.Pause();
        var quranWindow = _quranWindow;
        var ayah = quranWindow?.PauseRecitation();
        try
        {
            ShowAnnouncementLabel(items);
            if (items.Any(i => i.Kind == Announcement.Kinds.Medicine)) await Earcon.MedicineAsync();
            else if (items.Any(i => i.Kind != Announcement.Kinds.Hour)) await Earcon.ReminderAsync();
            // A reminder's own sound first (the azan still comes first: it
            // holds the queue).
            foreach (var path in items.Select(i => i.AudioPath).Where(p => p != null).Distinct())
            {
                if (_closing || _announcementStopped) break;
                if (_reminderAudioPlayer == null) _reminderAudioPlayer = new LocalAudioPlayer();
                if (!_reminderAudioPlayer.TryPlay(path!)) continue;
                for (var waited = 0; waited < 600 && _reminderAudioPlayer.IsPlaying && !_closing; waited++)
                    await Task.Delay(250);
            }
            var (english, arabic) = Announcement.Combine(items.Where(i => i.English.Length > 0).ToList());
            if (english.Length > 0 && !_closing && !_announcementStopped) await Voice.SayAndWaitAsync(english, arabic, TimeSpan.FromSeconds(90));
        }
        finally
        {
            _announcing = false;
        }
        // Carry on with what was playing - unless "stop" was said, the person
        // started talking, or the window was closed meanwhile.
        if (_closing || _announcementStopped || _pushToTalk != null) return;
        if (ayah is { } at && ReferenceEquals(quranWindow, _quranWindow)) quranWindow!.ResumeRecitation(at);
        if (speech != null) Voice.Resume(speech);
        // More may have come in meanwhile; the timer picks them up.
    }

    private void ShowAnnouncementLabel(IReadOnlyList<Announcement> items)
    {
        var reminders = items.Where(i => i.Kind is Announcement.Kinds.Reminder or Announcement.Kinds.Medicine).ToList();
        if (reminders.Count > 0)
        {
            ShowLabel(reminders[0].English, reminders.Count == 1 ? "Reminder" : $"Reminder 1 of {reminders.Count}", "⏰", seconds: 30);
            Keeper.SetCustomReminderActive(true);
            _reminderLookTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _reminderLookTimer.Stop();
            _reminderLookTimer.Tick -= ReminderLookTimer_Tick;
            _reminderLookTimer.Tick += ReminderLookTimer_Tick;
            _reminderLookTimer.Start();
        }
        else if (items.Any(i => i.Kind == Announcement.Kinds.Quran))
            ShowQuranReminderLabel();
    }
}

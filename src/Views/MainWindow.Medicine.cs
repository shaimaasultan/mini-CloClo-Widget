using System;
using System.Linq;

namespace MiniCloClo;

// Medicine reminders that wait for an answer (#29): at its time a medicine
// reminder asks "Did you take it? Say yes, or later." through the
// announcement queue (with its own chime), and again every few minutes
// until "yes" / "نعم" - logged as taken - or "later" / "بعدين", which asks
// again after a while. "Cancel" stops asking (skipped); when the time to
// answer runs out, the dose is logged as missed. "Did I take my medicine?"
// is answered from the log. Ctrl+Alt+Y answers "taken" from the keyboard.
public partial class MainWindow
{
    private readonly MedicineTracker _medicine = new();
    // The recitation talking stopped, and the ayah it was on: an answer to
    // the medicine question carries it on afterwards.
    private (QuranWindow Window, int Ayah)? _recitationBeforeTalk;

    // After Keeper's reply ("OK, I'll ask again...") has been said, so the
    // two don't overlap; not if the person talks again or says "stop".
    private async void ResumeRecitationAfterAnswer()
    {
        if (_recitationBeforeTalk is not { } before) return;
        _recitationBeforeTalk = null;
        await System.Threading.Tasks.Task.Delay(400); // the reply starts being synthesized
        for (var waited = 0; waited < 60 && SpeechReader.IsSpeaking; waited++)
            await System.Threading.Tasks.Task.Delay(250);
        if (!_closing && _pushToTalk == null && _recitationBeforeTalk == null && ReferenceEquals(before.Window, _quranWindow))
            before.Window.ResumeRecitation(before.Ayah);
    }

    // Every 20 seconds (MinuteTick): ask about the doses that are due, and
    // log those that weren't answered in time.
    private void MedicineTick(DateTime now)
    {
        var (ask, missed) = _medicine.Tick(now);
        foreach (var dose in missed)
        {
            MedicineLog.Record(dose.Message, dose.Due, MedicineLog.Missed, now);
            Announce(new Announcement(Announcement.Kinds.Reminder, $"I've noted {dose.Message} as missed",
                $"سجلت إن {dose.Message} فات", now));
        }
        if (ask.Count == 0) return;
        var (english, arabic) = MedicineTracker.Question(ask);
        Announce(new Announcement(Announcement.Kinds.Medicine, english, arabic, now));
    }

    // "Yes" / "later" while a medicine question waits: true when taken as the answer.
    private bool TryAnswerMedicine(string heard)
    {
        if (!_medicine.Waiting) return false;
        if (CallVoiceCommand.IsYes(heard))
        {
            MedicineTaken();
            ResumeRecitationAfterAnswer();
            return true;
        }
        if (!MedicineTracker.IsLater(heard)) return false;
        Keeper.SetVoiceStatus("complete");
        _medicine.Later(DateTime.Now);
        var minutes = (int)MedicineTracker.Snooze.TotalMinutes;
        ShowLabel("Medicine: later", $"I'll ask again in {minutes} minutes", "💊", seconds: 15);
        Voice.Say($"OK, I'll ask again in {minutes} minutes.", $"ماشي، هسألك تاني بعد {minutes} دقايق.");
        ResumeRecitationAfterAnswer();
        return true;
    }

    // "Yes", or Ctrl+Alt+Y: the doses asked about are taken now.
    internal void MedicineTaken()
    {
        var taken = _medicine.Taken();
        if (taken.Count == 0)
        {
            Voice.Say("No medicine is waiting for an answer.", "مفيش دوا مستني رد.");
            return;
        }
        Keeper.SetVoiceStatus("complete");
        var now = DateTime.Now;
        foreach (var dose in taken) MedicineLog.Record(dose.Message, dose.Due, MedicineLog.Taken, now);
        var names = string.Join(", ", taken.Select(d => d.Message));
        ShowLabel("Medicine taken", $"{names} — {SpokenTime.English(now)}", "💊", seconds: 15);
        Voice.Say($"Noted: taken at {SpokenTime.English(now)}.", $"تمام، اتسجل الساعة {SpokenTime.Arabic(now)}.");
    }

    // "Cancel" while a medicine question waits: stop asking about it.
    private bool CancelMedicine()
    {
        if (!_medicine.Waiting) return false;
        var now = DateTime.Now;
        foreach (var dose in _medicine.Skip()) MedicineLog.Record(dose.Message, dose.Due, MedicineLog.Skipped, now);
        ShowLabel("Medicine: not asking again", "Noted as skipped", "💊", seconds: 15);
        Voice.Say("OK, I won't ask again about it. Noted as skipped.", "ماشي، مش هسأل تاني. اتسجل إنه اتلغى.");
        ResumeRecitationAfterAnswer();
        return true;
    }

    // "Did I take my medicine (this morning)?" / "When did I last take it?"
    private void SayMedicineLog(string heard)
    {
        var medicines = _settings.CustomReminders.Where(r => r.WaitForAnswer).Select(r => r.Message).Distinct().ToList();
        var (english, arabic) = MedicineLog.Answer(medicines, heard, DateTime.Now);
        Answer("Medicine", english, arabic, 30);
    }
}

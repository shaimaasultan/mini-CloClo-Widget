using System;
using System.Linq;
using System.Threading.Tasks;

namespace MiniCloClo;

// Calls through the paired phone (BluetoothDialer): "call Mom" by voice is
// read back and dialed only after "yes"; the Contacts and calls window
// (Ctrl+Alt+K) dials the chosen contact at once. Only saved contacts can be
// called. The phone places the call; its sound stays on the phone.
public partial class MainWindow
{
    // How long a "call Mom" waits for "yes".
    private static readonly TimeSpan CallConfirmTime = TimeSpan.FromSeconds(45);
    private ContactSettings? _pendingCall;
    private DateTime _pendingCallUntil;
    private bool _dialing;

    internal void OpenContacts()
    {
        if (FocusOpenDialog()) return;
        var dialog = new ContactsWindow(_settings.Contacts, _settings.PhoneDeviceId, _settings.PhoneName);
        if (ShowDialogWindow(dialog) != true) return;
        _settings.Contacts = dialog.Contacts.ToList();
        if (dialog.Phone is { } phone)
        {
            _settings.PhoneDeviceId = phone.Id;
            _settings.PhoneName = phone.Name;
        }
        _settings.Save();
        if (dialog.CallContact is { } contact) _ = DialAsync(contact);
        else Voice.Say("Contacts saved.", "اتحفظت جهات الاتصال.");
    }

    // "Call ..." heard: true when it was a call command (handled here).
    private async Task<bool> TryHandleCallCommandAsync(string heard)
    {
        if (CallVoiceCommand.TryParse(heard) is not { } spokenName) return false;
        if (_settings.Contacts.Count == 0)
        {
            Keeper.SetVoiceStatus("failed");
            ShowLabel("No contacts yet", "Press Ctrl+Alt+K to add the people you call.", "📞");
            await SpeakPushToTalkStepAsync("You have no saved contacts yet. Press Control Alt K to add them.",
                "مفيش جهات اتصال محفوظة. دوس كنترول ألت K عشان تضيفهم.");
            return true;
        }
        if (CallVoiceCommand.Match(spokenName, _settings.Contacts) is not { } contact)
        {
            Keeper.SetVoiceStatus("failed");
            ShowLabel("No contact by that name", $"Heard: {spokenName}", "📞");
            await SpeakPushToTalkStepAsync("I don't have a contact by that name.", "مفيش اسم زي ده في جهات الاتصال.");
            return true;
        }
        _pendingCall = contact;
        _pendingCallUntil = DateTime.UtcNow + CallConfirmTime;
        Keeper.SetVoiceStatus("complete");
        ShowLabel($"Call {contact.Name}?", $"{contact.Number} — say yes, or cancel", "📞", seconds: (int)CallConfirmTime.TotalSeconds);
        await SpeakPushToTalkStepAsync(
            $"Call {contact.Name}, {ContactSettings.SpokenNumber(contact.Number)}? Talk to me again and say yes, or cancel.",
            $"أتصل ب{contact.OtherName ?? contact.Name}، {ContactSettings.SpokenNumber(contact.Number)}؟ كلمني تاني وقول ايوه، أو إلغاء.");
        return true;
    }

    // "Yes" while a call waits for it: true when it was taken as the answer.
    private bool TryConfirmPendingCall(string heard)
    {
        if (_pendingCall is not { } contact) return false;
        if (DateTime.UtcNow > _pendingCallUntil)
        {
            _pendingCall = null;
            return false;
        }
        if (!CallVoiceCommand.IsYes(heard)) return false;
        _pendingCall = null;
        Keeper.SetVoiceStatus("complete");
        _ = DialAsync(contact);
        return true;
    }

    // While a call waits for "yes", an answer that wasn't understood asks
    // the question again (the call keeps waiting). True when it did.
    private async Task<bool> AskAgainForPendingCallAsync(string heard)
    {
        if (_pendingCall is not { } contact) return false;
        if (DateTime.UtcNow > _pendingCallUntil)
        {
            _pendingCall = null;
            return false;
        }
        _pendingCallUntil = DateTime.UtcNow + CallConfirmTime;
        Keeper.SetVoiceStatus("failed");
        ShowLabel($"Call {contact.Name}?", $"Heard: {heard} — say yes, or cancel", "📞", seconds: (int)CallConfirmTime.TotalSeconds);
        await SpeakPushToTalkStepAsync($"I didn't catch that. Call {contact.Name}? Say yes, or cancel.",
            $"ما فهمتش. أتصل ب{contact.OtherName ?? contact.Name}؟ قول ايوه، أو إلغاء.");
        return true;
    }

    // "Cancel" while a call waits: true when there was one to cancel.
    private bool CancelPendingCall()
    {
        if (_pendingCall == null || DateTime.UtcNow > _pendingCallUntil)
        {
            _pendingCall = null;
            return false;
        }
        _pendingCall = null;
        ShowLabel("Call cancelled", "", "📞");
        Voice.Say("Call cancelled.", "تم إلغاء المكالمة.");
        return true;
    }

    private async Task DialAsync(ContactSettings contact)
    {
        if (_dialing)
        {
            Voice.Say("I'm already calling. Please wait a moment.", "بتصل دلوقتي. استنى لحظة.");
            return;
        }
        _dialing = true;
        try
        {
            var phone = await ChoosePhoneAsync();
            if (phone == null) return;
            ShowLabel($"Calling {contact.Name}…", $"Connecting to {phone.Name}", "📞", seconds: 30);
            await Voice.SayAndWaitAsync($"Calling {contact.Name}. Connecting to your phone.",
                $"بتصل ب{contact.OtherName ?? contact.Name}. بوصل بتليفونك.", TimeSpan.FromSeconds(8));
            var dialer = new BluetoothDialer();
            var ok = await dialer.RunAsync(phone, contact.Number);
            if (_closing) return;
            if (ok)
            {
                ShowLabel($"Calling {contact.Name}", $"on {phone.Name} — the call is on your phone", "📞", seconds: 30);
                Voice.Say($"Calling {contact.Name} on your phone.", $"المكالمة شغالة على تليفونك.");
                Keeper.Perk();
            }
            else
            {
                var reason = dialer.Log.LastOrDefault(l => !l.Contains("disconnected") && !l.Contains("Bluetooth left on") && !l.Contains("Bluetooth turned back off")) ?? "";
                ShowLabel("Couldn't reach your phone", reason.Length > 80 ? reason[..80] : reason, "📞", seconds: 30);
                Voice.Say("I couldn't reach your phone. Make sure it's nearby, unlocked, with Bluetooth on, then try again.",
                    "معرفتش أوصل لتليفونك. اتأكد إنه قريب ومفتوح والبلوتوث شغال، وجرب تاني.");
            }
        }
        finally
        {
            _dialing = false;
        }
    }

    // The saved phone, or - the first time - the paired phone that answers
    // as able to place calls, which is then remembered.
    private async Task<BluetoothDialer.PairedDevice?> ChoosePhoneAsync()
    {
        try
        {
            var paired = await BluetoothDialer.PairedDevicesAsync();
            if (_settings.PhoneDeviceId != null && paired.FirstOrDefault(p => p.Id == _settings.PhoneDeviceId) is { } saved)
                return saved;
            Voice.Say("Looking for your phone.", "بدور على تليفونك.");
            var phones = await BluetoothDialer.FindPhonesAsync();
            if (phones.Count > 0)
            {
                _settings.PhoneDeviceId = phones[0].Id;
                _settings.PhoneName = phones[0].Name;
                _settings.Save();
                return phones[0];
            }
        }
        catch
        {
            // No Bluetooth on this PC: reported below.
        }
        ShowLabel("No phone found", "Pair your phone in Windows Bluetooth settings", "📞", seconds: 30);
        Voice.Say("I couldn't find your phone. Pair it in Windows Bluetooth settings, keep it nearby with Bluetooth on, and try again.",
            "ما لقيتش تليفونك. اعمل له اقتران من إعدادات البلوتوث، وخليه قريب والبلوتوث شغال، وجرب تاني.");
        return null;
    }
}

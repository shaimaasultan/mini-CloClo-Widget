# Mini CloClo

A small, **offline** version of the CloClo desktop widget, made for someone who
can't see the screen well. It keeps only:

- **Voice commands** — talk to it instead of looking for it.
- **Custom reminders** — spoken out loud.
- **Prayer times and azan** — announced out loud, then the azan (a built-in
  recording, or your own).
- **Quran memorization** — recitation on a loop, controlled by keyboard or voice.
- **Calling** — "call Mom" places a call on your mobile phone over Bluetooth.
- **Your own questions and answers** — "Ramadan date", "when is Civic
  Holiday?": Islamic dates worked out on the PC, holidays and your own notes.
- **The avatar**, which changes with what's happening (prayer clothes, reading
  the Quran, reminder cardigan, pyjamas when asleep, a listening badge).
- **The time and date** — large, plus the Hijri date and the next prayer.

## Watch it

[![Mini CloClo — watch the video (1:53)](docs/video/promo-thumbnail.png)](https://github.com/shaimaasultan/mini-CloClo-Widget/releases/download/v1.0.0/MiniCloClo-promo.mp4)

▶ [Watch the video in English](https://github.com/shaimaasultan/mini-CloClo-Widget/releases/download/v1.0.0/MiniCloClo-promo.mp4) (1:53) ·
[شاهد الفيديو بالعربية](https://github.com/shaimaasultan/mini-CloClo-Widget/releases/download/v1.0.0/MiniCloClo-promo-ar.mp4) (2:19, narrated by Hoda).
Both have captions, so they work with the sound off. They're attached to the
[1.0 release](https://github.com/shaimaasultan/mini-CloClo-Widget/releases/tag/v1.0.0).

There is no weather, no location lookup and no internet at all: the build
fails if any code tries to go online (`scripts/check-no-network.ps1`). The one
connection it can make is over Bluetooth, to your own paired phone, and only
while placing a call you asked for (see [Calling](#calling)). Everything it
shows is also spoken, and everything can be reached without the mouse. The
widget and every window have names and labels for screen readers such as
Narrator.

## Requirements

- Windows 10 version 2004 or later, or Windows 11, 64-bit.
- For voice commands: a microphone, and a processor with AVX2 and FMA (most
  from 2015 on). Without them everything else still works, by keyboard.
- **Optional, for faster voice commands:** an NVIDIA graphics card with about
  1.5 GB of free graphics memory and a current NVIDIA driver. Mini CloClo then
  runs the speech model on it (CUDA); without one it uses the processor, which
  answers a few seconds later.
- For calls: Bluetooth, and a mobile phone paired with the PC.
- About 1.6 GB of disk (the speech model is 574 MB; the graphics-card files
  another 700 MB). While running it uses roughly 1–1.5 GB of memory, most of
  it the speech model, which is kept loaded so commands start at once.
- To build it: the [.NET 10 SDK](https://dotnet.microsoft.com/download), and
  [Git LFS](https://git-lfs.com) for the recitations and videos.

## Install

First, once: double-click **`scripts\Get-SpeechFiles.cmd`**. It downloads the
files too big for the repository and checks each one's fingerprint
(SHA-256) before using it:

- the **Whisper large-v3-turbo** speech model (574 MB), from this
  repository's [speech-model-1 release](https://github.com/shaimaasultan/mini-CloClo-Widget/releases/tag/speech-model-1)
  (or the whisper.cpp models on Hugging Face);
- **NVIDIA's cuBLAS** (548 MB, from NVIDIA's 422 MB download), so the model
  can run on an NVIDIA graphics card. Without an NVIDIA card, add `-NoCuda`
  to skip it.

This is the only install step that goes online; Mini CloClo itself never
does. Then double-click **`scripts\Install-MiniCloClo.cmd`** (as yourself, not as
administrator). It builds Mini CloClo, installs it into
`%LOCALAPPDATA%\Programs\MiniCloClo`, adds Start Menu and desktop shortcuts,
makes it start with Windows and starts it. It needs no .NET installed to run —
it carries its own runtime.

- **Ctrl+Alt+C** opens Mini CloClo from anywhere (the shortcuts' key). If it
  is already running, it says so.
- **To update**, run the installer again: it closes the running copy, replaces
  it and keeps your settings, reminders, contacts and Quran progress.
- **To remove it**: `scripts\Uninstall-MiniCloClo.cmd` (your settings are kept;
  `uninstall.ps1 -RemoveSettings` removes them too).

**Quran recitations:** Mishary Alafasy's recitation of **Al-Fatiha and
Al-Baqara** (293 ayat, 57 MB, stored with Git LFS) comes with the project in
`Assets\Recitations\Alafasy_64kbps\`. The installer packs every per-ayah MP3
in `Assets\Recitations\<reciter>\`, named like everyayah.com's (`001001.mp3`
is Al-Fatiha 1), into one file beside the app. To learn other surahs with
sound, add their files to that folder (or a new reciter's folder) and run the
installer again — or choose a folder of recitations from inside the Quran
window. Ayat without a recording are shown as text.

**First thing to set:** your city, for the prayer times. Click the 🕌 icon, or
press Ctrl+Alt+M → *Prayer times and azan…*, and pick it from the list (or
choose *Another place* and type its latitude and longitude). On first run it
picks a listed city in the PC's time zone, or Cairo.

**Speech voices:** replies use Windows' own voices. English works out of the
box. For Arabic replies, add an Arabic voice in **Settings → Time & language →
Speech → Add voices** (Microsoft Hoda is Egyptian), then turn on *Voice and
speech → Voice commands and replies in Arabic*.

**Optional — let Windows enforce "offline":** double-click
`scripts\Block-MiniCloClo-Network.cmd` and approve the administrator prompt. It
adds two Windows Firewall rules that block all of `MiniCloClo.exe`'s network
traffic, in and out, whatever code inside it tries. Calls still work, because
Bluetooth isn't network traffic. Check the rules any time with
`scripts\Set-MiniCloCloFirewall.ps1 -Check`; remove them with `-Remove` from an
administrator PowerShell.

## Talking to it

Hold the **right mouse button anywhere for a second** (or press
**Ctrl+Alt+V**, or click the 🎤 icon), wait for the short rising chime — that's
when it starts listening — say a command and let go (or press Ctrl+Alt+V
again). A short right-click still works as usual. Talking to it stops a
recitation, the azan or speech that is playing, so "stop", "next" and
"previous" work while the Quran is being recited.

| Say | It does |
|---|---|
| What time is it? / الساعة كام | Says the time |
| The date / التاريخ | Says the date and the Hijri date |
| Next prayer / الصلاة الجاية | Says the next prayer, its time and how long until it |
| Prayer times / مواقيت الصلاة | Reads all of today's prayer times |
| Quran / القرآن | Opens the Quran window where you left off; with it open, plays or pauses the recitation |
| Quran surah / القرآن سورة (or "choose a surah") | Asks which surah, from which ayah and to which ayah, then opens and recites it ([Quran](#what-it-does-on-its-own)) |
| Next / اللي بعده | Marks the ayah memorized and moves to the next |
| Previous / اللي قبله | Goes back one ayah |
| Reminders / التذكيرات | Opens the reminders |
| Call Mom / اتصل بماما | Reads the contact's number back, then calls after "yes" ([Calling](#calling)) |
| Stop / اسكت | Stops the azan, a recitation or speech |
| Cancel / إلغاء | Cancels a call or a shutdown waiting for an answer |
| I prayed / صليت | Ends the prayer look |
| Help / مساعدة | Says the commands and shortcuts |
| Ramadan date / تاريخ رمضان (any of your own words) | Answers with what you entered, or the next date ([Questions and answers](#questions-and-answers)) |
| Again / قولها تاني | Says the last answer to your own question again |
| What topics do I have? / مواضيعي | Lists your topics |
| Pull dates / اسحب التواريخ | Downloads fresh dates into your questions, after "yes" ([Pull](#pull)) — off until turned on |
| Pull news / اسحب الأخبار | Reads the latest headlines aloud, after "yes" ([Pull](#pull)) |
| Open Notepad, Word, Calculator, Narrator, Magnifier, Settings | Opens that app |
| Shut down / اقفل الجهاز | Shuts the PC down, after asking about open apps ("cancel" stops it) |

Recognition runs on this PC with the Whisper **large-v3-turbo** model — on
an NVIDIA graphics card when there is one (CUDA), otherwise on the
processor; what you say is never saved or sent. On the graphics card each
command is decoded carefully (beam search over 5 candidates, the full audio
window), which mishears less; on the processor it is decoded the quick way,
so it still answers within a few seconds. When a command isn't understood, the label under the
icons shows **"Heard: …"** — what the recognizer wrote — which helps tell a
noisy microphone from a misheard word.

## Keyboard

**From any app**, with Ctrl+Alt:

| Ctrl+Alt + | |
|---|---|
| **T** | The time |
| **D** | The date (and Hijri date) |
| **P** | The next prayer |
| **A** | All of today's prayer times |
| **Q** | Quran memorization |
| **R** | Custom reminders |
| **K** | Contacts and calls |
| **I** | Questions and answers |
| **V** | Talk (press again when done) |
| **S** | Stop sounds |
| **M** | The menu (opens with the keyboard in it) |
| **H** | Help |

If another app already uses one of these, Mini CloClo says so and the rest
still work. They can be turned off under *Voice and speech*.

**On the widget**, once it has focus (click it, or open its menu): the same
letters without Ctrl+Alt, plus **Space** to talk, **Shift+P** for all prayer
times, **Shift+Q** to choose a surah, **F1** for help, **Shift+F10** or the menu key for the menu, **Enter**
for "I've prayed" at prayer time, and **Esc** to close the label.

In the **Quran window**: Ctrl+P (or F5) plays/pauses, Ctrl+→ marks memorized
and moves on, Ctrl+← goes back, Ctrl+I says where you are, Ctrl+M reads the
English meaning. The buttons in its windows have Alt access keys (the underlined letter).

## Calling

Mini CloClo can call your saved contacts through your mobile phone over
Bluetooth, without Phone Link. Tested with an iPhone.

1. Pair the phone once in **Windows Settings → Bluetooth & devices**.
2. Press **Ctrl+Alt+K** (or menu → *Contacts and calls…*), choose **Add** for
   each person — the name you'll say, optionally another name such as the
   Arabic one, and the number — and **Find my phone**. Save.
3. Say **"call Mom"** (or "اتصل بماما"). CloClo reads the number back and asks;
   talk to it again and say **"yes"** — in Arabic **"نعم"** — (or "cancel" /
   "لا") within 45 seconds. A reply
   that starts with yes ("yes, call her") counts; one with "no" or "cancel"
   never does. In the window, select a contact and press **Enter** to call
   straight away.

| Contacts and calls (Ctrl+Alt+K) | Adding a contact |
|---|---|
| <img src="docs/screenshots/contacts.png" alt="Contacts and calls window: the phone that places calls (My iPhone) with a Find my phone button, a list of contacts - Mom with ماما, Ahmed with أحمد, Dr. Sara's clinic - and Call, Add, Edit, Remove and Say the number buttons" width="400"> | <img src="docs/screenshots/contact-editor.png" alt="New contact form: Name Mom, other name ماما, phone number 416 555 0123, with Say the number, Cancel and Save" width="330"> |

How it works: CloClo connects to the phone's hands-free service, the way a car
kit does, sends one dial request, and disconnects. The phone places the call
on its own line, and **the call's sound is on the phone** (or a headset paired
to the phone), not the PC; you hang up on the phone. The phone needs to be
nearby, unlocked and with Bluetooth on. If the PC's Bluetooth was off, CloClo
turns it on for the call and back off afterwards (Windows must allow apps to
control radios: **Settings → Privacy & security → Radios**); if it was on (a
Bluetooth headset in use), it leaves it on. Only saved contacts can be called,
and a voice call is always confirmed first. The window (Ctrl+Alt+K, Enter)
is always there as the dependable way when a name keeps being misheard.

## Questions and answers

Your own questions, by **topic**: each topic holds **words you say** —
*Ramadan*, *Civic Holiday*, *Clinic* — and Keeper answers each one with:

- **text** you typed, read exactly as written ("Dr. Sami's clinic is open
  Sunday to Thursday, 9 to 5");
- **dates** — the next one, its weekday and how far away it is, with the
  Hijri date: "Civic Holiday. In 284 days, on Monday, August 2, 2027, 29 Safar
  1449";
- a **Hijri date** — worked out on the PC every year with Windows' Umm
  al-Qura calendar (it may differ by a day from a local moon sighting):
  "Ramadan. In 109 days, on Monday, February 8, 2027, 1 Ramadan 1448."

**Islamic dates** come ready-made: Isra and Mi'raj, Shab-e-Barat, Ramadan,
Laylat al-Qadr, Eid al-Fitr, Dhul Hijjah, Hajj, Arafah, Eid al-Adha, the
Islamic new year and Ashura, each with its Arabic name.

**Asking:** say the word, or "Ramadan date", "when is Ashura?", "how many
days until Eid?", "date of Hajj", "تاريخ رمضان", "رمضان امتى؟", "فاضل قد ايه
على العيد؟". "Eid" / "العيد" answers both Eids. A word misheard by a sound
("Laylat al-Kodim", "A surah" for Ashura) still finds it. Say a topic's name
to hear what's in it, "what topics do I have?" / "مواضيعي" for the list, and
**"again"** / **"قولها تاني"** to hear the last answer again (for 5 minutes).

**Editing:** **Ctrl+Alt+I** (or menu → *Questions and answers…*). Topics are
on the left and their words on the right: *Add…* / *Edit…* asks for the word
(and another way to say it, e.g. in Arabic) and the kind of answer; *Say the
answer* tries it. Words that are already commands ("time", "Quran", an app's
name, "call …") can't be used. *Export…* / *Import…* share the list as a
file; importing a topic with the same name replaces it.

**Published dates from the internet** (optional, outside the app):
double-click `tools\Get-CalendarTopics.cmd`, give your country (e.g. `CA`)
and province (e.g. `CA-ON`), and it saves *CloClo calendar <year>.json* in
your Downloads folder with this year's and next year's Islamic dates
([Aladhan](https://aladhan.com), the Saudi official calendar by default) and
public holidays ([Nager.Date](https://date.nager.at)). Import it with
*Import…*: it replaces *Islamic dates* with the published dates, and adds
*Public holidays*. Mini CloClo itself still never goes online — only this
script does, when you run it; run it again each year.

## Pull

**"Pull dates"** / **"اسحب التواريخ"** fetches fresh data by voice, with no
window: a **pull tool** goes online, and its result is added to Questions and
answers — or, for tools that make one, read aloud.

1. Say **"pull dates"** ("what can I pull?" lists the tools; "pull" alone
   works when there is one).
2. Keeper asks first: *"Pull dates? This goes online to download Islamic
   dates and public holidays. Talk to me again and say yes, or cancel."*
3. *"Running the dates tool."* … *"Importing the file."* … *"Import done."* —
   then the topics added or updated and their words, ready to ask about
   ("Ramadan date"). "Again" repeats it. If something goes wrong, Keeper says
   what (no internet, too slow, nothing returned).

The tools that come with it:

- **dates** — Islamic dates and public holidays, added to Questions and
  answers. Left empty, the country and province come from your
  **prayer-times city** (Mississauga → Canada, Ontario: national holidays
  plus Family Day and the Civic Holiday), else from Windows' "Country or
  region" setting. Settings: country (e.g. `CA`; `none`: no holidays),
  province (e.g. `CA-ON`), the Islamic calendar method, and how many years.
- **news** — the latest headlines, read aloud: *"Here it is. CBC Top
  Stories. 5 items. 1: … Say a number to hear more."* Then say a headline's
  number — "two" / "اتنين" — to hear that story's summary; another number
  reads another (for 5 minutes). "Again" repeats, "stop" stops. Settings:
  the English feed (CBC Top Stories) and the Arabic feed (BBC Arabic), used by
  the voice commands' language, and how many headlines (1–10). Any RSS or
  Atom feed address (`https://…`) works.

"Pull" is often heard as "Bull" or "Paul": a word that sounds like it,
followed by a tool's name, counts.

**Pull commands are off until you turn them on**, because the tools go
online: menu → **Pull tools…** → *Allow pull commands (they go online)*. The
same window sets each tool's settings.

**Mini CloClo itself still never goes online.** It only starts a tool from
the `pull` folder beside the app — never anything you said — after you say
yes, and reads the file the tool writes. Its own network check and the
firewall rules for `MiniCloClo.exe` are unchanged; the tools are separate
PowerShell scripts.

**Adding a tool** (later: news, trends…): a folder `pull\<name>\` with
`tool.json` — its spoken name (`name`, `otherName`), what it goes online for
(`description`, `otherDescription`), `"result": "import"` or `"read"`,
`timeoutSeconds`, and its `settings` with `settingLabels` — and `run.ps1`,
which is run as `run.ps1 <settings file> <result file>`. The settings file
holds the tool's `settings`, `"language": "en"` or `"ar"` (the voice
commands' language) and `"place"`: the prayer-times city, its country code
and, where holidays differ by region, its province or state
(`{ "city": "Mississauga", "country": "CA", "region": "CA-ON" }`). An `import` tool
writes a list of topics (the same file *Import…* reads); a `read` tool
writes `{ "title": "...", "items": [ { "text": "...", "otherText": "..." } ] }`
(up to 10 items, read aloud in order).

## What it does on its own

- **Prayer times** are worked out on the PC (Egyptian, Umm al-Qura, Muslim
  World League, Karachi or ISNA; Standard or Hanafi Asr). At each prayer it
  says "It's time for the Asr prayer", then plays the azan — the built-in
  recording from `Assets\Azan`, or your own chosen with *Choose azan audio…*
  (*Use built-in* goes back) — and the avatar changes into prayer clothes on a
  prayer mat until you say "I prayed" (or click the green check or the head).
  It can also warn you 5–30 minutes before each prayer (10 by default).
  Prayers missed while the PC was asleep or off aren't announced late.
- **Custom reminders** (up to 10): a message, how often (15 minutes to 4
  hours) and active hours. Each is spoken, and can play a sound file first.
  Open them with the ⏰ icon, Ctrl+Alt+R or by saying "reminders"; **Add**
  makes a new one and **Say it now** tries it out.

  They come **on the clock**: at the start hour and every interval after it
  (every hour from 9 → 9:00, 10:00…; every 90 minutes → 9:00, 10:30,
  12:00…), also across midnight. A restart or reinstall doesn't reset them;
  one missed while the PC was asleep is said once if it was due in the last
  15 minutes. **Nothing is cut off:** reminders, the hourly time and the
  Quran review wait their turn and what is due together is said as one —
  *"It's 3:00 PM. Reminders: drink a glass of water. Take your medicine."* —
  and they wait, instead of being lost, while the azan plays, a recitation is
  on, Keeper is speaking or you're answering a question (a reminder waits up
  to an hour, the hourly time 5 minutes).

  | Your reminders | Adding a reminder |
  |---|---|
  | <img src="docs/screenshots/reminders.png" alt="Custom reminders window listing Drink a glass of water every hour 9 to 21, Take your medicine every 4 hours 8 to 20, and Stand up and stretch every 2 hours (off), with Add, Edit, Say it now, Turn off and Remove buttons" width="400"> | <img src="docs/screenshots/reminder-editor.png" alt="New reminder form: message Take your medicine, repeat every 4 hours, active from 8:00 AM until 8:00 PM, reminder on, say the message out loud, and buttons to choose and preview a sound" width="340"> |

- **Quran**: the Uthmani text with the English meaning, recited ayah by ayah
  on a loop from the installed recitations or a folder you choose. It
  remembers what you've memorized, and can remind you to review every 30
  minutes to 2 hours. Open it with the 📗 icon, Ctrl+Alt+Q or by saying
  "Quran"; **Repeat** (Ctrl+P) plays the ayah on a loop and **Memorized —
  next ayah** (Ctrl+→) moves on.

  **Choose a surah by voice:** say **"Quran surah"** (or "القرآن سورة",
  "choose a surah", "اختار سورة", or just "surah"), and Keeper asks three
  questions — plain "Quran" never asks them — each answer
  is its own hold of the right button after the chime:
  1. *Which surah?* — its number ("2") or name ("Al-Baqara", "the Cow",
     "البقرة"). Keeper says it back: "Surah Al-Baqara, 286 ayat."
  2. *From which ayah?* — a number, or **"all"** ("the whole surah", "كلها")
     to recite the whole surah from the first ayah to the last, starting again
     from the first when it ends.
  3. *To which ayah?* — a number, or "to the end". The Quran window opens on
     those ayat and recites them on a loop (up to 20 ayat at a time; a longer
     run is shortened to 20, and Keeper says so).

  Numbers are said **digit by digit**, and Keeper asks for them that way:
  "one four" is 14, "one seven" 17, "two three four" 234 ("واحد أربعة",
  "اتنين تلاتة أربعة"). Ten short digit words are far easier to hear than
  "fourteen" or "seventeen". Only those ten words are matched by sound
  ("tree" for three, "سبات" for سبعة), so other speech is asked again rather
  than guessed at. "14" said whole still works when it is heard right.
  With voice commands in Arabic, the digits may be said in Arabic **or in
  English** — "واحد أربعة" or "one four": each answer is read both ways, and
  the reading that is just a number is used.

  An unclear answer is asked again (the label shows what was heard). If
  Keeper reads back the wrong surah or ayah, say **"no"** (or "back", "لا",
  "ارجع") to go back one question and answer it again — at the first
  question, "no" stops. "Cancel" stops the questions at any time. The menu has *Choose a surah by voice…*, and
  Shift+Q starts it when the widget has focus.

  <img src="docs/screenshots/quran.png" alt="Quran memorization window on Al-Fatiha, ayah 2, in large Uthmani script with its English meaning, Show English ticked, Previous, Repeat and Memorized - next ayah buttons, a progress bar showing 1 of 7 ayat memorized, and the installed Alafasy recitations" width="480">
- **The time every hour**, if you turn it on (*Voice and speech*).

Reminders keep coming while the avatar is asleep (after the *Sleep after*
time with no keyboard or mouse use), since not touching the PC doesn't mean
nobody is listening. They wait only while the prayer look is on, until "I
prayed".

## The widget

The time sits on top, large, with the sun by day and the moon at night: from
sunrise and sunset in your city when it's in the PC's time zone, otherwise
6 AM to 6 PM by the PC's clock. Below the avatar are the date, the Hijri date
and the next prayer, each on a dark plate that reads on any wallpaper. Under
them, four icons: 🕌 prayer times, 📗 Quran, ⏰ reminders, 🎤 talk, and a
label for messages — click it to hear it again, or its ✕ to close it. Click
the head to hear the time; drag to move the widget; right-click or
double-click for the menu. *Look → Size* makes it 1×, 1.5× (default) or 2× as
large.

The menu also has the avatar (Keeper or Lady Keeper) and outfit colour,
motion, when it falls asleep, speaking speed, the startup greeting, always on
top and start with Windows. The **tray icon** (by the clock) has the same
menu, plus *Hide/Show the widget*; double-click it to hide or show the widget —
hidden, it keeps working and its shortcuts too.

## Troubleshooting

| What happens | What to do |
|---|---|
| Nothing is spoken | Check the volume and that a Windows voice is installed (Settings → Time & language → Speech) |
| "Voice commands need a newer processor" | The PC lacks AVX2/FMA; use the keyboard shortcuts |
| "Listening stopped" / "Didn't hear a command" | Check the default microphone (Settings → System → Sound), and speak after the chime |
| A command isn't understood | Look at "Heard: …" on the label; speak a little slower, closer to the microphone |
| "Some shortcuts are taken" | Another app uses that Ctrl+Alt key; the rest still work |
| "Couldn't reach your phone" | Phone nearby, unlocked, Bluetooth on; in Contacts and calls, *Find my phone* again |
| Phone Link says "blocked by your system administrator" | Not needed: calls go straight over Bluetooth |
| Install or uninstall says a file is in use | Choose *Exit* from Mini CloClo's menu and run it again |

## Where things are kept

- Settings, reminders, contacts (names and numbers, in plain text) and your
  questions and answers: `%APPDATA%\MiniCloClo\settings.json`
- Quran progress: `%APPDATA%\MiniCloClo\quran.json`
- Error log (exception types only, no text you said or typed):
  `%LOCALAPPDATA%\MiniCloClo\errors.log`

Nothing else is written: audio from the microphone stays in memory and is
dropped after each command.

## Project layout

- `src/Views/` — the widget (`MainWindow*.cs`, `KeeperView`) and its windows:
  prayer times, Quran, reminders, contacts and calls.
- `src/Services/` — prayer times and the offline city list, Quran text and
  progress, speech and the listening chime, the azan library, settings.
- `src/Platform/` — voice commands and call matching, the right-button hook,
  the Bluetooth dialer, single instance, startup and shutdown.
- `scripts/` — install, uninstall, recitation packing, the offline check and
  the firewall rules.
- `tools/Get-CalendarTopics.ps1` (and `.cmd`) — downloads published Islamic
  dates and public holidays into a file to import (the one part that goes
  online, run by hand or by "pull dates").
- `pull/` — the pull tools, installed beside the app: `pull/dates/` runs
  the calendar script with the settings from *Pull tools…*.
- `tools/PromoVideo/` — makes the promo videos, offline: scenes drawn with the
  app's own avatar, narration by a Windows voice, encoded by Windows.
  `PromoVideo.exe <output.mp4>` for English, add `--arabic` for Arabic. It
  hides the account name the avatar shows on its shirt. The videos are in
  `docs/video/`, stored with Git LFS, and attached to each release — the
  README links the release copies, whose downloads don't use the LFS
  bandwidth allowance.

Debug builds have these test switches:

- `MiniCloClo.exe --snapshot <folder>` draws every avatar look to PNG files.
- `--voice-test <file>` speaks test commands with a Windows voice and runs
  them through the recognizer.
- `--prayer-test <file>` writes today's times, sunrise and sunset for a few
  cities.
- `--topics-test <file> [import file]` checks the questions and answers: the
  answers, the Hijri dates, what is said, words that clash with commands —
  and, with a file, imports it into the window.
- `--quran-test <file>` plays a whole surah and a range in the Quran window.
- `--pull-test <file> [real]` runs local test pull tools (import, read, a
  failure, a time-out, no result — none goes online) and checks what is said;
  with `real`, the dates tool also runs once.
- `--voice-test <file> [text] [--model <ggml file>]` runs only the spoken
  phrases containing that text, and can compare another model; every
  recognition is timed.
- `--engine-check <file>` says whether the speech model runs on the graphics
  card or the processor, and times loading it and one recognition.
- `--load-trace <file>` records which part of the app loads any networking
  library (only WPF's startup and Windows' speech voices do, and none of them
  connects anywhere).
- `--bt-test <file> <phone> [number]` connects to the paired phone and does
  the hands-free handshake — with a number it also dials it and checks the
  call outlives the disconnect.

## License

Mini CloClo's code and artwork are under the [MIT License](LICENSE),
© 2026 Shaimaa Said Soltan — shaimaasultan@hotmail.com ·
[github.com/shaimaasultan](https://github.com/shaimaasultan).

The Quran text, its English meaning, the recitations, the azan and the speech
model are others' work under their own terms — some for non-commercial use
only — listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Credits

Built from the full CloClo widget (`C:\cloclo-widget`). The Quran text is the
[Tanzil Project](https://tanzil.net)'s verified Uthmani text (CC BY 3.0) and
the English meaning is Saheeh International's, as published by Tanzil, both
unchanged. The recitations are Mishary Alafasy's, from
[everyayah.com](https://everyayah.com), included for personal, non-commercial
use; all rights in them remain with the reciter. The built-in azan is recited by Aaqib Azeez. Speech
recognition: [whisper.cpp](https://github.com/ggerganov/whisper.cpp) through
Whisper.net.

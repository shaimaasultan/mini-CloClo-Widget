# Mini CloClo

A small, **completely offline** version of the CloClo desktop widget, made for
someone who can't see the screen well. It keeps only:

- **Voice commands** — talk to it instead of looking for it.
- **Custom reminders** — spoken out loud.
- **Prayer times and azan** — announced out loud, then the azan (a built-in
  recording, or your own).
- **Quran memorization** — recitation on a loop, controlled by keyboard or voice.
- **The avatar**, which changes with what's happening (prayer clothes, reading
  the Quran, reminder cardigan, pyjamas when asleep, a listening badge).
- **The time and date** — large, plus the Hijri date and the next prayer.

There is no weather, no location lookup and no internet at all: the build
fails if any code tries to go online (`scripts/check-no-network.ps1`).
Everything it shows is also spoken, and everything can be reached without
the mouse.

## Install

Double-click **`scripts\Install-MiniCloClo.cmd`** (as yourself, not as
administrator). It builds Mini CloClo, installs it into
`%LOCALAPPDATA%\Programs\MiniCloClo`, adds Start Menu and desktop shortcuts,
makes it start with Windows and starts it. To build it you need the
[.NET 10 SDK](https://dotnet.microsoft.com/download); to run it, nothing —
it carries its own runtime.

- **Ctrl+Alt+C** opens Mini CloClo from anywhere (the shortcuts' key).
- To remove it: `scripts\Uninstall-MiniCloClo.cmd` (your settings are kept;
  `uninstall.ps1 -RemoveSettings` removes them too).

**First thing to set:** your city, for the prayer times. Say "prayer times"
or press Ctrl+Alt+M → *Prayer times and azan…* and pick it from the list (or
choose *Another place* and type its latitude and longitude). On first run it
picks a listed city in the PC's time zone, or Cairo.

**Speech voices:** replies use Windows' own voices. English works out of the
box. For Arabic replies, add the Arabic speech pack in **Settings → Time &
language → Speech** (Microsoft Hoda), then turn on *Voice and speech → Voice
commands and replies in Arabic*.

## Talking to it

Hold the **right mouse button anywhere for a second** (or press
**Ctrl+Alt+V**, or click the 🎤 icon), wait for the short rising chime (the 🎤 badge turns blue), say a command and
let go (press Ctrl+Alt+V again). A short right-click still works as usual.

| Say | It does |
|---|---|
| What time is it? / الساعة كام | Says the time |
| The date / التاريخ | Says the date and the Hijri date |
| Next prayer / الصلاة الجاية | Says the next prayer, its time and how long until it |
| Prayer times / مواقيت الصلاة | Reads all of today's prayer times |
| Quran / القرآن | Opens the Quran window; with it open, plays or pauses the recitation |
| Next / اللي بعده | Marks the ayah memorized and moves to the next |
| Previous / اللي قبله | Goes back one ayah |
| Reminders / التذكيرات | Opens the reminders |
| Stop / اسكت | Stops the azan, a recitation or speech |
| I prayed / صليت | Ends the prayer look |
| Help / مساعدة | Says the commands and shortcuts |
| Open Notepad, Word, Calculator, Narrator, Magnifier, Settings | Opens that app |
| Shut down / اقفل الجهاز | Shuts the PC down, after asking about open apps ("cancel" stops it) |

Recognition runs on this PC with the bundled Whisper *base* model; no audio
is saved or sent. English is recognized more reliably than Arabic with this
small model. Talking to it stops a recitation or azan that is playing, so
"stop", "next" and "previous" work while the Quran is being recited.

## Keyboard shortcuts (from any app)

| Ctrl+Alt + | |
|---|---|
| **T** | The time |
| **D** | The date (and Hijri date) |
| **P** | The next prayer |
| **A** | All of today's prayer times |
| **Q** | Quran memorization |
| **R** | Custom reminders |
| **V** | Talk (press again when done) |
| **S** | Stop sounds |
| **M** | The menu (opens with the keyboard in it) |
| **H** | Help |

If another app already uses one of these, Mini CloClo says so and the rest
still work. They can be turned off under *Voice and speech*.

In the **Quran window**: Ctrl+P (or F5) plays/pauses, Ctrl+→ marks memorized
and moves on, Ctrl+← goes back, Ctrl+I says where you are, Ctrl+M reads the
English meaning. Every button also has an Alt access key.

## What it does on its own

- **Prayer times** are worked out on the PC (Egyptian, Umm al-Qura, Muslim
  World League, Karachi or ISNA; Standard or Hanafi Asr). At each prayer it
  says "It's time for the Asr prayer", then plays the azan — the built-in
  recording from `Assets\Azan` (Aaqib Azeez), or one of your own chosen with
  *Choose azan audio…* (*Use built-in* goes back) — and the avatar changes into prayer clothes on a prayer mat until you
  say "I prayed" (or click the green check or the head). It can also warn you
  5–30 minutes before each prayer (10 by default).
- **Custom reminders** (up to 10): a message, how often (15 minutes to 4
  hours) and active hours. Each is spoken, and can play a sound file first.
- **Quran**: the Uthmani text with the English meaning, recited ayah by ayah
  on a loop from the recitations installed with it (Mishary Alafasy, Al-Fatiha
  and Al-Baqara, packed from `Assets\Recitations`), or from a folder of
  per-ayah MP3s you choose. It remembers what you've memorized, and can remind
  you to review every 30 minutes to 2 hours.
- **The time every hour**, if you turn it on.

## The widget

The time sits on top, large, with the sun by day and the moon at night: from
sunrise and sunset in your city when it's in the PC's time zone, otherwise
6 AM to 6 PM by the PC's clock. Below the avatar are the date, the Hijri date
and the next prayer, each on a dark plate that reads on any wallpaper. Under
them, four icons: 🕌 prayer times, 📗 Quran, ⏰ reminders, 🎤 talk. Click the
head to hear the time; drag to move it; right-click or double-click for the
menu. *Look → Size* makes it 1×, 1.5× (default) or 2× as large.

The menu also has the avatar (Keeper or Lady Keeper) and outfit colour,
motion, when it falls asleep, speaking speed, always on top and start with
Windows.

## Where things are kept

- Settings and reminders: `%APPDATA%\MiniCloClo\settings.json`
- Quran progress: `%APPDATA%\MiniCloClo\quran.json`
- Error log (exception types only): `%LOCALAPPDATA%\MiniCloClo\errors.log`

## Project layout

- `src/Views/` — the widget (`MainWindow*.cs`, `KeeperView`), the prayer,
  Quran and reminder windows.
- `src/Services/` — prayer times, Quran text and progress, speech, the
  offline city list, settings.
- `src/Platform/` — voice commands, the right-button hook, single instance,
  startup and shutdown.
- `scripts/` — install, uninstall, recitation packing, the offline check.

Debug builds have three test switches: `MiniCloClo.exe --snapshot <folder>`
draws every avatar look to PNG files, `--voice-test <file>` speaks test
commands with a Windows voice and runs them through the recognizer, and
`--prayer-test <file>` writes today's times for a few cities.

## Credits

Built from the full CloClo widget (`C:\cloclo-widget`). The Quran text is the
[Tanzil Project](https://tanzil.net)'s verified Uthmani text (CC BY 3.0) and
the English meaning is Saheeh International's, as published by Tanzil, both
unchanged. The recitations are Mishary Alafasy's, from everyayah.com, for
personal use. Speech recognition: [whisper.cpp](https://github.com/ggerganov/whisper.cpp)
through Whisper.net.

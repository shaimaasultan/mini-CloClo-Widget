using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MiniCloClo;

// The azan recordings installed with Mini CloClo (Assets\Azan, copied to an
// Azan folder beside the exe). Used whenever no recording of the person's
// own is chosen.
public static class AzanLibrary
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Azan");

    // The playable MP3/WAV files there, by name.
    public static IReadOnlyList<string> Files()
    {
        try
        {
            if (!Directory.Exists(Folder)) return [];
            return Directory.GetFiles(Folder)
                .Where(LocalAudioPlayer.IsPlayable)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static string? Default => Files().FirstOrDefault();

    // "Aaqib Azeez" for "Aaqib_Azeez.mp3".
    public static string DisplayName(string path) => Path.GetFileNameWithoutExtension(path).Replace('_', ' ');

    // The person's own recording if it can be played, otherwise the built-in one.
    public static string? Effective(string? chosen) => LocalAudioPlayer.IsPlayable(chosen) ? chosen : Default;
}

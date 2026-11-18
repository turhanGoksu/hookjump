using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hookjump.App;

/// <summary>
/// The id of a song on Windows, where the media controls give no id
/// (spec/STORAGE.md, "Track ids on Windows").
/// </summary>
/// <remarks>
/// It is made from the song's details but says nothing about the song by
/// itself, so the samples kept for a week still name no song.
/// </remarks>
public static class TrackKey
{
    public const string Prefix = "smtc:";
    private const char Separator = '\u001F';

    /// <summary>The song's id, or null when there is no title: nothing is loaded.</summary>
    public static string? Of(string? title, string? artist, string? album, int trackNumber)
    {
        if (string.IsNullOrEmpty(title)) return null;
        // The fields exactly as Windows gives them: no normalization, which .NET
        // would not do anyway with InvariantGlobalization on.
        var text = string.Join(
            Separator, title, artist ?? "", album ?? "", trackNumber.ToString(CultureInfo.InvariantCulture));
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

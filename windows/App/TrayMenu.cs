using System.Collections.Generic;
using System.Linq;
using Hookjump.Core;

namespace Hookjump.App;

/// <summary>What a menu entry does when chosen; None is a line that only tells something.</summary>
public enum Command
{
    None,
    Jump,
    Pin,
    PinAtTime,
    RemovePin,
    ForgetLearned,
    ForgetSong,
    IgnoreSong,
    TrackAgain,
    PauseLearning,
    ChangeJumpKey,
    ChangePinKey,
    DefaultKeys,
    ForgetAllLearned,
    DeleteEverything,
    Quit,
}

/// <summary>One entry of the notification-area menu, as plain data; Keys is its shortcut, shown at the right.</summary>
public sealed record MenuEntry(
    string Text,
    Command Command = Command.None,
    bool Checked = false,
    IReadOnlyList<MenuEntry>? Children = null,
    string? Keys = null)
{
    public static readonly MenuEntry Separator = new("");

    public bool IsSeparator => Text.Length == 0;

    /// <summary>A line that tells something and cannot be chosen.</summary>
    public bool IsNote => Command == Command.None && Children is null && !IsSeparator;
}

/// <summary>What the menu needs to know, read from the booth and the store when it opens.</summary>
public sealed record TrayState(
    string? Title,
    string? Artist,
    bool HasTrack,
    JumpResult Decision,
    bool Pinned,
    bool Ignored,
    bool LearningPaused,
    IReadOnlyList<Record> Records,
    string? LastAnswer,
    ShortcutChoice? Shortcuts = null,
    bool ChosenShortcuts = false); // the listener changed the keys from the defaults

/// <summary>
/// The menu of the hook in the notification area, decided apart from Windows so
/// it is tested without a screen. Its words are the Mac menu's.
/// </summary>
public static class TrayMenu
{
    public const string NothingLeaves = "Nothing leaves this PC.";

    public static List<MenuEntry> Build(TrayState state)
    {
        var menu = new List<MenuEntry>();
        if (state.HasTrack)
        {
            var name = Words.RecordName(new Record(state.Title, state.Artist, null, null, 0, 0, false));
            menu.Add(new MenuEntry(Words.Shorten(name ?? "A song with no name")));
        }
        menu.Add(new MenuEntry(Words.DescribeTarget(state.Decision)));
        if (state.LastAnswer is { } answer) menu.Add(new MenuEntry(answer));
        if (state.Shortcuts?.Note is { } note) menu.Add(new MenuEntry($"⚠ {note}"));
        menu.Add(MenuEntry.Separator);

        menu.Add(new MenuEntry("Jump", Command.Jump, Keys: state.Shortcuts?.Jump?.ToString()));
        menu.Add(new MenuEntry("Pin", Command.Pin, Keys: state.Shortcuts?.Pin?.ToString()));
        if (state.HasTrack)
        {
            var song = new List<MenuEntry> { new("Pin at a Time…", Command.PinAtTime) };
            if (state.Pinned) song.Add(new MenuEntry("Remove the Pin", Command.RemovePin));
            // An ignored song learns nothing, so there is nothing to forget.
            if (!state.Ignored) song.Add(new MenuEntry("Forget What Was Learned", Command.ForgetLearned));
            song.Add(new MenuEntry("Forget This Song", Command.ForgetSong));
            song.Add(state.Ignored
                ? new MenuEntry("Track This Song Again", Command.TrackAgain)
                : new MenuEntry("Don't Track This Song", Command.IgnoreSong));
            menu.Add(new MenuEntry("This Song", Children: song));
        }
        menu.Add(new MenuEntry("Pause Learning", Command.PauseLearning, Checked: state.LearningPaused));
        menu.Add(new MenuEntry("What hookjump Keeps", Children: Kept(state.Records)));
        menu.Add(new MenuEntry("Shortcuts", Children: ShortcutEntries(state)));
        menu.Add(MenuEntry.Separator);

        menu.Add(new MenuEntry("Forget Everything Learned…", Command.ForgetAllLearned));
        menu.Add(new MenuEntry("Delete Everything…", Command.DeleteEverything));
        menu.Add(new MenuEntry("Quit hookjump", Command.Quit));
        return menu;
    }

    /// <summary>Changing the keys, each showing the keys it has now; the defaults back once something was changed.</summary>
    private static List<MenuEntry> ShortcutEntries(TrayState state)
    {
        var entries = new List<MenuEntry>
        {
            new("Change the Jump Key…", Command.ChangeJumpKey, Keys: state.Shortcuts?.Jump?.ToString()),
            new("Change the Pin Key…", Command.ChangePinKey, Keys: state.Shortcuts?.Pin?.ToString()),
        };
        if (state.ChosenShortcuts) entries.Add(new MenuEntry("Use the Default Keys", Command.DefaultKeys));
        return entries;
    }

    /// <summary>
    /// Each named song, with what is kept about it one level further; the songs
    /// kept before names were, in one line: they get a name when they play again.
    /// </summary>
    private static List<MenuEntry> Kept(IReadOnlyList<Record> records)
    {
        var entries = new List<MenuEntry>();
        if (records.Count == 0) entries.AddRange(Words.NothingKept.Select(line => new MenuEntry(line)));
        var unnamed = 0;
        foreach (var record in records)
        {
            if (Words.RecordName(record) is not { } name)
            {
                unnamed += 1;
                continue;
            }
            var facts = Words.DescribeRecord(record).Select(line => new MenuEntry(line)).ToList();
            entries.Add(new MenuEntry(Words.Shorten(name), Children: facts));
        }
        if (unnamed > 0) entries.Add(new MenuEntry(Words.UnnamedSongs(unnamed)));
        entries.Add(MenuEntry.Separator);
        entries.Add(new MenuEntry(NothingLeaves));
        return entries;
    }
}

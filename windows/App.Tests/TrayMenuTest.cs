using System.Collections.Generic;
using System.Linq;
using Hookjump.Core;
using Xunit;

namespace Hookjump.App.Tests;

/// <summary>The notification-area menu, as data: what it says and offers in each state.</summary>
public class TrayMenuTest
{
    private static readonly TrayState Playing = new(
        "Sorma", "Motive", HasTrack: true, new JumpResult(Status.Personal, 103500),
        Pinned: false, Ignored: false, LearningPaused: false, Records: [], LastAnswer: null);

    private static List<string> Texts(IEnumerable<MenuEntry> entries) => entries.Select(e => e.Text).ToList();

    private static MenuEntry Find(IEnumerable<MenuEntry> entries, string text) => entries.Single(e => e.Text == text);

    [Fact]
    public void APlayingSongIsNamedAndWhatAJumpWouldDoIsSaid()
    {
        var menu = TrayMenu.Build(Playing);
        Assert.Equal(["Sorma — Motive", "Jumps to 1:43: the part you replay most."], Texts(menu.Take(2)));
        Assert.True(menu[0].IsNote && menu[1].IsNote);
        Assert.True(menu[2].IsSeparator);
        Assert.Equal(
            ["Jump", "Pin", "This Song", "Pause Learning", "What hookjump Keeps", "", "Delete Everything…", "Quit hookjump"],
            Texts(menu.Skip(3)));
    }

    [Fact]
    public void WithNothingPlayingThereIsNoSongToActOn()
    {
        var menu = TrayMenu.Build(Playing with { HasTrack = false, Title = null, Artist = null, Decision = new JumpResult(Status.PlayerUnavailable) });
        Assert.Equal("Spotify is not playing anything.", menu[0].Text);
        Assert.DoesNotContain("This Song", Texts(menu));
        Assert.Contains("Jump", Texts(menu)); // it still answers, by saying why it cannot
    }

    [Fact]
    public void ThisSongOffersWhatFitsIt()
    {
        var plain = Find(TrayMenu.Build(Playing), "This Song").Children!;
        Assert.Equal(["Forget This Song", "Don't Track This Song"], Texts(plain));
        var pinned = Find(TrayMenu.Build(Playing with { Pinned = true, Ignored = true }), "This Song").Children!;
        Assert.Equal(["Remove the Pin", "Forget This Song", "Track This Song Again"], Texts(pinned));
        Assert.Equal(Command.RemovePin, pinned[0].Command);
    }

    [Fact]
    public void PausedLearningIsTicked()
    {
        Assert.False(Find(TrayMenu.Build(Playing), "Pause Learning").Checked);
        Assert.True(Find(TrayMenu.Build(Playing with { LearningPaused = true }), "Pause Learning").Checked);
    }

    [Fact]
    public void TheLastAnswerIsShownUnderTheSong()
    {
        var menu = TrayMenu.Build(Playing with { LastAnswer = "Pin removed." });
        Assert.Equal("Pin removed.", menu[2].Text);
        Assert.True(menu[2].IsNote);
    }

    [Fact]
    public void WhatIsKeptListsNamedSongsWithTheirFactsAndCountsTheUnnamed()
    {
        Record[] records =
        [
            new("Ou", "UZI", new Pin(83000, true), null, 0, 0, false),
            new(null, null, null, null, 2, 1, false),
            new(null, null, null, null, 1, 1, false),
        ];
        var kept = Find(TrayMenu.Build(Playing with { Records = records }), "What hookjump Keeps").Children!;
        Assert.Equal(["Ou — UZI", "2 songs not played since names were kept", "", TrayMenu.NothingLeaves], Texts(kept));
        Assert.Equal(["Pinned at 1:23, typed"], Texts(kept[0].Children!));
    }

    [Fact]
    public void NothingKeptSaysHowToStart()
    {
        var kept = Find(TrayMenu.Build(Playing), "What hookjump Keeps").Children!;
        Assert.Equal([.. Words.NothingKept, "", TrayMenu.NothingLeaves], Texts(kept));
    }

    [Fact]
    public void LongNamesAreShortened()
    {
        var menu = TrayMenu.Build(Playing with { Title = new string('a', 60) });
        Assert.Equal(48, menu[0].Text.Length);
        Assert.EndsWith("…", menu[0].Text);
    }
}

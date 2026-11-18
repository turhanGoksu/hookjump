using Xunit;

namespace Hookjump.App.Tests;

/// <summary>The hook's look, as src/hookjump/face.py decides it on the Mac.</summary>
public class MoodTest
{
    [Fact]
    public void AKnownSpotPutsBaitOnTheHook()
    {
        Assert.Equal(Mood.Known, Moods.Of(playingTrack: true, learningPaused: false, spotKnown: true));
    }

    [Fact]
    public void NothingKnownLeavesTheHookEmpty()
    {
        Assert.Equal(Mood.Unknown, Moods.Of(playingTrack: true, learningPaused: false, spotKnown: false));
    }

    [Fact]
    public void NothingPlayingOrLearningPausedRests()
    {
        Assert.Equal(Mood.Resting, Moods.Of(playingTrack: false, learningPaused: false, spotKnown: false));
        Assert.Equal(Mood.Resting, Moods.Of(playingTrack: true, learningPaused: true, spotKnown: true));
    }
}

namespace Hookjump.App;

/// <summary>The look of the hook in the notification area, as on the Mac (src/hookjump/face.py).</summary>
public enum Mood
{
    Known, // bait on the hook: this song's spot is known (pinned or learned)
    Unknown, // an empty hook: nothing known about this song yet
    Resting, // a faded hook: nothing playing, or learning paused
}

public static class Moods
{
    public static Mood Of(bool playingTrack, bool learningPaused, bool spotKnown)
    {
        if (!playingTrack || learningPaused) return Mood.Resting;
        return spotKnown ? Mood.Known : Mood.Unknown;
    }
}

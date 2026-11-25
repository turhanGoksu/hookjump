using System;
using System.Linq;
using System.Threading.Tasks;
using Hookjump.Player;
using Windows.Media.Control;

namespace Hookjump.App;

/// <summary>What the panel's song buttons ask Spotify, as the phone's do.</summary>
public enum SongControl
{
    Previous,
    PlayPause,
    Next,
}

/// <summary>Spotify, through Windows' media controls (SMTC).</summary>
/// <remarks>
/// <para>
/// Windows tells the song, the playback state and the position in three
/// separate reports, and gives no track id (see <see cref="TrackKey"/>). A
/// reading puts the latest of each together, dated by the position's own
/// moment. Around a change of song one of them may still be the old song's
/// for a moment; seek detection takes that as a restart, not as a seek.
/// </para>
/// <para>
/// Spotify was measured on Windows 11 (Microsoft Store app 1.309): it reports
/// a seek, a pause and a change of song at once, repeats its position every
/// 4.5 s, and moves when asked.
/// </para>
/// </remarks>
public sealed class SmtcTurntable : ITurntable, IDisposable
{
    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private readonly object _gate = new();
    private GlobalSystemMediaTransportControlsSession? _session;

    private SmtcTurntable(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _manager = manager;
        _manager.SessionsChanged += OnSessionsChanged;
        Attach();
    }

    public string Name => "Spotify";

    /// <summary>Raised, on a thread of Windows', whenever Spotify reports something.</summary>
    public event Action? Changed;

    public static async Task<SmtcTurntable> OpenAsync() =>
        new(await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

    /// <summary>
    /// Spotify's app ids: the Microsoft Store app's, measured, and the one the
    /// installer from spotify.com is said to use, not tried yet.
    /// </summary>
    public static bool IsSpotify(string appId) =>
        appId.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.Ordinal) ||
        appId.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase);

    public Reading? Read()
    {
        var session = Session();
        if (session is null) return null;
        var song = session.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();
        var state = session.GetPlaybackInfo();
        var time = session.GetTimelineProperties();
        return SmtcReading.From(
            song.Title, song.Artist, song.AlbumTitle, song.TrackNumber,
            state.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            time.Position.Ticks, time.StartTime.Ticks, time.EndTime.Ticks, time.LastUpdatedTime,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public bool Place(string trackId, long positionMs)
    {
        var session = Session();
        if (session is null) return false;
        var song = session.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();
        if (TrackKey.Of(song.Title, song.Artist, song.AlbumTitle, song.TrackNumber) != trackId) return false;
        return session.TryChangePlaybackPositionAsync(positionMs * TimeSpan.TicksPerMillisecond)
            .AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asks Spotify for the song before, to play or pause, or for the next
    /// song, as the phone's song buttons do. It does not wait: Spotify's own
    /// report of the change is the answer.
    /// </summary>
    public void Control(SongControl control)
    {
        if (Session() is not { } session) return;
        _ = control switch
        {
            SongControl.Previous => session.TrySkipPreviousAsync(),
            SongControl.PlayPause => session.TryTogglePlayPauseAsync(),
            _ => session.TrySkipNextAsync(),
        };
    }

    public void Dispose()
    {
        _manager.SessionsChanged -= OnSessionsChanged;
        lock (_gate) Watch(null);
    }

    private GlobalSystemMediaTransportControlsSession? Session()
    {
        lock (_gate) return _session;
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        Attach();
        Changed?.Invoke();
    }

    private void Attach()
    {
        var spotify = _manager.GetSessions().FirstOrDefault(s => IsSpotify(s.SourceAppUserModelId));
        lock (_gate)
        {
            var same = spotify is null
                ? _session is null
                : _session?.SourceAppUserModelId == spotify.SourceAppUserModelId;
            if (!same) Watch(spotify);
        }
    }

    private void Watch(GlobalSystemMediaTransportControlsSession? next)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSong;
            _session.PlaybackInfoChanged -= OnState;
            _session.TimelinePropertiesChanged -= OnTime;
        }
        _session = next;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSong;
            _session.PlaybackInfoChanged += OnState;
            _session.TimelinePropertiesChanged += OnTime;
        }
    }

    private void OnSong(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Changed?.Invoke();

    private void OnState(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Changed?.Invoke();

    private void OnTime(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Changed?.Invoke();
}

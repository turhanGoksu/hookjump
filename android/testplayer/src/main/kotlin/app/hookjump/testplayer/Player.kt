package app.hookjump.testplayer

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.media.MediaMetadata
import android.media.session.MediaSession
import android.media.session.PlaybackState
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.SystemClock
import android.util.Log

/**
 * A stand-in for Spotify on emulators: a media session with the habits
 * hookjump has seen Spotify's session have, driven from adb. Nothing plays;
 * the position runs on the clock, as a player's would.
 *
 * ```
 * adb shell am start-foreground-service -n app.hookjump.testplayer/.Player -a play --ei song 0
 * adb shell am start-foreground-service -n app.hookjump.testplayer/.Player -a seek --el at 105000
 * ```
 *
 * Actions: `play` (extras: `song` index, `at` ms), `pause`, `seek` (`at`),
 * `hide` (leave the active sessions, as Spotify does after a long pause,
 * without ending the session) and `stop`. Everything it does, and every move
 * hookjump asks of it, is logged under [TAG], so a script can check where a
 * jump landed.
 */
class Player : Service() {
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var session: MediaSession

    private var song = NONE
    private var positionMs = 0L // where the song was at [updatedAt]
    private var updatedAt = 0L // on the since-boot clock, like Spotify's reports
    private var playing = false

    private val songEnds = Runnable { change(next(1), 0) }

    override fun onCreate() {
        super.onCreate()
        session = MediaSession(this, TAG).apply {
            setCallback(
                object : MediaSession.Callback() {
                    override fun onSeekTo(pos: Long) {
                        log("asked: seekTo $pos")
                        seek(pos)
                    }

                    override fun onPlay() {
                        log("asked: play")
                        play(null, null)
                    }

                    override fun onPause() {
                        log("asked: pause")
                        pause()
                    }

                    override fun onSkipToNext() {
                        log("asked: next")
                        change(next(1), 0)
                    }

                    override fun onSkipToPrevious() {
                        log("asked: previous")
                        if (now() > RESTART_MS) seek(0) else change(next(-1), 0)
                    }
                },
                handler,
            )
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        foreground()
        val at = intent?.takeIf { it.hasExtra("at") }?.getLongExtra("at", 0)
        when (intent?.action) {
            "play" -> play(intent.takeIf { it.hasExtra("song") }?.getIntExtra("song", 0), at)
            "pause" -> pause()
            "seek" -> seek(at ?: 0)
            "hide" -> {
                session.isActive = false
                log("hidden from the active sessions")
            }
            "stop" -> {
                log("stopped")
                stopSelf()
            }
        }
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        handler.removeCallbacksAndMessages(null)
        session.release()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun play(index: Int?, at: Long?) {
        session.isActive = true
        when {
            song == NONE -> {
                // Spotify's first report is a placeholder: the song, paused at
                // 0:00, before it says where it really is.
                song = index ?: 0
                session.setMetadata(SONGS[song].metadata())
                set(0, playing = false)
                report()
                handler.postDelayed({ set(at ?: 0, playing = true); report() }, PLACEHOLDER_MS)
            }
            index != null && index != song -> change(index, at ?: 0)
            else -> {
                set(at ?: now(), playing = true)
                report()
            }
        }
    }

    private fun pause() {
        set(now(), playing = false)
        report()
    }

    /** A seek, by the listener or by hookjump: buffering first, then on. */
    private fun seek(at: Long) {
        if (song == NONE) return
        set(at.coerceIn(0, SONGS[song].durationMs), playing)
        report(PlaybackState.STATE_BUFFERING)
        handler.postDelayed({ report() }, BUFFERING_MS)
    }

    /**
     * Another song. Spotify reports the new song with the old one's position
     * for a moment, then the new position.
     */
    private fun change(index: Int, at: Long) {
        val stale = now()
        song = index
        session.setMetadata(SONGS[song].metadata())
        set(stale.coerceAtMost(SONGS[song].durationMs), playing = true)
        report()
        handler.postDelayed({ set(at, playing = true); report() }, STALE_MS)
    }

    private fun set(at: Long, playing: Boolean) {
        positionMs = at
        updatedAt = SystemClock.elapsedRealtime()
        this.playing = playing
        handler.removeCallbacks(songEnds)
        if (playing) handler.postDelayed(songEnds, SONGS[song].durationMs - at)
    }

    private fun now(): Long {
        if (song == NONE) return 0
        val ran = if (playing) SystemClock.elapsedRealtime() - updatedAt else 0
        return (positionMs + ran).coerceAtMost(SONGS[song].durationMs)
    }

    /** Spotify reports each change a few times over, all alike. */
    private fun report(state: Int = if (playing) PlaybackState.STATE_PLAYING else PlaybackState.STATE_PAUSED) {
        val now = now()
        positionMs = now
        updatedAt = SystemClock.elapsedRealtime()
        val report = PlaybackState.Builder()
            .setActions(ACTIONS)
            .setState(state, now, if (state == PlaybackState.STATE_PLAYING) 1f else 0f, updatedAt)
            .build()
        repeat(REPEATS) { session.setPlaybackState(report) }
        log("report: ${SONGS[song].id} state=$state at=$now")
    }

    private fun next(step: Int) = Math.floorMod((if (song == NONE) 0 else song) + step, SONGS.size)

    private fun foreground() {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel(CHANNEL, "Test player", NotificationManager.IMPORTANCE_LOW))
        val notice = Notification.Builder(this, CHANNEL)
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setContentTitle("hookjump test player")
            .setContentText("A stand-in for Spotify, driven from adb.")
            .build()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(ID, notice, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK)
        } else {
            startForeground(ID, notice)
        }
    }

    private fun log(text: String) {
        Log.i(TAG, text)
    }

    private class Song(val id: String, val title: String, val artist: String, val durationMs: Long) {
        fun metadata(): MediaMetadata = MediaMetadata.Builder()
            .putString(MediaMetadata.METADATA_KEY_MEDIA_ID, id)
            .putString(MediaMetadata.METADATA_KEY_TITLE, title)
            .putString(MediaMetadata.METADATA_KEY_ARTIST, artist)
            .putLong(MediaMetadata.METADATA_KEY_DURATION, durationMs)
            .build()
    }

    companion object {
        const val TAG = "hookjumpTestPlayer"
        private const val CHANNEL = "player"
        private const val ID = 1
        private const val NONE = -1
        private const val REPEATS = 3
        private const val PLACEHOLDER_MS = 400L
        private const val STALE_MS = 150L
        private const val BUFFERING_MS = 150L
        private const val RESTART_MS = 3000L
        private const val ACTIONS = PlaybackState.ACTION_PLAY or PlaybackState.ACTION_PAUSE or
            PlaybackState.ACTION_PLAY_PAUSE or PlaybackState.ACTION_SEEK_TO or
            PlaybackState.ACTION_SKIP_TO_NEXT or PlaybackState.ACTION_SKIP_TO_PREVIOUS

        // Ids shaped like Spotify's. The titles test what the screen must
        // draw: Turkish letters, and a name too long for one line.
        private val SONGS = listOf(
            Song("spotify:track:testplayer000000000001", "Çıpa Şarkısı", "Test Oynatıcı", 200_000),
            Song("spotify:track:testplayer000000000002", "Second Song", "The Stand-ins", 150_000),
            Song(
                "spotify:track:testplayer000000000003",
                "A Song Whose Name Is Far Too Long to Fit on One Line of Any Phone (Extended Mix)",
                "Somebody, Somebody Else & Another Somebody",
                320_000,
            ),
        )
    }
}

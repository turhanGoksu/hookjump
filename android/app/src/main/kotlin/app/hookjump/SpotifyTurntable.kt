package app.hookjump

import android.content.ComponentName
import android.content.Context
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.SystemClock
import app.hookjump.player.Reading
import app.hookjump.player.Turntable

/**
 * Spotify on Android, through its media session.
 *
 * The session's state carries the position and the moment it was measured,
 * so a reading is exact however late it is picked up. Seeing the session
 * needs notification access for [Listener]; without it there is no player.
 */
class SpotifyTurntable(private val context: Context) : Turntable {
    override val name = "Spotify"

    /** Spotify's session, or null if Spotify is not running or access is off. */
    fun session(): MediaController? = try {
        context.getSystemService(MediaSessionManager::class.java)
            .getActiveSessions(ComponentName(context, Listener::class.java))
            .firstOrNull { it.packageName == SPOTIFY }
    } catch (e: SecurityException) {
        null
    }

    override fun read(): Reading? = session()?.let(::reading)

    override fun place(trackId: String, positionMs: Long): Boolean {
        val session = session() ?: return false
        if (trackIdOf(session.metadata) != trackId) return false
        session.transportControls.seekTo(positionMs)
        return true
    }

    companion object {
        const val SPOTIFY = "com.spotify.music"
    }
}

fun reading(session: MediaController): Reading {
    val state = session.playbackState
    val metadata = session.metadata
    return Reading(
        atMs = state?.lastPositionUpdateTime?.takeIf { it > 0 }?.let(::unixTime) ?: System.currentTimeMillis(),
        trackId = trackIdOf(metadata),
        title = metadata?.getString(MediaMetadata.METADATA_KEY_TITLE),
        artist = metadata?.getString(MediaMetadata.METADATA_KEY_ARTIST),
        durationMs = metadata?.getLong(MediaMetadata.METADATA_KEY_DURATION)?.takeIf { it > 0 },
        playing = state?.state == PlaybackState.STATE_PLAYING,
        positionMs = state?.position?.takeIf { it >= 0 }, // -1: the player did not say
    )
}

private fun trackIdOf(metadata: MediaMetadata?): String? =
    metadata?.getString(MediaMetadata.METADATA_KEY_MEDIA_ID)

/** A moment on the phone's since-boot clock, as Unix time. */
private fun unixTime(elapsedRealtimeMs: Long): Long =
    System.currentTimeMillis() - (SystemClock.elapsedRealtime() - elapsedRealtimeMs)

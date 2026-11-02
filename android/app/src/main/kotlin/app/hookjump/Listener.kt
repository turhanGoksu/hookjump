package app.hookjump

import android.content.ComponentName
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Handler
import android.os.HandlerThread
import android.service.notification.NotificationListenerService
import androidx.sqlite.driver.AndroidSQLiteDriver
import app.hookjump.booth.Booth
import app.hookjump.storage.Store

/**
 * The part of hookjump that keeps running: the system keeps a notification
 * listener alive, and notification access is what shows Spotify's session.
 *
 * Spotify reports every change; a check every [CHECK_MS] catches a report
 * that went missing. All of it runs on one thread of its own.
 */
class Listener : NotificationListenerService() {
    private var thread: HandlerThread? = null
    private lateinit var handler: Handler
    private lateinit var turntable: SpotifyTurntable
    private var store: Store? = null
    private var booth: Booth? = null
    private var controller: MediaController? = null

    private val callback = object : MediaController.Callback() {
        override fun onPlaybackStateChanged(state: PlaybackState?) = heard()
        override fun onMetadataChanged(metadata: MediaMetadata?) = heard()
        override fun onSessionDestroyed() {
            attach()
            heard()
        }
    }

    private val sessions = MediaSessionManager.OnActiveSessionsChangedListener {
        attach()
        heard()
    }

    private val check = object : Runnable {
        override fun run() {
            heard()
            handler.postDelayed(this, CHECK_MS)
        }
    }

    override fun onListenerConnected() {
        connected = true
        if (thread != null) return
        val started = HandlerThread("hookjump").also { it.start() }
        thread = started
        handler = Handler(started.looper)
        turntable = SpotifyTurntable(this)
        handler.post {
            val opened = Store(AndroidSQLiteDriver(), getDatabasePath(DATABASE).path)
            store = opened
            booth = Booth(opened, turntable) { System.currentTimeMillis() }
            getSystemService(MediaSessionManager::class.java)
                .addOnActiveSessionsChangedListener(sessions, ComponentName(this, Listener::class.java), handler)
            attach()
            heard()
            handler.postDelayed(check, CHECK_MS)
        }
    }

    override fun onListenerDisconnected() {
        connected = false
        val running = thread ?: return
        thread = null
        handler.post {
            handler.removeCallbacks(check)
            getSystemService(MediaSessionManager::class.java).removeOnActiveSessionsChangedListener(sessions)
            controller?.unregisterCallback(callback)
            controller = null
            booth = null
            store?.close()
            store = null
            running.quitSafely()
        }
    }

    private fun attach() {
        val next = turntable.session()
        if (next?.sessionToken == controller?.sessionToken) return
        controller?.unregisterCallback(callback)
        controller = next
        next?.registerCallback(callback, handler)
    }

    private fun heard() {
        booth?.heard()
    }

    companion object {
        const val CHECK_MS = 30_000L

        /** Whether the system has bound the listener in this process. */
        @Volatile
        var connected = false
            private set
        const val DATABASE = "hookjump.db"
    }
}

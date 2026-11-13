package app.hookjump

import android.content.ComponentName
import android.content.Context
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Handler
import android.os.HandlerThread
import android.os.Looper
import android.service.notification.NotificationListenerService
import android.widget.Toast
import androidx.sqlite.driver.AndroidSQLiteDriver
import app.hookjump.booth.Booth
import app.hookjump.booth.Chart
import app.hookjump.booth.Record
import app.hookjump.booth.describeJump
import app.hookjump.booth.describePin
import app.hookjump.booth.describeTarget
import app.hookjump.booth.describeUnpin
import app.hookjump.player.Needle
import app.hookjump.player.Reading
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

    // The player's song and state as its reports said them, in order. If this
    // thread runs late (HyperOS holds apps in the background back), the
    // controller already has a later state, and reading that would merge the
    // moves in between: a skip and a rewind would count as one seek.
    private var saidMetadata: MediaMetadata? = null
    private var saidState: PlaybackState? = null

    private val callback = object : MediaController.Callback() {
        override fun onPlaybackStateChanged(state: PlaybackState?) {
            saidState = state
            heard(reading(saidMetadata, saidState), SAID)
        }

        override fun onMetadataChanged(metadata: MediaMetadata?) {
            saidMetadata = metadata
            heard(reading(saidMetadata, saidState), SAID)
        }

        override fun onSessionDestroyed() {
            turntable.attached = null // gone for good, not just paused
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
        running = this
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
        if (running === this) running = null
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
        turntable.attached = next
        saidMetadata = next?.metadata
        saidState = next?.playbackState
        next?.registerCallback(callback, handler)
    }

    /** Take in [latest]: a report as it was said, or else the player's state now. */
    private fun heard(latest: Reading? = turntable.read(), how: String = READ) {
        val current = booth ?: return
        trace(controller?.packageName) {
            "$how: ${latest?.trackId} ${if (latest?.playing == true) "playing" else "paused"} at ${latest?.positionMs}, said at ${latest?.atMs}"
        }
        current.heard(latest)
        val shown = current.reading?.trackId to describeTarget(current.decision())
        if (shown != notice) {
            notice = shown
            Notice.show(this, current.reading?.title ?: "hookjump", shown.second)
        }
    }

    private var notice: Pair<String?, String>? = null // what the notification shows now

    private fun refreshNotice() {
        notice = null
        heard()
    }

    private fun doJump() {
        val current = booth ?: return
        current.jump { jumped -> say(describeJump(jumped)) }
        // The needle waits for Spotify's word; these give it the chance to
        // try once more, or to report a slip, even if Spotify stays silent.
        handler.postDelayed({ heard() }, Needle.SETTLE_MS + 100)
        handler.postDelayed({ heard() }, 2 * Needle.SETTLE_MS + 200)
    }

    private fun doPin() {
        val current = booth ?: return
        say(describePin(current.pin().pin?.atMs))
        refreshNotice()
    }

    private fun doUnpin() {
        val current = booth ?: return
        say(describeUnpin(current.unpin()))
        refreshNotice()
    }

    private fun doAct(action: (Booth) -> String) {
        val current = booth ?: return
        say(action(current))
        refreshNotice()
    }

    private fun say(text: String) {
        main.post { Toast.makeText(applicationContext, text, Toast.LENGTH_SHORT).show() }
    }

    companion object {
        const val CHECK_MS = 30_000L
        private const val SAID = "said" // for the trace: a report, as it was said
        private const val READ = "read" // the player's state, read now

        private val main = Handler(Looper.getMainLooper())

        @Volatile
        private var running: Listener? = null

        /** Jump now; false if hookjump is not connected to Spotify yet. */
        fun jump(context: Context): Boolean = ask(context) { it.doJump() }

        /** Pin where the song is now; false if hookjump is not connected yet. */
        fun pin(context: Context): Boolean = ask(context) { it.doPin() }

        /** Run a change on the booth and say its answer; false if hookjump is not connected yet. */
        fun act(context: Context, action: (Booth) -> String): Boolean = ask(context) { it.doAct(action) }

        /** Remove the playing song's pin; false if hookjump is not connected yet. */
        fun unpin(context: Context): Boolean = ask(context) { it.doUnpin() }

        /** The playing song's chart, handed over on the main thread; null if none. */
        fun chart(onChart: (Chart?) -> Unit) {
            val listener = running
            if (listener == null) {
                onChart(null)
                return
            }
            listener.handler.post {
                val chart = listener.booth?.chart()
                main.post { onChart(chart) }
            }
        }

        /** Everything kept, song by song, handed over on the main thread. */
        fun records(onRecords: (List<Record>) -> Unit) {
            val listener = running ?: return onRecords(emptyList())
            listener.handler.post {
                val records = listener.booth?.records().orEmpty()
                main.post { onRecords(records) }
            }
        }

        /** Show the notification again, as after notifications were allowed. */
        fun refresh(context: Context): Boolean = ask(context, quiet = true) { it.refreshNotice() }

        private fun ask(context: Context, quiet: Boolean = false, what: (Listener) -> Unit): Boolean {
            val listener = running
            if (listener == null) {
                if (!quiet) {
                    main.post {
                        Toast.makeText(context.applicationContext, "hookjump is not connected: open hookjump once.", Toast.LENGTH_LONG).show()
                    }
                }
                return false
            }
            listener.handler.post { what(listener) }
            return true
        }

        /** Whether the system has bound the listener in this process. */
        @Volatile
        var connected = false
            private set
        const val DATABASE = "hookjump.db"
    }
}

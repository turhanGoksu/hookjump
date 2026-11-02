package app.hookjump

import android.content.ComponentName
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.mutableStateOf
import app.hookjump.booth.Chart
import app.hookjump.booth.FORGOT_EVERYTHING
import app.hookjump.booth.describeForget
import app.hookjump.booth.describeIgnore
import app.hookjump.booth.describePause
import app.hookjump.booth.describePin
import app.hookjump.booth.describeTrackAgain

/** The screen: the playing song as a fishing line, with Jump and Pin. */
class MainActivity : ComponentActivity() {
    private val access = mutableStateOf(false)
    private val chart = mutableStateOf<Chart?>(null)
    private val handler = Handler(Looper.getMainLooper())

    // The listener holds what was learned; ask it for the chart now and then.
    private val refresh = object : Runnable {
        override fun run() {
            Listener.chart { chart.value = it }
            handler.postDelayed(this, REFRESH_MS)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            FishingLineScreen(
                access = access.value,
                chart = chart.value,
                onOpenAccess = { startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)) },
                onJump = { Listener.jump(this) },
                onPin = { Listener.pin(this) },
                onUnpin = { Listener.unpin(this) },
                onSong = { SpotifyTurntable(this).control(it) },
                onRequest = ::handle,
            )
        }
    }

    override fun onResume() {
        super.onResume()
        access.value = hasAccess()
        if (access.value && !Listener.connected) {
            // After a force stop or cleared data Android does not bind the
            // listener again by itself, even with access on. Turning the
            // service off and on tells it the app changed, and it binds again.
            val listener = ComponentName(this, Listener::class.java)
            for (state in listOf(PackageManager.COMPONENT_ENABLED_STATE_DISABLED, PackageManager.COMPONENT_ENABLED_STATE_ENABLED)) {
                packageManager.setComponentEnabledSetting(listener, state, PackageManager.DONT_KILL_APP)
            }
        }
        if (Build.VERSION.SDK_INT >= 33 &&
            checkSelfPermission("android.permission.POST_NOTIFICATIONS") != PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf("android.permission.POST_NOTIFICATIONS"), 1)
        }
        handler.post(refresh)
    }

    override fun onPause() {
        super.onPause()
        handler.removeCallbacks(refresh)
    }

    @Deprecated("Still the way to hear back from requestPermissions in a ComponentActivity without AndroidX activity results")
    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<String>, grantResults: IntArray) {
        @Suppress("DEPRECATION")
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        Listener.refresh(this) // the notification can show now
    }

    private fun handle(request: Request) {
        Listener.act(this) { booth ->
            when (request) {
                is Request.PinAt -> describePin(booth.pinAt(request.ms, request.exact).pin?.atMs, request.exact)
                Request.ForgetSong -> describeForget(booth.forgetSong())
                Request.IgnoreSong -> describeIgnore(booth.ignoreSong())
                Request.TrackAgain -> describeTrackAgain(booth.trackSongAgain())
                is Request.PauseLearning -> describePause(request.paused).also { booth.setLearningPaused(request.paused) }
                Request.ForgetEverything -> FORGOT_EVERYTHING.also { booth.forgetEverything() }
            }
        }
        Listener.chart { chart.value = it } // after the change, on the same thread
    }

    private fun hasAccess(): Boolean {
        val enabled = Settings.Secure.getString(contentResolver, "enabled_notification_listeners") ?: return false
        return ComponentName(this, Listener::class.java).flattenToString() in enabled.split(':')
    }

    companion object {
        const val REFRESH_MS = 1000L
    }
}

package app.hookjump

import android.app.Activity
import android.content.ComponentName
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.WindowInsets
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView

/** For now a plain status screen: the real one is the fishing-line chart. */
class MainActivity : Activity() {
    private lateinit var status: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val pad = (24 * resources.displayMetrics.density).toInt()
        status = TextView(this).apply { textSize = 18f }
        val open = Button(this).apply {
            text = "Open notification access"
            isAllCaps = false
            setOnClickListener { startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)) }
        }
        setContentView(
            LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                addView(status)
                addView(open)
                // The screen runs edge to edge: leave room for the status and
                // navigation bars, whatever their size on this phone.
                setOnApplyWindowInsetsListener { view, insets ->
                    val (top, bottom) = systemBars(insets)
                    view.setPadding(pad, top + pad, pad, bottom + pad)
                    insets
                }
            },
        )
    }

    override fun onResume() {
        super.onResume()
        status.text = if (hasAccess()) {
            "hookjump is learning from Spotify."
        } else {
            "hookjump needs notification access to see what Spotify plays."
        }
    }

    private fun systemBars(insets: WindowInsets): Pair<Int, Int> =
        if (Build.VERSION.SDK_INT >= 30) {
            insets.getInsets(WindowInsets.Type.systemBars()).let { it.top to it.bottom }
        } else {
            @Suppress("DEPRECATION")
            insets.systemWindowInsetTop to insets.systemWindowInsetBottom
        }

    private fun hasAccess(): Boolean {
        val enabled = Settings.Secure.getString(contentResolver, "enabled_notification_listeners") ?: return false
        return ComponentName(this, Listener::class.java).flattenToString() in enabled.split(':')
    }
}

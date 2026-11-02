package app.hookjump

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/** The notification's Jump and Pin buttons. */
class Actions : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            JUMP -> Listener.jump(context)
            PIN -> Listener.pin(context)
        }
    }

    companion object {
        const val JUMP = "app.hookjump.JUMP"
        const val PIN = "app.hookjump.PIN"
    }
}

package app.hookjump

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.drawable.Icon

/**
 * The notification that stays: the playing song, where a jump would land, and
 * the Jump and Pin buttons, on the lock screen too. Silent: it never rings.
 */
object Notice {
    private const val CHANNEL = "hookjump"
    private const val ID = 1

    fun show(context: Context, title: String, text: String) {
        val manager = context.getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL, "Jump and pin", NotificationManager.IMPORTANCE_LOW).apply {
                description = "The Jump and Pin buttons, while hookjump runs."
                setShowBadge(false)
            },
        )
        val open = PendingIntent.getActivity(
            context, 0, Intent(context, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE,
        )
        manager.notify(
            ID,
            Notification.Builder(context, CHANNEL)
                .setSmallIcon(R.drawable.ic_hook)
                .setContentTitle(title)
                .setContentText(text)
                .setContentIntent(open)
                .setOngoing(true)
                .setShowWhen(false)
                .setVisibility(Notification.VISIBILITY_PUBLIC)
                .addAction(action(context, Actions.JUMP, "Jump"))
                .addAction(action(context, Actions.PIN, "Pin"))
                .build(),
        )
    }

    private fun action(context: Context, what: String, label: String): Notification.Action {
        val intent = Intent(context, Actions::class.java).setAction(what)
        val pending = PendingIntent.getBroadcast(context, 0, intent, PendingIntent.FLAG_IMMUTABLE)
        return Notification.Action.Builder(Icon.createWithResource(context, R.drawable.ic_hook), label, pending).build()
    }
}

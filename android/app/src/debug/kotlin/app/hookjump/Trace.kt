package app.hookjump

import android.util.Log

/**
 * A debug build writes what hookjump hears from the test player to the log,
 * so a test run on a lab phone can be read back. What Spotify says is never
 * written: on a phone in use, the listening stays the listener's.
 */
inline fun trace(player: String?, text: () -> String) {
    if (player == TEST_PLAYER) Log.i("hookjump", text())
}

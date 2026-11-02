package app.hookjump

import android.app.Activity
import android.content.Intent
import android.os.Bundle

/**
 * What opening hookjump does: a jump while Spotify plays, so the side key set
 * to open hookjump works like the Mac's ⌥F, and the screen otherwise. The
 * notification opens the screen even while music plays.
 */
class LaunchActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val playing = SpotifyTurntable(this).read()?.playing == true
        if (!(playing && Listener.jump(this))) {
            startActivity(Intent(this, MainActivity::class.java))
        }
        finish()
    }
}

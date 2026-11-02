package app.hookjump.player

/**
 * A player in memory that confirms moves later, as Spotify on Android does:
 * place() records the request, and report() is the player's next report.
 */
class FakeTurntable(var track: String? = "a", var positionMs: Long? = 30000) : Turntable {
    override val name = "Fake"
    var running = true
    var playing = true
    var atMs = 1_700_000_000_000
    var ignores = 0 // how many coming moves to accept but not do
    var switchTo: String? = null // the listener changes track first
    var places = 0

    override fun read(): Reading? =
        if (running) Reading(atMs, track, "Title", "Artist", 240000, playing, positionMs) else null

    override fun place(trackId: String, positionMs: Long): Boolean {
        places += 1
        switchTo?.let { track = it; this.positionMs = 0 }
        if (!running || track != trackId) return false
        if (ignores > 0) ignores -= 1 else this.positionMs = positionMs
        return true
    }

    /** The player's next report, [afterMs] later. */
    fun report(afterMs: Long = 150): Reading? {
        atMs += afterMs
        return read()
    }
}

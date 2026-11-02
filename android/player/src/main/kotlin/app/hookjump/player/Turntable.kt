package app.hookjump.player

// The turntable: what every player adapter must do, and nothing more.
//
// On Android the player reports its state when it changes, and each report
// says when its position was measured. So a reading carries its own moment,
// unlike on the Mac, where hookjump asks every second and the asking is the
// moment. Every turntable keeps three promises:
//
// 1. read() returns one report: the track, the state and the position belong
//    to its atMs.
// 2. place() moves the playhead only if the given track is still loaded.
// 3. A value the player does not report is null, never a made-up 0.

/** One report of the player. Null means the player did not say. */
data class Reading(
    val atMs: Long, // Unix time when the player measured this position
    val trackId: String?, // null: nothing loaded
    val title: String?,
    val artist: String?,
    val durationMs: Long?,
    val playing: Boolean,
    val positionMs: Long?,
)

interface Turntable {
    val name: String // for messages, such as "Spotify"

    /** The player's latest report, or null if the player is not running. */
    fun read(): Reading?

    /**
     * Ask the player to move the playhead if [trackId] is still loaded.
     * False if it is not; true only means the request was taken, as the
     * player reports where it went later.
     */
    fun place(trackId: String, positionMs: Long): Boolean
}

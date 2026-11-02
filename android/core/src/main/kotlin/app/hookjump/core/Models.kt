package app.hookjump.core

/**
 * Where a jump target came from, or why there is none (spec/ALGORITHM.md, section 2).
 *
 * The values are the strings used in spec/ JSON vectors; renaming one breaks
 * every vector and every other implementation.
 */
enum class Status(val value: String) {
    PINNED("pinned"), // a spot the listener chose by hand
    PERSONAL("personal"), // from the listener's own heatmap
    NO_DATA("no_data"), // nothing pinned or learned yet
    PLAYER_UNAVAILABLE("player_unavailable"), // Spotify closed or nothing loaded
    ;

    val hasTarget: Boolean get() = this == PINNED || this == PERSONAL
}

/** What the player reported at one moment, as a platform adapter read it. */
data class PlaybackSample(
    val atMs: Long, // a clock in ms; only differences matter (stored: Unix time)
    val trackId: String?, // null: no track loaded, or the player is unavailable
    val playing: Boolean,
    val positionMs: Long?, // null: the player did not report a position
    val ownSeek: Boolean = false, // hookjump moved the playhead since the last sample
)

/** What seek detection reports: playback carrying on, or the listener moving it. */
sealed interface ListeningEvent {
    val trackId: String
    val listen: Int // numbers one play-through of a track, counting from 1
    val fromMs: Long
    val toMs: Long
}

/** Uninterrupted playback of one stretch of a track during one listen. */
data class PlaySpan(
    override val trackId: String,
    override val listen: Int,
    override val fromMs: Long,
    override val toMs: Long,
) : ListeningEvent

/** The listener moved the playhead. Jumps made by hookjump are not seeks. */
data class SeekEvent(
    override val trackId: String,
    override val listen: Int,
    override val fromMs: Long, // where playback was at the sample before the seek
    override val toMs: Long, // where it was at the sample after; at most one interval late
) : ListeningEvent

/** A spot the listener chose by hand for one track. */
data class Pin(
    val atMs: Long, // the position when the button was pressed, or the typed time
    val typed: Boolean, // true: typed as m:ss, exact; false: a button, approximate
)

/** The part of a track the listener's own seeks point to. */
data class Favorite(
    val targetMs: Long,
    val points: Int, // rewinds and forward skips that landed in this region
    val listens: Int, // how many different listens those seeks came from
)

/**
 * A jump decision: a target, or an explicit reason why there is none.
 *
 * The two always agree: `pinned` and `personal` carry a target, every other
 * status carries none. hookjump never jumps without saying why.
 */
data class JumpResult(val status: Status, val targetMs: Long? = null) {
    init {
        require(!status.hasTarget || targetMs != null) {
            "status ${status.value} needs a target"
        }
        require(status.hasTarget || targetMs == null) {
            "status ${status.value} must not carry a target"
        }
        require(targetMs == null || targetMs >= 0) { "target must be >= 0, got $targetMs" }
    }
}

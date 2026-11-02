package app.hookjump.booth

import app.hookjump.core.Favorite
import app.hookjump.core.Pin
import app.hookjump.core.SeekEvent
import app.hookjump.player.Reading
import kotlin.math.roundToLong

/**
 * The fishing-line chart of the playing song, as plain data: the phone's
 * counterpart of the Mac's record sleeve (src/hookjump/sleeve.py).
 *
 * The song is a line going down from 0:00: a float where playback is, the hook
 * where the jump goes, ticks where the listener rewound to and skipped to.
 * Drawing it is the app's work; this only decides what goes where.
 */
data class Chart(
    val title: String,
    val artist: String?,
    val durationMs: Long,
    val positionMs: Long?, // where playback was at positionAtMs
    val positionAtMs: Long,
    val playing: Boolean,
    val hookMs: Long?, // the pin or the learned spot, before any lead-in
    val pinned: Boolean, // the hook is a pin, which the listener can remove
    val hookLabel: String, // what the hook is, or what to do when there is none
    val hookDetail: String,
    val rewinds: List<Long>, // where the listener rewound to
    val skips: List<Long>, // where they skipped forward to
) {
    /** Where playback is at [clockMs], moving on while it plays. */
    fun nowAt(clockMs: Long): Long? {
        val position = positionMs ?: return null
        if (!playing || clockMs <= positionAtMs) return position
        return minOf(durationMs, position + (clockMs - positionAtMs))
    }
}

/** Null when there is no track with a known length to draw. */
fun chartFor(reading: Reading?, pin: Pin?, favorite: Favorite?, seeks: List<SeekEvent>): Chart? {
    val duration = reading?.durationMs
    if (reading?.trackId == null || duration == null || duration <= 0) return null
    val (hookMs, label, detail) = when {
        pin != null -> Triple(
            pin.atMs,
            "Your pinned spot",
            if (pin.typed) "the jump lands right there" else "the jump lands 1.5 s before it",
        )
        favorite != null -> Triple(favorite.targetMs, "The part you replay most", "learned from ${favorite.listens} listens")
        else -> Triple(null, "Nothing learned yet", "press Pin at the part you love")
    }
    return Chart(
        title = reading.title ?: reading.artist ?: "Unknown track",
        artist = reading.artist.takeIf { reading.title != null },
        durationMs = duration,
        positionMs = reading.positionMs,
        positionAtMs = reading.atMs,
        playing = reading.playing,
        hookMs = hookMs,
        pinned = pin != null,
        hookLabel = label,
        hookDetail = detail,
        rewinds = seeks.filter { it.toMs < it.fromMs }.map { it.toMs },
        skips = seeks.filter { it.toMs >= it.fromMs }.map { it.toMs },
    )
}

/** Where [ms] sits along the line, from 0.0 at the top to 1.0 at the end. */
fun fraction(ms: Long, durationMs: Long): Double = (ms.toDouble() / durationMs).coerceIn(0.0, 1.0)

/** The moment under a point of the line, to the whole second. */
fun msAt(fraction: Double, durationMs: Long): Long {
    val ms = (fraction.coerceIn(0.0, 1.0) * durationMs / 1000).roundToLong() * 1000
    return minOf(ms, durationMs)
}

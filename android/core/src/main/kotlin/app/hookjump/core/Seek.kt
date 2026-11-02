package app.hookjump.core

// Turn polled player positions into listening events (spec/ALGORITHM.md, section 3).
//
// The adapter reads the player's position about once a second. Between two
// samples of the same track, the position should advance by about the time that
// passed; anything far from that is the listener moving the playhead.

const val TOLERANCE_MS = 2000L // polling jitter; smaller moves are not seeks
const val MAX_GAP_MS = 5000L // a longer gap between samples (sleep, a hang) says nothing
const val RESTART_MS = 3000L // going back to before this is starting the track over

/** Listening events, in order, from samples in time order. */
fun detect(samples: List<PlaybackSample>): List<ListeningEvent> {
    val events = mutableListOf<ListeningEvent>()
    var listen = 0
    var prev: Known? = null
    for (sample in samples) {
        val trackId = sample.trackId ?: continue
        val position = sample.positionMs ?: continue
        val now = Known(trackId, sample.atMs, sample.playing, position)
        val before = prev
        prev = now
        if (before == null || trackId != before.trackId) {
            listen += 1
            continue
        }

        val elapsed = now.atMs - before.atMs
        val moved = position - before.positionMs
        if (sample.ownSeek || elapsed <= 0 || elapsed > MAX_GAP_MS) continue

        // Playing during the whole interval: the position moves by `elapsed`.
        // Playing during part of it (paused or resumed in between): by anything
        // from 0 to `elapsed`. Paused throughout: not at all.
        val both = before.playing && now.playing
        val either = before.playing || now.playing
        val low = (if (both) elapsed else 0L) - TOLERANCE_MS
        val high = (if (either) elapsed else 0L) + TOLERANCE_MS
        when {
            moved in low..high -> if (moved > 0) addPlay(events, trackId, listen, before.positionMs, moved)
            moved < 0 && position < RESTART_MS -> listen += 1
            else -> events += SeekEvent(trackId, listen, before.positionMs, position)
        }
    }
    return events
}

/** A sample with both a track and a position: the only kind compared. */
private class Known(val trackId: String, val atMs: Long, val playing: Boolean, val positionMs: Long)

private fun addPlay(
    events: MutableList<ListeningEvent>,
    trackId: String,
    listen: Int,
    fromMs: Long,
    moved: Long,
) {
    // Extend the last span when this one carries straight on from it, as after
    // a pause; otherwise start a new span.
    val last = events.lastOrNull()
    if (last is PlaySpan && last.toMs == fromMs && last.trackId == trackId && last.listen == listen) {
        events[events.lastIndex] = last.copy(toMs = fromMs + moved)
        return
    }
    events += PlaySpan(trackId, listen, fromMs, fromMs + moved)
}

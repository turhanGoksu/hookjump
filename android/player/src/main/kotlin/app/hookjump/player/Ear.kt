package app.hookjump.player

import app.hookjump.core.PlaybackSample
import kotlin.math.abs

/**
 * Turns the player's reports into the samples seek detection reads.
 *
 * Seek detection (spec/ALGORITHM.md, section 3) expects samples about a second
 * apart. Android tells about changes instead, so for each report that says
 * something new the ear writes two samples: one just before it, where
 * playback would have been had nothing happened, and the report itself. A
 * seek then shows up between the two, at its exact moment.
 *
 * Two habits of Spotify on Android shape what counts as new:
 * - Before a track has played, its position may be a placeholder: on start
 *   Spotify reports 0:00 and then where the track was left. A track is
 *   trusted from its first report while playing.
 * - Around a start or a seek it reports many times a second, each matching
 *   playback. A report within [QUIET_MS] of where playback would be anyway
 *   adds nothing.
 */
class Ear {
    private var last: Reading? = null // the latest trusted report, written or not
    private var silent = false // the last thing heard was "no player"
    private var trusted: String? = null // the track that has played since it came
    private var ownMove = false

    /** hookjump moved the playhead: mark the next sample as its own. */
    fun heardOwnMove() {
        ownMove = true
    }

    /** Samples for a report, or none if it says nothing new. [nowMs] dates "no player". */
    fun hear(reading: Reading?, nowMs: Long): List<PlaybackSample> {
        if (reading == null) {
            if (silent) return emptyList()
            silent = true
            last = null
            trusted = null
            return listOf(PlaybackSample(nowMs, null, false, null, takeOwnMove()))
        }
        silent = false
        val trackId = reading.trackId
        if (trackId != null && trackId != trusted) {
            if (!reading.playing) return emptyList() // not played yet: maybe a placeholder
            trusted = trackId
        }
        val prev = last
        last = reading
        if (prev != null && saysNothingNew(prev, reading)) return emptyList()

        val samples = mutableListOf<PlaybackSample>()
        val justBefore = reading.atMs - 1
        if (prev != null && prev.trackId != null && prev.trackId == trackId &&
            prev.positionMs != null && justBefore > prev.atMs
        ) {
            samples += PlaybackSample(justBefore, prev.trackId, prev.playing, expected(prev, justBefore))
        }
        samples += PlaybackSample(reading.atMs, trackId, reading.playing, reading.positionMs, takeOwnMove())
        return samples
    }

    private fun takeOwnMove(): Boolean = ownMove.also { ownMove = false }

    companion object {
        const val QUIET_MS = 250L
    }
}

/** Where [prev] says playback is at [atMs], had nothing happened since. */
private fun expected(prev: Reading, atMs: Long): Long {
    val position = checkNotNull(prev.positionMs)
    return if (prev.playing) position + (atMs - prev.atMs) else position
}

private fun saysNothingNew(prev: Reading, reading: Reading): Boolean {
    if (prev == reading) return true
    if (prev.trackId != reading.trackId || prev.playing != reading.playing) return false
    if (reading.trackId == null) return true // still nothing loaded
    val position = reading.positionMs ?: return prev.positionMs == null
    if (prev.positionMs == null) return false
    return abs(position - expected(prev, reading.atMs)) <= Ear.QUIET_MS
}

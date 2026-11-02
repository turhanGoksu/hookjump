package app.hookjump.player

import app.hookjump.core.TOLERANCE_MS

/** Where a jump ended up. */
enum class Landing {
    LANDED, // the playhead is where it was sent
    TRACK_CHANGED, // another track is playing now; no move
    SLIPPED, // the player took the move but did not go there, twice
    NO_PLAYER, // the player is not running
}

/**
 * Jumps on a turntable and checks every jump.
 *
 * The player confirms a move later, in a report, so a jump waits: each report
 * goes to heard(), and tick() gives up on a silent player after [SETTLE_MS].
 * A move that does not land is tried once more, then reported.
 */
class Needle(private val turntable: Turntable, private val ear: Ear) {
    private class Jump(val trackId: String, val targetMs: Long, val done: (Landing) -> Unit) {
        var tries = 0
        var placedAtMs = 0L
    }

    private var jump: Jump? = null

    /** Start a jump; [done] hears where it ended, now or after later reports. */
    fun drop(trackId: String, positionMs: Long, nowMs: Long, done: (Landing) -> Unit) {
        jump?.done?.invoke(Landing.SLIPPED) // a new press replaces a jump still waiting
        jump = Jump(trackId, positionMs, done)
        attempt(nowMs)
    }

    /** A new report from the player. */
    fun heard(reading: Reading?, nowMs: Long) {
        val current = jump ?: return
        when {
            reading == null -> finish(Landing.NO_PLAYER)
            reading.trackId != current.trackId -> finish(Landing.TRACK_CHANGED)
            reading.atMs < current.placedAtMs -> Unit // said before the move: not an answer
            near(reading.positionMs, current.targetMs) -> finish(Landing.LANDED)
            else -> retryOrSlip(nowMs)
        }
    }

    /** Call now and then; a move the player never confirms counts as slipped. */
    fun tick(nowMs: Long) {
        val current = jump ?: return
        if (nowMs - current.placedAtMs >= SETTLE_MS) retryOrSlip(nowMs)
    }

    private fun attempt(nowMs: Long) {
        val current = jump ?: return
        current.tries += 1
        current.placedAtMs = nowMs
        if (turntable.place(current.trackId, current.targetMs)) {
            ear.heardOwnMove() // even a slipped move may have moved the playhead
            return
        }
        val reading = turntable.read()
        when {
            reading == null -> finish(Landing.NO_PLAYER)
            reading.trackId != current.trackId -> finish(Landing.TRACK_CHANGED)
            else -> retryOrSlip(nowMs)
        }
    }

    private fun retryOrSlip(nowMs: Long) {
        val current = jump ?: return
        if (current.tries <= RETRIES) attempt(nowMs) else finish(Landing.SLIPPED)
    }

    private fun finish(landing: Landing) {
        val current = jump ?: return
        jump = null
        current.done(landing)
    }

    companion object {
        const val RETRIES = 1 // one more try before reporting a slip
        const val SETTLE_MS = 2000L // how long a player may take to confirm a move
    }
}

private fun near(positionMs: Long?, targetMs: Long): Boolean =
    positionMs != null && kotlin.math.abs(positionMs - targetMs) <= TOLERANCE_MS

package app.hookjump.booth

import app.hookjump.core.JumpResult
import app.hookjump.core.Status
import app.hookjump.player.Landing

// What hookjump says, in words: the same sentences as src/hookjump/words.py.

private val REASONS = mapOf(
    Status.PINNED to "your pinned spot",
    Status.PERSONAL to "the part you replay most",
    Status.NO_DATA to "nothing pinned or learned for this song yet",
    Status.PLAYER_UNAVAILABLE to "Spotify is not playing anything",
)
private val LANDINGS = mapOf(
    Landing.SLIPPED to "Spotify did not move there",
    Landing.TRACK_CHANGED to "the song changed meanwhile",
    Landing.NO_PLAYER to "Spotify closed meanwhile",
)

/** 1:43 for 103500; seconds are cut, not rounded, like players show them. */
fun clock(ms: Long): String = "%d:%02d".format(ms / 60_000, ms % 60_000 / 1000)

fun describeJump(jumped: Jumped): String {
    val reason = REASONS.getValue(jumped.result.status)
    val target = jumped.result.targetMs ?: return "No jump: $reason."
    val landing = jumped.landing
    if (landing != null && landing != Landing.LANDED) {
        return "Tried ${clock(target)} ($reason), but ${LANDINGS.getValue(landing)}."
    }
    return "Jumped to ${clock(target)}: $reason."
}

/** What a jump would do now, before any move: for the notification. */
fun describeTarget(result: JumpResult): String {
    val reason = REASONS.getValue(result.status)
    val target = result.targetMs ?: return if (result.status == Status.PLAYER_UNAVAILABLE) "$reason." else "No jump: $reason."
    return "Jumps to ${clock(target)}: $reason."
}

fun describePin(pinnedMs: Long?, exact: Boolean = false): String = when {
    pinnedMs == null -> "Nothing pinned: Spotify is not playing anything."
    exact -> "Pinned ${clock(pinnedMs)}. The jump lands right there."
    else -> "Pinned ${clock(pinnedMs)}. The jump lands 1.5 s before it."
}

/** `1:23`, `1:23.5` or `83` to milliseconds; null if it is not a time. */
fun parseClock(text: String): Long? {
    val trimmed = text.trim()
    val colon = trimmed.lastIndexOf(':')
    val minutesText = if (colon >= 0) trimmed.substring(0, colon) else ""
    val whole = if (minutesText.isEmpty()) 0 else minutesText.toIntOrNull() ?: return null
    val part = trimmed.substring(colon + 1).toDoubleOrNull() ?: return null
    if (whole < 0 || part < 0 || part.isNaN() || (minutesText.isNotEmpty() && part >= 60)) return null
    return whole * 60_000L + Math.round(part * 1000)
}

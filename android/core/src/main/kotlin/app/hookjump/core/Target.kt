package app.hookjump.core

// Decide where a jump lands, and say why (spec/ALGORITHM.md, section 5).

// Targets are measured a little late: the button is pressed after the part
// starts, and a seek's arrival is read at the next poll. Landing this much
// earlier keeps the first word.
const val LEAD_MS = 1500L

/** The jump for the playing track: the pin, else the learned favorite. */
fun choose(trackLoaded: Boolean, pin: Pin?, favorite: Favorite?): JumpResult = when {
    !trackLoaded -> JumpResult(Status.PLAYER_UNAVAILABLE)
    pin != null -> JumpResult(Status.PINNED, if (pin.typed) pin.atMs else lead(pin.atMs))
    favorite != null -> JumpResult(Status.PERSONAL, lead(favorite.targetMs))
    else -> JumpResult(Status.NO_DATA)
}

private fun lead(targetMs: Long): Long = maxOf(0L, targetMs - LEAD_MS)

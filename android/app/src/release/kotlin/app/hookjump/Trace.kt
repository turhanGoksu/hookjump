package app.hookjump

/** A release build writes nothing to the log (the debug build's trace is for the test player). */
@Suppress("UNUSED_PARAMETER")
inline fun trace(player: String?, text: () -> String) = Unit

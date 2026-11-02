package app.hookjump.booth

import app.hookjump.core.Favorite
import app.hookjump.core.JumpResult
import app.hookjump.core.Pin
import app.hookjump.core.Status
import app.hookjump.core.choose
import app.hookjump.core.favorite
import app.hookjump.player.Ear
import app.hookjump.player.Landing
import app.hookjump.player.Needle
import app.hookjump.player.Reading
import app.hookjump.player.Turntable
import app.hookjump.storage.Store

// The booth: where the turntable, the ear, the needle and the records meet.
//
// Every way of asking (a notification button, the side key, the screen) ends
// here, so a jump or a pin goes through the same few lines however it was
// asked for. No UI and no player calls of its own: in tests it runs on a fake
// turntable.

const val PURGE_EVERY_MS = 60 * 60 * 1000L

/** A jump decision, and where the jump ended up: null when nothing was moved. */
data class Jumped(val result: JumpResult, val landing: Landing?)

/** The pin set, or null when nothing is playing, so nothing was pinned. */
data class Pinned(val pin: Pin?)

/** One song in "what hookjump keeps": its name, and what is kept about it. */
data class Record(
    val title: String?, // null when the song was not heard since it was kept
    val artist: String?,
    val pin: Pin?,
    val favorite: Favorite?,
    val seeks: Int,
    val listens: Int,
    val ignored: Boolean,
)

private const val NAMES_REMEMBERED = 50

class Booth(
    private val store: Store,
    private val turntable: Turntable,
    private val clock: () -> Long, // Unix time in milliseconds
) {
    private val ear = Ear()
    private val needle = Needle(turntable, ear)
    private var track: String? = null // the track of the listen going on
    private var purgedAtMs = 0L

    // Names heard this session: a song's listen is processed after it is over,
    // when the player may already report the next song. Memory only.
    private val names = object : LinkedHashMap<String, Pair<String?, String?>>() {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, Pair<String?, String?>>) = size > NAMES_REMEMBERED
    }

    /** The player's latest report, for the screen. */
    var reading: Reading? = null
        private set

    init {
        // Samples left from the last run belong to listens that are over.
        store.process()
    }

    /** The player reported a change, or the regular check came round: take it in. */
    fun heard() {
        val now = clock()
        val latest = turntable.read()
        reading = latest
        latest?.trackId?.let { names[it] = latest.title to latest.artist }
        for (sample in ear.hear(latest, now)) {
            val sampleId = store.addSample(sample)
            if (sample.trackId != track) {
                // A new track: the listen before it is over, so learn from it.
                // The new listen starts at this sample and waits for its own end.
                store.process(beforeId = sampleId)
                track?.let(::name) // it may be kept now, so it may be named
                track = sample.trackId
            }
        }
        latest?.trackId?.let(::name)
        needle.heard(latest, now)
        needle.tick(now)
        if (now - purgedAtMs >= PURGE_EVERY_MS) {
            store.purgeSamples(now)
            purgedAtMs = now
        }
    }

    /** Jump to the best part; [done] hears the decision and, later, the landing. */
    fun jump(done: (Jumped) -> Unit) {
        val latest = turntable.read()
        val trackId = latest?.trackId
        if (trackId == null) {
            done(Jumped(JumpResult(Status.PLAYER_UNAVAILABLE), null))
            return
        }
        val result = choose(trackLoaded = true, pin = store.pin(trackId), favorite = favorite(store.seeks(trackId)))
        val target = result.targetMs
        if (target == null) {
            done(Jumped(result, null))
            return
        }
        needle.drop(trackId, target, clock()) { landing -> done(Jumped(result, landing)) }
    }

    /** Pin where the playing track is now (the Pin button). */
    fun pin(): Pinned {
        val latest = turntable.read()
        val trackId = latest?.trackId
        val position = latest?.let { positionNow(it, clock()) }
        if (trackId == null || position == null) return Pinned(null)
        return pinned(trackId, Pin(position, typed = false))
    }

    /** Pin a chosen time on the song on screen: typed as 1:23 (exact), or picked on its line (approximate). */
    fun pinAt(positionMs: Long, exact: Boolean = true): Pinned {
        val trackId = reading?.trackId ?: return Pinned(null)
        return pinned(trackId, Pin(positionMs, typed = exact))
    }

    /** Remove the pin of the song on screen; false when it had none. */
    fun unpin(): Boolean {
        val trackId = reading?.trackId ?: return false
        if (store.pin(trackId) == null) return false
        store.clearPin(trackId)
        return true
    }

    /** What a jump would do now, without moving anything: for the notification. */
    fun decision(): JumpResult {
        val trackId = reading?.trackId ?: return JumpResult(Status.PLAYER_UNAVAILABLE)
        return choose(trackLoaded = true, pin = store.pin(trackId), favorite = favorite(store.seeks(trackId)))
    }

    /** The playing song as a chart for the screen, or null when there is none to draw. */
    fun chart(): Chart? {
        val trackId = reading?.trackId ?: return null
        val seeks = store.seeks(trackId)
        return chartFor(reading, store.pin(trackId), favorite(seeks), seeks, store.isIgnored(trackId), store.learningPaused())
    }

    // The logbook: what the listener can change about what is kept. Each acts
    // on the song on screen, the last one the player reported, as the Mac's
    // menu does, and returns false when there is none.

    /** Forget the playing song: its pin and everything learned from it. */
    fun forgetSong(): Boolean = withSong { store.forgetTrack(it) }

    /** Stop learning from the playing song and forget what was learned. Its pin stays. */
    fun ignoreSong(): Boolean = withSong {
        store.ignore(it)
        name(it) // listed as not tracked
    }

    /** Learn from the playing song again. */
    fun trackSongAgain(): Boolean = withSong { store.unignore(it) }

    fun setLearningPaused(paused: Boolean) = store.setLearningPaused(paused)

    /** Delete everything: pins, what was learned, ignored songs and settings. */
    fun forgetEverything() = store.forgetAll()

    private fun withSong(action: (String) -> Unit): Boolean {
        val trackId = reading?.trackId ?: return false
        action(trackId)
        return true
    }

    /** Whether the playing track has a spot to jump to. */
    fun spotKnown(): Boolean {
        val trackId = reading?.trackId ?: return false
        return store.pin(trackId) != null || favorite(store.seeks(trackId)) != null
    }

    private fun pinned(trackId: String, pin: Pin): Pinned {
        store.setPin(trackId, pin)
        name(trackId)
        return Pinned(pin)
    }

    /** Everything kept, song by song, for the listener to read. */
    fun records(): List<Record> = store.kept().map { kept ->
        val seeks = store.seeks(kept.trackId)
        Record(kept.title, kept.artist, kept.pin, favorite(seeks), seeks.size, seeks.map { it.listen }.toSet().size, kept.ignored)
    }

    private fun name(trackId: String) {
        // The store keeps it only if something else is kept about the song.
        val (title, artist) = names[trackId] ?: return
        store.name(trackId, title, artist)
    }
}

/** Where playback is at [nowMs]: a report may be seconds old while playing goes on. */
fun positionNow(reading: Reading, nowMs: Long): Long? {
    val position = reading.positionMs ?: return null
    if (!reading.playing || nowMs <= reading.atMs) return position
    val now = position + (nowMs - reading.atMs)
    return reading.durationMs?.let { minOf(now, it) } ?: now
}

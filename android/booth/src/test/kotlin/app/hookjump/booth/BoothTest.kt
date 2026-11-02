package app.hookjump.booth

import androidx.sqlite.driver.bundled.BundledSQLiteDriver
import app.hookjump.core.Pin
import app.hookjump.core.PlaybackSample
import app.hookjump.core.SeekEvent
import app.hookjump.core.Status
import app.hookjump.player.FakeTurntable
import app.hookjump.player.Landing
import app.hookjump.storage.Store
import java.io.File
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir

private const val HOUR_MS = 60 * 60 * 1000L
private const val DAY_MS = 24 * HOUR_MS
private val REWIND = listOf(130000L, 131000, 105000, 106000) // one rewind to 1:45

/** The booth, end to end on a fake turntable and a real store. */
class BoothTest {
    @TempDir
    lateinit var dir: File
    private val driver = BundledSQLiteDriver()
    private lateinit var path: String
    private lateinit var store: Store
    private val player = FakeTurntable("a", 30000)
    private var now = 1_700_000_000_000L
    private lateinit var booth: Booth
    private var jumped: Jumped? = null

    @BeforeEach
    fun open() {
        path = File(dir, "hookjump.db").path
        store = Store(driver, path)
        booth = Booth(store, player) { now }
    }

    @AfterEach
    fun close() = store.close()

    /** The player reports [track] at each of these positions, one second apart. */
    private fun play(track: String?, positions: List<Long>) {
        player.track = track
        for (position in positions) report(position)
    }

    /** One report, as Spotify on Android sends when something changes. */
    private fun report(positionMs: Long, playing: Boolean = true) {
        player.positionMs = positionMs
        player.playing = playing
        player.atMs = now
        booth.heard()
        now += 1000
    }

    /** Spotify confirms a move a moment later. */
    private fun confirm() {
        player.atMs = now + 150
        booth.heard()
    }

    private fun jump() = booth.jump { jumped = it }

    private fun samples(track: String) = driver.open(path).use { db ->
        db.prepare("SELECT COUNT(*) FROM samples WHERE track_id = ?").use { it.bindText(1, track); it.step(); it.getLong(0) }
    }

    @Test
    fun `a rewind is learned when the track changes`() {
        play("a", REWIND)
        assertEquals(emptyList<SeekEvent>(), store.seeks("a")) // the listen is still going on
        play("b", listOf(5000))
        assertEquals(listOf(105000L), store.seeks("a").map { it.toMs })
    }

    @Test
    fun `a skip right after a track starts is not lost`() {
        play("a", listOf(10000))
        play("b", listOf(0, 60000, 61000))
        play("c", listOf(0))
        assertEquals(listOf(60000L), store.seeks("b").map { it.toMs })
    }

    @Test
    fun `sparse reports, as on Android, still learn a rewind at its moment`() {
        report(30000) // playing from 0:30, then nothing for 70 s
        now += 69000
        report(60000) // rewound to 1:00 from 1:40
        now += 20000
        play("b", listOf(0))
        assertEquals(listOf(SeekEvent("a", 1, 99999, 60000)), store.seeks("a"))
    }

    @Test
    fun `nothing known is no data and nothing moves`() {
        play("a", listOf(30000))
        jump()
        assertEquals(Jumped(app.hookjump.core.JumpResult(Status.NO_DATA), null), jumped)
        assertEquals(0, player.places)
        assertFalse(booth.spotKnown())
    }

    @Test
    fun `two listens teach the favorite, and the jump lands when Spotify confirms`() {
        repeat(2) {
            play("a", REWIND)
            play("b", listOf(5000, 6000))
        }
        play("a", listOf(30000))
        assertTrue(booth.spotKnown())
        jump()
        assertNull(jumped) // waiting for Spotify
        confirm()
        assertEquals(Status.PERSONAL, jumped!!.result.status)
        assertEquals(103500L, jumped!!.result.targetMs)
        assertEquals(Landing.LANDED, jumped!!.landing)
        assertEquals(103500L, player.positionMs)
    }

    @Test
    fun `a pin wins and its jump is never learned`() {
        play("a", listOf(50000))
        assertEquals(Pin(51000, typed = false), booth.pin().pin) // a second has passed since the report
        play("a", listOf(80000)) // a seek, but the pin will win
        jump()
        confirm()
        assertEquals(Pin(51000, false), store.pin("a"))
        assertEquals(49500L, player.positionMs) // 1.5 s before the pin
        assertEquals(Landing.LANDED, jumped!!.landing)
        play("a", listOf(49500 + 1000L))
        play("b", listOf(0))
        assertEquals(listOf(80000L), store.seeks("a").map { it.toMs }) // the listener's seek only
    }

    @Test
    fun `a pin counts the time since the last report only while playing`() {
        report(60000)
        now += 4000
        assertEquals(65000L, booth.pin().pin!!.atMs) // the report was 5 s ago
        report(70000, playing = false)
        now += 4000
        assertEquals(70000L, booth.pin().pin!!.atMs)
    }

    @Test
    fun `a typed pin is exact`() {
        play("a", listOf(30000))
        assertEquals(Pin(83000, typed = true), booth.pinAt(83000).pin)
        jump()
        confirm()
        assertEquals(83000L, player.positionMs)
    }

    @Test
    fun `a jump Spotify ignores twice is reported as slipped`() {
        play("a", listOf(30000))
        booth.pinAt(83000)
        player.ignores = 2
        jump()
        confirm()
        confirm()
        assertEquals(Landing.SLIPPED, jumped!!.landing)
        assertEquals(2, player.places)
    }

    @Test
    fun `with Spotify closed nothing happens and it says so`() {
        player.running = false
        booth.heard()
        jump()
        assertEquals(Status.PLAYER_UNAVAILABLE, jumped!!.result.status)
        assertNull(jumped!!.landing)
        assertNull(booth.pin().pin)
        assertNull(booth.pinAt(1000).pin)
        assertEquals(0, player.places)
    }

    @Test
    fun `leftovers of the last run are learned at start`() {
        listOf(130000L, 131000, 105000, 106000).forEachIndexed { i, position ->
            store.addSample(PlaybackSample(now + i * 1000, "a", true, position))
        }
        Booth(store, player) { now }
        assertEquals(listOf(105000L), store.seeks("a").map { it.toMs })
    }

    @Test
    fun `old samples are purged once an hour`() {
        val old = now - 8 * DAY_MS
        store.addSample(PlaybackSample(old, "x", true, 1000))
        store.process()
        booth.heard() // the first report purges
        assertEquals(0L, samples("x"))
        store.addSample(PlaybackSample(old, "y", true, 1000))
        store.process()
        now += HOUR_MS - 2000
        booth.heard()
        assertEquals(1L, samples("y")) // not within the hour
        now += 2000
        booth.heard()
        assertEquals(0L, samples("y"))
    }
}

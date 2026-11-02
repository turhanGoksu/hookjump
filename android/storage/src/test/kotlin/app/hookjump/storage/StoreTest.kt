package app.hookjump.storage

import androidx.sqlite.driver.bundled.BundledSQLiteDriver
import androidx.sqlite.execSQL
import app.hookjump.core.Pin
import app.hookjump.core.PlaybackSample
import app.hookjump.core.RECENT_LISTENS
import app.hookjump.core.Status
import app.hookjump.core.choose
import app.hookjump.core.favorite
import java.io.File
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.io.TempDir

private const val DAY_MS = 24L * 60 * 60 * 1000
private val REWIND = listOf(130000L, 131000, 105000, 106000) // one rewind, 131000 -> 105000

/** Feeds samples one second apart, as the adapter would poll. */
private class Clock(val store: Store, var at: Long = 1_700_000_000_000) {
    fun play(track: String, positions: List<Long>): List<Long?> = positions.map {
        store.addSample(PlaybackSample(at, track, true, it)).also { at += 1000 }
    }
}

/** Local storage: pins, learned seeks, the 7-day samples and forgetting (spec/STORAGE.md). */
class StoreTest {
    @TempDir
    lateinit var dir: File
    private val driver = BundledSQLiteDriver()
    private lateinit var path: String
    private lateinit var store: Store

    @BeforeEach
    fun open() {
        path = File(dir, "hookjump.db").path
        store = Store(driver, path)
    }

    @AfterEach
    fun close() = store.close()

    /** Looks into the file through a second connection, as any reader would. */
    private fun <T> look(sql: String, read: (androidx.sqlite.SQLiteStatement) -> T): List<T> =
        driver.open(path).use { db ->
            db.prepare(sql).use { s -> buildList { while (s.step()) add(read(s)) } }
        }

    private fun count(table: String) = look("SELECT COUNT(*) FROM $table") { it.getLong(0) }.single()

    @Test
    fun `pins round trip`() {
        assertNull(store.pin("a"))
        store.setPin("a", Pin(83000, typed = true))
        assertEquals(Pin(83000, true), store.pin("a"))
        store.setPin("a", Pin(106200, typed = false)) // a new pin replaces the old
        assertEquals(Pin(106200, false), store.pin("a"))
        store.clearPin("a")
        assertNull(store.pin("a"))
    }

    @Test
    fun `listen numbers count per track`() {
        val clock = Clock(store)
        clock.play("a", REWIND)
        clock.play("b", REWIND)
        clock.play("a", REWIND)
        assertEquals(3, store.process())
        assertEquals(listOf(1, 2), store.seeks("a").map { it.listen })
        assertEquals(listOf(1), store.seeks("b").map { it.listen })
        assertEquals(131000L to 105000L, store.seeks("a")[0].let { it.fromMs to it.toMs })
    }

    @Test
    fun `listen numbers continue across batches`() {
        val clock = Clock(store)
        clock.play("a", REWIND)
        store.process()
        clock.play("b", listOf(5000, 6000))
        clock.play("a", REWIND)
        store.process()
        assertEquals(listOf(1, 2), store.seeks("a").map { it.listen })
    }

    @Test
    fun `the listen still going on waits`() {
        val clock = Clock(store)
        clock.play("a", REWIND)
        val firstOfB = clock.play("b", REWIND.take(2))[0]
        assertEquals(1, store.process(beforeId = firstOfB)) // only track a's listen
        clock.play("b", REWIND.drop(2)) // the same listen of b goes on, then ends
        assertEquals(1, store.process())
        assertEquals(listOf(1), store.seeks("b").map { it.listen }) // one listen, not two
    }

    @Test
    fun `a seek right at the start of the waiting listen is kept`() {
        // Cutting the listen after its first sample would lose this seek.
        val clock = Clock(store)
        clock.play("a", REWIND)
        val firstOfB = clock.play("b", listOf(130000))[0]
        store.process(beforeId = firstOfB)
        clock.play("b", listOf(50000, 51000)) // skipped back just after b began
        store.process()
        assertEquals(listOf(130000L to 50000L), store.seeks("b").map { it.fromMs to it.toMs })
    }

    @Test
    fun `only the recent listens with seeks are kept`() {
        val clock = Clock(store)
        repeat(RECENT_LISTENS + 2) {
            clock.play("a", REWIND)
            clock.play("b", listOf(5000, 6000)) // a change of track ends each listen of a
        }
        store.process()
        assertEquals((3..RECENT_LISTENS + 2).toList(), store.seeks("a").map { it.listen })
    }

    @Test
    fun `paused learning writes nothing`() {
        store.setLearningPaused(true)
        assertEquals(List(4) { null }, Clock(store).play("a", REWIND))
        assertEquals(0L, count("samples"))
        store.setLearningPaused(false)
        assertFalse(null in Clock(store).play("a", REWIND))
    }

    @Test
    fun `an ignored track is forgotten and not learned but keeps its pin`() {
        val clock = Clock(store)
        clock.play("a", REWIND)
        store.process()
        store.setPin("a", Pin(50000, typed = true))
        store.ignore("a")
        assertTrue(store.isIgnored("a"))
        assertEquals(emptyList<Any>(), store.seeks("a"))
        assertEquals(Pin(50000, true), store.pin("a"))
        assertEquals(List(4) { null }, clock.play("a", REWIND))
        assertFalse(null in clock.play("b", REWIND)) // other tracks are still learned
        store.unignore("a")
        assertFalse(null in clock.play("a", REWIND))
    }

    @Test
    fun `forget track touches only that track`() {
        val clock = Clock(store)
        clock.play("a", REWIND)
        clock.play("b", REWIND)
        store.process()
        store.setPin("a", Pin(1000, false))
        store.setPin("b", Pin(2000, false))
        store.forgetTrack("a")
        assertNull(store.pin("a"))
        assertEquals(emptyList<Any>(), store.seeks("a"))
        assertEquals(Pin(2000, false), store.pin("b"))
        assertEquals(1, store.seeks("b").size)
        assertEquals(listOf("b"), look("SELECT DISTINCT track_id FROM samples") { it.getText(0) })
    }

    @Test
    fun `forget learned keeps pins, ignored tracks and settings`() {
        Clock(store).play("a", REWIND)
        store.process()
        store.setPin("a", Pin(1000, true))
        store.ignore("c")
        store.setLearningPaused(true)
        store.forgetLearned()
        assertEquals(0L to 0L, count("seeks") to count("samples"))
        assertEquals(Pin(1000, true), store.pin("a"))
        assertTrue(store.isIgnored("c") && store.learningPaused())
    }

    @Test
    fun `forget all leaves an empty working store`() {
        val clock = Clock(store)
        clock.play("a", REWIND)
        store.process()
        store.setPin("a", Pin(1000, true))
        store.ignore("c")
        store.setLearningPaused(true)
        store.forgetAll()
        for (table in listOf("pins", "seeks", "samples", "ignored", "settings")) {
            assertEquals(0L, count(table), table)
        }
        assertFalse(store.learningPaused())
        clock.play("a", REWIND)
        assertEquals(1, store.process()) // the processed mark started over with it
    }

    @Test
    fun `sample ids are never reused`() {
        // Reused ids would sit below the processed mark and be skipped for good.
        val clock = Clock(store)
        val old = clock.play("a", REWIND).filterNotNull()
        store.process()
        store.forgetLearned()
        val new = clock.play("a", REWIND).filterNotNull()
        assertTrue(new.min() > old.max())
        assertEquals(1, store.process())
    }

    @Test
    fun `purge drops only old processed samples`() {
        val now = 1_700_000_000_000L
        val old = Clock(store, now - (SAMPLE_DAYS + 1) * DAY_MS)
        old.play("a", REWIND)
        store.process()
        old.play("b", REWIND) // old but not processed yet: must survive
        Clock(store, now - DAY_MS).play("c", REWIND) // recent
        assertEquals(4, store.purgeSamples(now))
        assertEquals(listOf("b", "c"), look("SELECT DISTINCT track_id FROM samples ORDER BY 1") { it.getText(0) })
    }

    @Test
    fun `data survives reopening`() {
        val nested = File(dir, "sub/hookjump.db").path // the folder is created
        Store(driver, nested).use { it.setPin("a", Pin(1000, true)) }
        Store(driver, nested).use { assertEquals(Pin(1000, true), it.pin("a")) }
    }

    @Test
    fun `a newer schema is refused`() {
        store.close()
        driver.open(path).use { it.execSQL("PRAGMA user_version = 2") }
        val error = assertThrows<IllegalStateException> { Store(driver, path) }
        assertTrue("schema version 2" in error.message.orEmpty(), error.message)
        store = Store(driver, File(dir, "other.db").path) // for close()
    }

    @Test
    fun `deleted data is overwritten in the file`() {
        store.setPin("spotify:track:FORGETME0123456789", Pin(1000, true))
        store.forgetTrack("spotify:track:FORGETME0123456789")
        store.close()
        val bytes = File(path).readBytes().toString(Charsets.ISO_8859_1)
        assertFalse("FORGETME0123456789" in bytes, "a forgotten track id is still readable in the file")
        store = Store(driver, path)
    }

    @Test
    fun `the file matches the Mac's format`() {
        assertEquals(listOf(SCHEMA_VERSION), look("PRAGMA user_version") { it.getLong(0) })
        val tables = look("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name") { it.getText(0) }
        assertEquals(listOf("ignored", "pins", "samples", "seeks", "settings", "sqlite_sequence"), tables)
    }

    @Test
    fun `from polls to a jump`() {
        // Two listens rewinding to 1:45: the next jump lands 1.5 s before it.
        val clock = Clock(store)
        repeat(2) {
            clock.play("a", REWIND)
            clock.play("b", listOf(5000, 6000))
        }
        store.process()
        val result = choose(trackLoaded = true, pin = null, favorite = favorite(store.seeks("a")))
        assertEquals(Status.PERSONAL to 103500L, result.status to result.targetMs)
    }
}

package app.hookjump.storage

import androidx.sqlite.SQLiteConnection
import androidx.sqlite.SQLiteDriver
import androidx.sqlite.SQLiteStatement
import androidx.sqlite.execSQL
import app.hookjump.core.Pin
import app.hookjump.core.PlaybackSample
import app.hookjump.core.RECENT_LISTENS
import app.hookjump.core.SeekEvent
import app.hookjump.core.detect
import java.io.File

// Local SQLite storage for what hookjump learns (spec/STORAGE.md), the same
// file format as src/hookjump/adapters/storage.py on the Mac.
//
// Raw samples are kept for 7 days; the seeks found in them and the listener's
// pins are kept for good. The kept tables hold no dates or times.

const val SCHEMA_VERSION = 2L
const val SAMPLE_DAYS = 7
private const val DAY_MS = 24L * 60 * 60 * 1000

private val SCHEMA = listOf(
    """CREATE TABLE pins (
        track_id TEXT PRIMARY KEY,
        at_ms INTEGER NOT NULL CHECK (at_ms >= 0),
        typed INTEGER NOT NULL CHECK (typed IN (0, 1))
    )""",
    """CREATE TABLE seeks (
        track_id TEXT NOT NULL,
        listen INTEGER NOT NULL,
        from_ms INTEGER NOT NULL,
        to_ms INTEGER NOT NULL
    )""",
    "CREATE INDEX seeks_by_track ON seeks (track_id, listen)",
    """CREATE TABLE samples (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        at_ms INTEGER NOT NULL,
        track_id TEXT,
        playing INTEGER NOT NULL,
        position_ms INTEGER,
        own_seek INTEGER NOT NULL
    )""",
    "CREATE TABLE ignored (track_id TEXT PRIMARY KEY)",
    "CREATE TABLE settings (key TEXT PRIMARY KEY, value INTEGER NOT NULL)",
)

// Version 2 adds the names of the songs something is kept about.
private const val TRACKS = "CREATE TABLE tracks (track_id TEXT PRIMARY KEY, title TEXT, artist TEXT)"

// Every track something is kept about; a name alone keeps nothing.
private const val KEPT = "SELECT track_id FROM pins UNION SELECT track_id FROM seeks UNION SELECT track_id FROM ignored"

/** One song something is kept about, for the listener to see. */
data class Kept(
    val trackId: String,
    val title: String?, // null when the song was not heard since it was kept
    val artist: String?,
    val pin: Pin?,
    val ignored: Boolean,
)

/** One hookjump database at [path]. Call close(), or use it with `use`. */
class Store(driver: SQLiteDriver, path: String) : AutoCloseable {
    private val db: SQLiteConnection

    init {
        if (path != ":memory:") File(path).absoluteFile.parentFile?.mkdirs()
        db = driver.open(path)
        // Deleted rows are overwritten, not left readable in free pages.
        db.execSQL("PRAGMA secure_delete = ON")
        val version = one("PRAGMA user_version") { it.getLong(0) } ?: 0L
        if (version == 0L) {
            transaction {
                (SCHEMA + TRACKS).forEach(db::execSQL)
                db.execSQL("PRAGMA user_version = $SCHEMA_VERSION")
            }
        } else if (version == 1L) { // upgrade in place: only the names are new
            transaction {
                db.execSQL(TRACKS)
                db.execSQL("PRAGMA user_version = $SCHEMA_VERSION")
            }
        } else if (version != SCHEMA_VERSION) {
            db.close()
            error("$path has schema version $version; this hookjump reads version $SCHEMA_VERSION")
        }
    }

    override fun close() = db.close()

    // Samples and seeks ------------------------------------------------------

    /**
     * Record one poll; its `atMs` is Unix time in milliseconds.
     *
     * Nothing is written while learning is paused or for an ignored track.
     * Returns the sample's id, or null if it was not written.
     */
    fun addSample(sample: PlaybackSample): Long? {
        if (learningPaused() || (sample.trackId != null && isIgnored(sample.trackId!!))) return null
        return transaction {
            exec(
                "INSERT INTO samples (at_ms, track_id, playing, position_ms, own_seek)" +
                    " VALUES (?, ?, ?, ?, ?)",
                sample.atMs, sample.trackId, sample.playing, sample.positionMs, sample.ownSeek,
            )
            one("SELECT last_insert_rowid()") { it.getLong(0) }
        }
    }

    /**
     * Find the seeks in the samples not processed yet and keep them.
     *
     * Pass the id of the first sample of the listen still going on, so a
     * listen is never cut in two (it would count as two listens); pass null
     * when nothing is playing any more, such as at start-up. Returns the
     * number of seeks kept.
     */
    fun process(beforeId: Long? = null): Int {
        val done = setting("processed_sample_id")
        val rows = query(
            "SELECT id, at_ms, track_id, playing, position_ms, own_seek FROM samples" +
                " WHERE id > ? AND (? IS NULL OR id < ?) ORDER BY id",
            done, beforeId, beforeId,
        ) {
            it.getLong(0) to PlaybackSample(
                atMs = it.getLong(1),
                trackId = if (it.isNull(2)) null else it.getText(2),
                playing = it.getLong(3) != 0L,
                positionMs = if (it.isNull(4)) null else it.getLong(4),
                ownSeek = it.getLong(5) != 0L,
            )
        }
        if (rows.isEmpty()) return 0
        val seeks = detect(rows.map { it.second }).filterIsInstance<SeekEvent>()

        // detect() numbers listens within this batch. Kept listen numbers count
        // per track, only for listens with seeks, and only ever grow. Each seek
        // is inserted at once, so the next listen of the track counts on from it.
        val numbers = mutableMapOf<Pair<String, Int>, Long>()
        transaction {
            for (seek in seeks) {
                val number = numbers.getOrPut(seek.trackId to seek.listen) { lastListen(seek.trackId) + 1 }
                exec(
                    "INSERT INTO seeks (track_id, listen, from_ms, to_ms) VALUES (?, ?, ?, ?)",
                    seek.trackId, number, seek.fromMs, seek.toMs,
                )
            }
            seeks.map { it.trackId }.toSet().forEach(::keepRecent)
            setSetting("processed_sample_id", rows.last().first)
        }
        return seeks.size
    }

    fun seeks(trackId: String): List<SeekEvent> = query(
        "SELECT listen, from_ms, to_ms FROM seeks WHERE track_id = ? ORDER BY listen, rowid",
        trackId,
    ) { SeekEvent(trackId, it.getLong(0).toInt(), it.getLong(1), it.getLong(2)) }

    /** Delete processed samples older than [SAMPLE_DAYS]; returns how many. */
    fun purgeSamples(nowMs: Long): Int = transaction {
        exec(
            "DELETE FROM samples WHERE at_ms < ? AND id <= ?",
            nowMs - SAMPLE_DAYS * DAY_MS, setting("processed_sample_id"),
        )
        one("SELECT changes()") { it.getLong(0).toInt() } ?: 0
    }

    // Pins -------------------------------------------------------------------

    fun pin(trackId: String): Pin? =
        one("SELECT at_ms, typed FROM pins WHERE track_id = ?", trackId) { Pin(it.getLong(0), it.getLong(1) != 0L) }

    fun setPin(trackId: String, pin: Pin) = transaction {
        exec("INSERT OR REPLACE INTO pins (track_id, at_ms, typed) VALUES (?, ?, ?)", trackId, pin.atMs, pin.typed)
    }

    fun clearPin(trackId: String) = transaction {
        exec("DELETE FROM pins WHERE track_id = ?", trackId)
        dropUnkeptNames()
    }

    // Names (spec/STORAGE.md, rule 6) ----------------------------------------

    /**
     * Keep a song's name while something else is kept about it. True if it is
     * kept now; nothing is written for a song only listened to, or when the
     * name is already there.
     */
    fun name(trackId: String, title: String?, artist: String?): Boolean {
        if (title == null && artist == null) return false
        one("SELECT 1 FROM ($KEPT) WHERE track_id = ?", trackId) { true } ?: return false
        val stored = one("SELECT title, artist FROM tracks WHERE track_id = ?", trackId) { textOrNull(it, 0) to textOrNull(it, 1) }
        if (stored != (title to artist)) {
            transaction { exec("INSERT OR REPLACE INTO tracks (track_id, title, artist) VALUES (?, ?, ?)", trackId, title, artist) }
        }
        return true
    }

    /** Every song something is kept about, named ones first, by title. */
    fun kept(): List<Kept> = query(
        "SELECT k.track_id, t.title, t.artist, p.at_ms, p.typed, k.track_id IN (SELECT track_id FROM ignored)" +
            " FROM ($KEPT) AS k LEFT JOIN tracks AS t USING (track_id) LEFT JOIN pins AS p USING (track_id)" +
            " ORDER BY t.title IS NULL, t.title COLLATE NOCASE, k.track_id",
    ) {
        Kept(
            trackId = it.getText(0),
            title = textOrNull(it, 1),
            artist = textOrNull(it, 2),
            pin = if (it.isNull(3)) null else Pin(it.getLong(3), it.getLong(4) != 0L),
            ignored = it.getLong(5) != 0L,
        )
    }

    // Privacy ----------------------------------------------------------------

    fun learningPaused(): Boolean = setting("learning_paused") != 0L

    fun setLearningPaused(paused: Boolean) = transaction { setSetting("learning_paused", if (paused) 1 else 0) }

    fun isIgnored(trackId: String): Boolean = one("SELECT 1 FROM ignored WHERE track_id = ?", trackId) { true } ?: false

    /** Stop learning from a track and forget what was learned. Pins stay. */
    fun ignore(trackId: String) = transaction {
        exec("INSERT OR IGNORE INTO ignored (track_id) VALUES (?)", trackId)
        forgetLearned(trackId)
    }

    fun unignore(trackId: String) = transaction {
        exec("DELETE FROM ignored WHERE track_id = ?", trackId)
        dropUnkeptNames()
    }

    /** Forget one track: its pin and everything learned from it. */
    fun forgetTrack(trackId: String) = transaction {
        exec("DELETE FROM pins WHERE track_id = ?", trackId)
        forgetLearned(trackId)
        dropUnkeptNames()
    }

    /** Forget everything learned from listening; pins and settings stay. */
    fun forgetLearned() = transaction {
        exec("DELETE FROM seeks")
        exec("DELETE FROM samples")
        dropUnkeptNames()
    }

    /** Delete everything: pins, learned data, ignored tracks, settings. */
    fun forgetAll() {
        transaction {
            for (table in listOf("pins", "seeks", "samples", "ignored", "settings", "tracks")) exec("DELETE FROM $table")
        }
        db.execSQL("VACUUM") // give the space back to the device
    }

    // Internals --------------------------------------------------------------

    private fun lastListen(trackId: String): Long =
        one("SELECT MAX(listen) FROM seeks WHERE track_id = ?", trackId) { if (it.isNull(0)) 0L else it.getLong(0) } ?: 0L

    private fun keepRecent(trackId: String) {
        // The heatmap only reads the RECENT_LISTENS latest listens with seeks.
        exec(
            "DELETE FROM seeks WHERE track_id = ? AND listen NOT IN (" +
                " SELECT DISTINCT listen FROM seeks WHERE track_id = ? ORDER BY listen DESC LIMIT ?)",
            trackId, trackId, RECENT_LISTENS,
        )
    }

    private fun dropUnkeptNames() {
        // A name lives only as long as something else is kept about the song.
        exec("DELETE FROM tracks WHERE track_id NOT IN ($KEPT)")
    }

    private fun textOrNull(statement: SQLiteStatement, column: Int): String? =
        if (statement.isNull(column)) null else statement.getText(column)

    private fun forgetLearned(trackId: String) {
        exec("DELETE FROM seeks WHERE track_id = ?", trackId)
        exec("DELETE FROM samples WHERE track_id = ?", trackId)
    }

    private fun setting(key: String): Long =
        one("SELECT value FROM settings WHERE key = ?", key) { it.getLong(0) } ?: 0L

    private fun setSetting(key: String, value: Long) =
        exec("INSERT OR REPLACE INTO settings (key, value) VALUES (?, ?)", key, value)

    private fun <T> transaction(block: () -> T): T {
        db.execSQL("BEGIN IMMEDIATE TRANSACTION")
        val result = try {
            block()
        } catch (e: Throwable) {
            db.execSQL("ROLLBACK")
            throw e
        }
        db.execSQL("COMMIT")
        return result
    }

    private fun exec(sql: String, vararg args: Any?) {
        db.prepare(sql).use { statement ->
            bind(statement, args)
            statement.step()
        }
    }

    private fun <T> query(sql: String, vararg args: Any?, row: (SQLiteStatement) -> T): List<T> =
        db.prepare(sql).use { statement ->
            bind(statement, args)
            buildList { while (statement.step()) add(row(statement)) }
        }

    private fun <T> one(sql: String, vararg args: Any?, row: (SQLiteStatement) -> T): T? =
        query(sql, *args, row = row).firstOrNull()

    private fun bind(statement: SQLiteStatement, args: Array<out Any?>) {
        args.forEachIndexed { i, value ->
            val index = i + 1
            when (value) {
                null -> statement.bindNull(index)
                is Long -> statement.bindLong(index, value)
                is Int -> statement.bindLong(index, value.toLong())
                is Boolean -> statement.bindLong(index, if (value) 1 else 0)
                is String -> statement.bindText(index, value)
                else -> error("cannot store a ${value::class.simpleName}")
            }
        }
    }
}

package app.hookjump.core

import java.io.File
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/**
 * The core stays portable: it imports only Kotlin itself.
 *
 * No java.io, java.time, android or a player: the core receives data as
 * arguments and returns data, like src/hookjump/core in Python.
 */
class PurityTest {
    private val allowed = listOf("kotlin.", "app.hookjump.core.")

    private fun imports(source: String): List<String> =
        source.lines().map { it.trim() }.filter { it.startsWith("import ") }
            .map { it.removePrefix("import ").substringBefore(" as ").trim() }

    private fun bad(source: String) = imports(source).filterNot { name -> allowed.any { name.startsWith(it) } }

    @Test
    fun `core imports only Kotlin`() {
        val dir = File(System.getProperty("core.sources") ?: error("core.sources is not set"))
        val files = dir.walk().filter { it.extension == "kt" }.toList()
        assertTrue(files.isNotEmpty(), "no core sources found in $dir")
        val found = files.flatMap { file -> bad(file.readText()).map { "${file.name}: $it" } }
        assertEquals(emptyList<String>(), found)
    }

    @Test
    fun `the check catches platform imports`() {
        // Guard the guard: a check that never fails proves nothing.
        val sneaky = "import java.io.File\nimport android.media.session.MediaController\n" +
            "import kotlin.math.max\nimport app.hookjump.core.Pin\n"
        assertEquals(listOf("java.io.File", "android.media.session.MediaController"), bad(sneaky))
    }
}

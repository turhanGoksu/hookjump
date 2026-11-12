package app.hookjump

import android.app.Notification
import android.app.NotificationManager
import android.os.Build
import android.os.ParcelFileDescriptor
import android.os.SystemClock
import android.util.Log
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import app.hookjump.booth.Record
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.BeforeClass
import org.junit.Test
import org.junit.runner.RunWith

/**
 * The emulator checks (android/emulators/check.py) as a test that runs on
 * real phones in a device lab. The test player (android/testplayer) is
 * installed next to hookjump; the test grants notification access from the
 * shell, plays songs on the test player and checks what hookjump makes of
 * them. Screenshots go to /sdcard/Download/hookjump, for the lab to collect.
 *
 * It plays, learns and pins, so it needs a phone where hookjump keeps
 * nothing yet; anywhere else it skips. Run it only on emulators and lab
 * phones, never with the Gradle task that uninstalls the app afterwards on
 * a phone in use.
 */
@RunWith(AndroidJUnit4::class)
class DeviceCheckTest {
    private val context = InstrumentationRegistry.getInstrumentation().targetContext

    @Test
    fun learnsAFavoriteAndJumpsBeforeIt() {
        player("play", song = 0, at = 0)
        assertTrue("hookjump sees the player: ${notice()}", until(20_000) { "nothing pinned or learned" in notice() })

        // Two listens, each with a skip to 2:10 and a rewind to 1:45; another
        // song ends each listen.
        for (listen in 1..2) {
            if (listen == 2) player("play", song = 0, at = 0)
            pause(3_000)
            player("seek", at = 130_000)
            pause(2_000)
            player("seek", at = 105_000)
            pause(2_000)
            player("play", song = 1, at = 0)
            pause(2_000)
        }
        player("play", song = 0, at = 0)
        val favorite = "Jumps to 1:43: the part you replay most."
        assertTrue("two listens teach a favorite: ${notice()}", until(10_000) { notice() == favorite })

        val before = asked().size
        assertTrue("hookjump is connected", Listener.jump(context))
        assertTrue("the jump lands 1.5 s before the favorite: ${asked()}", until(10_000) { "seekTo 103500" in asked().drop(before) })

        pause(3_000) // the jump has settled: it must not count as a seek of the listener's
        val song = records().firstOrNull { it.title == FIRST_SONG }
        assertNotNull("the first song is kept", song)
        assertEquals("only the listener's seeks are learned", 4, song!!.seeks)
        assertEquals("in two listens", 2, song.listens)
        assertEquals("the favorite is the rewinds' spot", 105_000L, song.favorite?.targetMs)
    }

    @Test
    fun pinsAndJumpsAfterThePlayerLeavesTheActiveSessions() {
        player("play", song = 1, at = 0)
        pause(2_000)
        player("seek", at = 72_000)
        pause(1_000)
        assertTrue("hookjump is connected", Listener.pin(context))
        assertTrue("the Pin button pins: ${notice()}", until(10_000) { "your pinned spot" in notice() })

        player("pause")
        pause(1_000)
        player("hide") // as Spotify does after a long pause
        pause(1_000)
        val before = asked().size
        assertTrue("hookjump is connected", Listener.jump(context))
        assertTrue(
            "a jump lands after the player leaves the active sessions: ${asked()}",
            until(10_000) { asked().drop(before).any { it.startsWith("seekTo") } },
        )
    }

    @Test
    fun drawsTheScreen() {
        player("play", song = 2, at = 60_000) // the song with the longest name
        pause(2_000)
        player("pause")
        shell("mkdir -p $SHOTS")
        val shots = listOf("screen" to "1.0", "screen-large-text" to "1.3").map { (name, scale) ->
            shell("settings put system font_scale $scale")
            // Opened as the launcher opens it: with the song paused, hookjump
            // shows its screen. A picture of anything else is no check.
            shell("am start -n app.hookjump/.LaunchActivity")
            assertTrue("opening hookjump shows its screen ($name): ${front()}", until(15_000) { SCREEN in front() })
            pause(3_000)
            shell("screencap -p $SHOTS/$name.png")
            name
        }
        shell("settings put system font_scale 1.0")
        for (name in shots) {
            val size = shell("stat -c %s $SHOTS/$name.png").trim().toLongOrNull() ?: 0
            assertTrue("a screenshot of the screen ($name)", size > 10_000)
        }
    }

    private fun notice(): String {
        val shown = context.getSystemService(NotificationManager::class.java).activeNotifications
        return shown.firstOrNull { it.id == 1 }?.notification?.extras?.getCharSequence(Notification.EXTRA_TEXT)?.toString().orEmpty()
    }

    companion object {
        private const val LISTENER = "app.hookjump/app.hookjump.Listener"
        private const val PLAYER = "app.hookjump.testplayer/.Player"
        private const val FIRST_SONG = "Çıpa Şarkısı"
        private const val SHOTS = "/sdcard/Download/hookjump"
        private const val SCREEN = "app.hookjump/.MainActivity"

        @JvmStatic
        @BeforeClass
        fun connect() {
            Log.i("hookjumpDeviceCheck", "${Build.MANUFACTURER} ${Build.MODEL}, Android ${Build.VERSION.RELEASE}")
            assertTrue("the test player is installed", "app.hookjump.testplayer" in shell("pm list packages app.hookjump.testplayer"))
            shell("cmd notification allow_listener $LISTENER")
            assertTrue("notification access binds the listener", until(30_000) { Listener.connected })
            assumeTrue("hookjump keeps something on this phone: not touching it", records().isEmpty())
        }

        private fun shell(command: String): String {
            val output = InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand(command)
            return ParcelFileDescriptor.AutoCloseInputStream(output).use { it.readBytes().decodeToString() }
        }

        private fun player(action: String, song: Int? = null, at: Long? = null) {
            val extras = listOfNotNull(song?.let { "--ei song $it" }, at?.let { "--el at $it" }).joinToString(" ")
            shell("am start-foreground-service -n $PLAYER -a $action $extras")
        }

        /** The activity in front, as the system reports it. */
        private fun front(): String = shell("dumpsys activity activities").lineSequence()
            .firstOrNull { "topResumedActivity" in it || "mResumedActivity" in it }?.trim().orEmpty()

        /** What hookjump asked of the test player, in order. */
        private fun asked(): List<String> =
            Regex("asked: (.*)").findAll(shell("logcat -d -s hookjumpTestPlayer:I")).map { it.groupValues[1].trim() }.toList()

        private fun records(): List<Record> {
            val done = CountDownLatch(1)
            var kept = emptyList<Record>()
            Listener.records {
                kept = it
                done.countDown()
            }
            done.await(10, TimeUnit.SECONDS)
            return kept
        }

        private fun until(timeoutMs: Long, condition: () -> Boolean): Boolean {
            val end = SystemClock.uptimeMillis() + timeoutMs
            while (SystemClock.uptimeMillis() < end) {
                if (condition()) return true
                pause(250)
            }
            return condition()
        }

        private fun pause(ms: Long) = SystemClock.sleep(ms)
    }
}

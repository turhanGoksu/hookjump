package app.hookjump

/** The test player (android/testplayer), the stand-in for Spotify on emulators and lab phones. */
const val TEST_PLAYER = "app.hookjump.testplayer"

/**
 * The players whose session hookjump reads. A debug build also reads the test
 * player.
 */
val PLAYERS = setOf(SpotifyTurntable.SPOTIFY, TEST_PLAYER)

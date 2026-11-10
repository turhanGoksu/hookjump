package app.hookjump

/**
 * The players whose session hookjump reads. A debug build also reads the test
 * player (android/testplayer), the stand-in for Spotify on emulators.
 */
val PLAYERS = setOf(SpotifyTurntable.SPOTIFY, "app.hookjump.testplayer")

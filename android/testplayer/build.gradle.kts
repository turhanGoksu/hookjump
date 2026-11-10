// A stand-in for Spotify on emulators, where Spotify is not installed. Only
// a debug build of hookjump reads it; it is never published.
plugins {
    alias(libs.plugins.android.application)
}

android {
    namespace = "app.hookjump.testplayer"
    compileSdk = 37
    buildToolsVersion = "36.1.0"

    defaultConfig {
        applicationId = "app.hookjump.testplayer"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "1"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

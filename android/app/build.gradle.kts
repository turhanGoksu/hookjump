plugins {
    alias(libs.plugins.android.application)
}

android {
    namespace = "app.hookjump"
    compileSdk = 36
    buildToolsVersion = "36.1.0"

    defaultConfig {
        applicationId = "app.hookjump"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    implementation(project(":booth"))
    // Android's own SQLite, behind the driver interface the storage module uses.
    implementation(libs.androidx.sqlite.framework)
}

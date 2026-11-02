import org.jetbrains.kotlin.gradle.dsl.JvmTarget

// Where the turntable, the ear, the needle and the records meet, as in
// src/hookjump/booth.py. Plain Kotlin: tested here on a fake turntable and a
// real store; the app only passes Spotify and Android's SQLite in.
plugins {
    alias(libs.plugins.kotlin.jvm)
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

kotlin {
    compilerOptions {
        jvmTarget = JvmTarget.JVM_17
    }
}

dependencies {
    api(project(":storage"))
    api(project(":player"))
    testImplementation(testFixtures(project(":player")))
    testImplementation(libs.androidx.sqlite.bundled)
    testImplementation(platform(libs.junit.bom))
    testImplementation(libs.junit.jupiter)
    testRuntimeOnly(libs.junit.platform.launcher)
}

tasks.test {
    useJUnitPlatform()
    // The bundled SQLite is a native library; newer Java asks for this.
    jvmArgs("--enable-native-access=ALL-UNNAMED")
}

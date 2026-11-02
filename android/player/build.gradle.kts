import org.jetbrains.kotlin.gradle.dsl.JvmTarget

// The turntable contract and the ear and needle built on it, as on the Mac
// (src/hookjump/adapters/turntable.py, src/hookjump/playback.py). Plain Kotlin:
// the app supplies the Spotify turntable, the tests a fake one.
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
    api(project(":core"))
    testImplementation(platform(libs.junit.bom))
    testImplementation(libs.junit.jupiter)
    testRuntimeOnly(libs.junit.platform.launcher)
}

tasks.test {
    useJUnitPlatform()
}

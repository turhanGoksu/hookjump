import org.jetbrains.kotlin.gradle.dsl.JvmTarget

// The SQLite file of spec/STORAGE.md, through androidx.sqlite's driver
// interface: on the phone the app passes Android's own SQLite, and the tests
// here pass the bundled one, so storage is tested on any machine.
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
    api(libs.androidx.sqlite)
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

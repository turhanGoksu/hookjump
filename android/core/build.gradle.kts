import org.jetbrains.kotlin.gradle.dsl.JvmTarget

// Plain Kotlin with no Android in it, like src/hookjump/core: it is tested on
// any machine against the same spec/vectors as the Python core.
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
    testImplementation(platform(libs.junit.bom))
    testImplementation(libs.junit.jupiter)
    testImplementation(libs.kotlinx.serialization.json)
    testRuntimeOnly(libs.junit.platform.launcher)
}

tasks.test {
    useJUnitPlatform()
    // The same JSON vectors the Python tests load.
    val vectors = rootProject.file("../spec/vectors")
    inputs.dir(vectors)
    systemProperty("spec.vectors", vectors.path)
    systemProperty("core.sources", file("src/main/kotlin").path)
}

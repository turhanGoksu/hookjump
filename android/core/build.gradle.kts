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

// Android-Gradle-Plugin und Compose-Plugin werden nur im App-Modul geladen, damit `core` ohne Android SDK
// (und ohne Zugriff auf Googles Maven-Repository) baut.
plugins {
    alias(libs.plugins.kotlin.jvm) apply false
    alias(libs.plugins.kotlin.serialization) apply false
}

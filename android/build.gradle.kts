// Das Android-Gradle-Plugin muss im selben Classloader wie das Kotlin-Plugin liegen. Es wird nur geladen, wenn ein
// Android SDK vorhanden ist – so baut `core` auch ohne SDK und ohne Zugriff auf Googles Maven-Repository.
buildscript {
    val hasAndroidSdk = System.getenv("ANDROID_HOME") != null || System.getenv("ANDROID_SDK_ROOT") != null || file("local.properties").exists()
    if (hasAndroidSdk) {
        repositories {
            google()
            mavenCentral()
        }
        dependencies {
            classpath("com.android.tools.build:gradle:8.13.0")
        }
    }
}

plugins {
    alias(libs.plugins.kotlin.jvm) apply false
    alias(libs.plugins.kotlin.serialization) apply false
    alias(libs.plugins.kotlin.compose) apply false
}

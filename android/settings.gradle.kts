pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "EstrichONE"

// core: reines Kotlin (API-Client, Anmeldung, Geräteschlüssel) – überall baubar und testbar.
include(":core")

// app: braucht das Android SDK (ANDROID_HOME bzw. local.properties).
if (System.getenv("ANDROID_HOME") != null || System.getenv("ANDROID_SDK_ROOT") != null || file("local.properties").exists()) {
    include(":app")
}

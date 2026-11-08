plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("org.jetbrains.kotlin.plugin.serialization")
}

// Version: im Release aus dem Tag (release.yml setzt -PappVersionName=1.0.x -PappVersionCode=…), sonst 1.0.x aus dem Repository.
val appVersionName = (findProperty("appVersionName") as String?)?.removePrefix("v") ?: "1.0.2"
val appVersionCode = (findProperty("appVersionCode") as String?)?.toIntOrNull() ?: run {
    val parts = appVersionName.split('.', '-').mapNotNull { it.toIntOrNull() } + listOf(0, 0, 0)
    parts[0] * 10_000 + parts[1] * 100 + parts[2]
}

// Signatur für Release-APKs: nur wenn die CI den Schlüssel aus einem Secret bereitstellt (nie im Repository).
val releaseKeystore = System.getenv("ESTRICHONE_KEYSTORE_FILE")?.let { file(it) }?.takeIf { it.exists() }

android {
    namespace = "de.estrichone.android"
    compileSdk = 36

    defaultConfig {
        applicationId = "de.estrichone.android"
        minSdk = 29
        targetSdk = 36
        versionCode = appVersionCode
        versionName = appVersionName
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    signingConfigs {
        if (releaseKeystore != null) {
            create("release") {
                storeFile = releaseKeystore
                storePassword = System.getenv("ESTRICHONE_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ESTRICHONE_KEY_ALIAS")
                keyPassword = System.getenv("ESTRICHONE_KEY_PASSWORD") ?: System.getenv("ESTRICHONE_KEYSTORE_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // Ohne Schlüssel bleibt das Release-APK unsigniert; die CI liefert dann das Debug-APK (docs/reengineering/08, Abschnitt 5).
            if (releaseKeystore != null) signingConfig = signingConfigs.getByName("release")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    packaging {
        resources.excludes += setOf("META-INF/*.kotlin_module", "META-INF/versions/**", "META-INF/LICENSE*", "META-INF/NOTICE*")
    }

    testOptions {
        unitTests {
            isIncludeAndroidResources = true
            all {
                // Screenshots (Roborazzi, JVM ohne Emulator): nur aufnehmen, nicht vergleichen.
                it.systemProperty("roborazzi.test.record", "true")
                it.systemProperty("robolectric.pixelCopyRenderMode", "hardware")
                it.maxHeapSize = "3g"
            }
        }
    }

    lint {
        // Die Version kommt aus dem Release-Tag, nicht aus Google Play.
        disable += setOf("OldTargetApi", "GradleDependency", "NewerVersionAvailable", "AndroidGradlePluginVersion")
    }
}

kotlin {
    compilerOptions { jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17) }
}

dependencies {
    implementation(project(":core"))
    implementation(libs.coroutines.android)
    implementation(libs.core.ktx)
    implementation(libs.activity.compose)
    implementation(libs.lifecycle.viewmodel.compose)
    implementation(libs.lifecycle.runtime.compose)
    implementation(platform(libs.compose.bom))
    implementation(libs.compose.ui)
    implementation(libs.compose.material3)
    implementation(libs.compose.adaptive.navigation)
    implementation(libs.compose.icons)
    implementation(libs.compose.ui.tooling.preview)
    implementation(libs.camera.core)
    implementation(libs.camera.camera2)
    implementation(libs.camera.lifecycle)
    implementation(libs.camera.view)
    implementation(libs.biometric)
    // BiometricPrompt bringt eine alte Fragment-Version mit; ActivityResult-APIs brauchen mindestens 1.3.
    implementation(libs.fragment)
    implementation(libs.work.runtime)
    implementation(libs.sqlcipher)
    implementation(libs.sqlite)

    testImplementation(libs.junit4)
    testImplementation(libs.robolectric)
    testImplementation(libs.androidx.test.core)
    testImplementation(libs.roborazzi)
    testImplementation(libs.roborazzi.compose)
    testImplementation(platform(libs.compose.bom))
    testImplementation(libs.compose.ui.test.junit4)
    debugImplementation(libs.compose.ui.test.manifest)
}

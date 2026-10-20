package de.estrichone.android.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// Markenfarben aus docs/reengineering/05 (keine dynamischen Farben: Status- und Markenfarben bleiben erkennbar).
private val Brand = Color(0xFF0B2D5B)
private val BrandLight = Color(0xFF9CC3FF)

private val Light = lightColorScheme(
    primary = Brand, onPrimary = Color.White, primaryContainer = Color(0xFFD7E5FA), onPrimaryContainer = Brand,
    secondary = Color(0xFF2E7D32), surface = Color(0xFFFBFCFE), background = Color(0xFFF5F7FA),
)

private val Dark = darkColorScheme(
    primary = BrandLight, onPrimary = Color(0xFF00213F), primaryContainer = Color(0xFF1C3A63), onPrimaryContainer = Color(0xFFD7E5FA),
    secondary = Color(0xFF81C784),
)

@Composable
fun EstrichTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (isSystemInDarkTheme()) Dark else Light, content = content)
}

val Warning = Color(0xFFC25700)
val Success = Color(0xFF2E7D32)

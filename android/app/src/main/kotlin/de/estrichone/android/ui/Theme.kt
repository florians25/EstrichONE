package de.estrichone.android.ui

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.material3.dynamicLightColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

// Farben aus den Gestaltungsgrundlagen (ios/EstrichONE/Design/Theme.swift, docs/reengineering/05): Marke Navy, Akzent Blau,
// Statusfarben mit hellen Flächen. Dynamische Farben (Material You) nur auf Wunsch – Status- und Markenfarben bleiben.

val Navy = Color(0xFF061536)
val NavyLight = Color(0xFF0B2E66)
val Ice = Color(0xFFCFE5FF)

/** Statusfarben, die Material 3 nicht mitbringt. */
@Immutable
data class Tones(
    val success: Color, val successSoft: Color,
    val warning: Color, val warningSoft: Color,
    val danger: Color, val dangerSoft: Color,
    val info: Color, val infoSoft: Color,
    val badge: Color,
    val canvas: Color, val card: Color, val fill: Color, val separator: Color,
    val textSecondary: Color, val textTertiary: Color,
    val dark: Boolean,
)

private val LightTones = Tones(
    success = Color(0xFF1C7A45), successSoft = Color(0xFFE5F4EB),
    warning = Color(0xFF9A5800), warningSoft = Color(0xFFFFF3DD),
    danger = Color(0xFFC0262D), dangerSoft = Color(0xFFFDECEC),
    info = Color(0xFF0062CC), infoSoft = Color(0xFFE7F0FC),
    badge = Color(0xFFD70015),
    canvas = Color(0xFFF2F3F7), card = Color(0xFFFFFFFF), fill = Color(0xFFF0F2F6), separator = Color(0xFFE0E3E9),
    textSecondary = Color(0xFF4A5361), textTertiary = Color(0xFF667080), dark = false,
)

private val DarkTones = Tones(
    success = Color(0xFF5FCF8C), successSoft = Color(0xFF13291C),
    warning = Color(0xFFF3B54A), warningSoft = Color(0xFF2E2410),
    danger = Color(0xFFFF7B7B), dangerSoft = Color(0xFF351617),
    info = Color(0xFF4DA3FF), infoSoft = Color(0xFF18283C),
    badge = Color(0xFFE0353F),
    canvas = Color(0xFF0E0F12), card = Color(0xFF1C1D21), fill = Color(0xFF2A2B30), separator = Color(0xFF34353B),
    textSecondary = Color(0xFFAEB5C0), textTertiary = Color(0xFF8C939E), dark = true,
)

private val LightScheme = lightColorScheme(
    primary = Color(0xFF0062CC), onPrimary = Color.White, primaryContainer = Color(0xFFE7F0FC), onPrimaryContainer = Color(0xFF00408A),
    secondary = NavyLight, onSecondary = Color.White, secondaryContainer = Color(0xFFDDE7F7), onSecondaryContainer = Navy,
    tertiary = Color(0xFF1C7A45), tertiaryContainer = Color(0xFFE5F4EB), onTertiaryContainer = Color(0xFF0E4A28),
    background = LightTones.canvas, onBackground = Color(0xFF14171C), surface = LightTones.canvas, onSurface = Color(0xFF14171C),
    surfaceVariant = LightTones.fill, onSurfaceVariant = LightTones.textSecondary,
    surfaceContainerLowest = Color.White, surfaceContainerLow = Color.White, surfaceContainer = Color(0xFFF7F8FB),
    surfaceContainerHigh = Color(0xFFEFF1F5), surfaceContainerHighest = Color(0xFFE6E9EF),
    outline = Color(0xFF8A93A1), outlineVariant = LightTones.separator,
    error = LightTones.danger, onError = Color.White, errorContainer = LightTones.dangerSoft, onErrorContainer = Color(0xFF7A1015),
)

private val DarkScheme = darkColorScheme(
    primary = Color(0xFF4DA3FF), onPrimary = Color(0xFF002A57), primaryContainer = Color(0xFF18283C), onPrimaryContainer = Color(0xFFCFE5FF),
    secondary = Color(0xFF9CC3FF), onSecondary = Color(0xFF00213F), secondaryContainer = Color(0xFF233247), onSecondaryContainer = Ice,
    tertiary = Color(0xFF5FCF8C), tertiaryContainer = Color(0xFF13291C), onTertiaryContainer = Color(0xFFBFF0D2),
    background = DarkTones.canvas, onBackground = Color(0xFFE9EBEF), surface = DarkTones.canvas, onSurface = Color(0xFFE9EBEF),
    surfaceVariant = DarkTones.fill, onSurfaceVariant = DarkTones.textSecondary,
    surfaceContainerLowest = Color(0xFF0A0B0D), surfaceContainerLow = DarkTones.card, surfaceContainer = Color(0xFF202126),
    surfaceContainerHigh = Color(0xFF26272C), surfaceContainerHighest = Color(0xFF303137),
    outline = Color(0xFF7A818C), outlineVariant = DarkTones.separator,
    error = DarkTones.danger, onError = Color(0xFF4A0003), errorContainer = DarkTones.dangerSoft, onErrorContainer = Color(0xFFFFD9D8),
)

val LocalTones = staticCompositionLocalOf { LightTones }

private val Type = Typography().let { base ->
    base.copy(
        displaySmall = base.displaySmall.copy(fontWeight = FontWeight.Bold),
        headlineLarge = base.headlineLarge.copy(fontWeight = FontWeight.Bold, fontSize = 32.sp, lineHeight = 38.sp),
        headlineMedium = base.headlineMedium.copy(fontWeight = FontWeight.Bold),
        headlineSmall = base.headlineSmall.copy(fontWeight = FontWeight.SemiBold),
        titleLarge = base.titleLarge.copy(fontWeight = FontWeight.SemiBold),
        titleMedium = base.titleMedium.copy(fontWeight = FontWeight.SemiBold, fontSize = 17.sp),
        titleSmall = base.titleSmall.copy(fontWeight = FontWeight.SemiBold),
        bodyLarge = base.bodyLarge.copy(fontSize = 17.sp, lineHeight = 24.sp),
        bodyMedium = base.bodyMedium.copy(fontSize = 15.sp, lineHeight = 21.sp),
        labelLarge = base.labelLarge.copy(fontWeight = FontWeight.SemiBold, fontSize = 15.sp),
    )
}

private val AppShapes = Shapes(
    extraSmall = RoundedCornerShape(8.dp), small = RoundedCornerShape(12.dp), medium = RoundedCornerShape(16.dp),
    large = RoundedCornerShape(22.dp), extraLarge = RoundedCornerShape(28.dp),
)

/** Erscheinungsbild: System, hell oder dunkel (Mehr › Darstellung). */
enum class Appearance(val title: String) { System("Wie System"), Light("Hell"), Dark("Dunkel") }

@Composable
fun EstrichTheme(appearance: Appearance = Appearance.System, dynamicColor: Boolean = false, content: @Composable () -> Unit) {
    val dark = when (appearance) {
        Appearance.System -> isSystemInDarkTheme()
        Appearance.Light -> false
        Appearance.Dark -> true
    }
    val tones = if (dark) DarkTones else LightTones
    val scheme: ColorScheme = if (dynamicColor && Build.VERSION.SDK_INT >= 31) {
        val context = LocalContext.current
        val dynamic = if (dark) dynamicDarkColorScheme(context) else dynamicLightColorScheme(context)
        // Nur neutrale Flächen aus dem Hintergrundbild; Akzent und Status bleiben EstrichONE (Entscheidung E-17).
        (if (dark) DarkScheme else LightScheme).copy(
            background = dynamic.surface, surface = dynamic.surface, surfaceContainer = dynamic.surfaceContainer,
            surfaceContainerHigh = dynamic.surfaceContainerHigh, surfaceContainerHighest = dynamic.surfaceContainerHighest,
            secondaryContainer = dynamic.secondaryContainer, onSecondaryContainer = dynamic.onSecondaryContainer,
        )
    } else if (dark) {
        DarkScheme
    } else {
        LightScheme
    }
    CompositionLocalProvider(LocalTones provides tones.copy(canvas = scheme.background)) {
        MaterialTheme(colorScheme = scheme, typography = Type, shapes = AppShapes, content = content)
    }
}

/** Kurzer Zugriff in Composables. */
val tones: Tones @Composable get() = LocalTones.current

val SectionTitle: TextStyle @Composable get() = MaterialTheme.typography.titleLarge.copy(fontWeight = FontWeight.Bold, fontSize = 21.sp)

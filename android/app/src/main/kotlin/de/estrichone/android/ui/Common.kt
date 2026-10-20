package de.estrichone.android.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import de.estrichone.core.ApiException
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale

private val German = Locale.GERMANY
val DayFormat: DateTimeFormatter = DateTimeFormatter.ofPattern("EEEE, dd.MM.", German)
val DateTimeFormat: DateTimeFormatter = DateTimeFormatter.ofPattern("dd.MM.yyyy, HH:mm", German)

fun day(value: String): String = runCatching { LocalDate.parse(value).format(DayFormat) }.getOrDefault(value)

fun dateTime(value: String): String = runCatching { OffsetDateTime.parse(value).atZoneSameInstant(java.time.ZoneId.systemDefault()).format(DateTimeFormat) }
    .getOrDefault(value)

fun address(street: String?, postalCode: String?, city: String?): String =
    listOfNotNull(street?.takeIf { it.isNotBlank() }, listOfNotNull(postalCode, city).joinToString(" ").takeIf { it.isNotBlank() })
        .joinToString(", ").ifEmpty { "–" }

/** Ergebnis eines Ladevorgangs: Daten oder verständlicher Fehler. */
sealed interface Load<out T> {
    data object Loading : Load<Nothing>
    data class Done<T>(val value: T) : Load<T>
    data class Failed(val message: String) : Load<Nothing>
}

/** Lädt bei Änderung von [key] neu; Fehler der API werden als Text angezeigt statt die App zu beenden. */
@Composable
fun <T> rememberLoad(key: Any?, fetch: suspend () -> T): MutableState<Load<T>> {
    val state = remember(key) { mutableStateOf<Load<T>>(Load.Loading) }
    LaunchedEffect(key, state.value is Load.Loading) {
        if (state.value is Load.Loading) state.value = attempt(fetch)
    }
    return state
}

suspend fun <T> attempt(fetch: suspend () -> T): Load<T> = try {
    Load.Done(fetch())
} catch (e: ApiException) {
    Load.Failed(e.message ?: "Fehler")
}

@Composable
fun Message(text: String, error: Boolean = false) {
    Box(Modifier.fillMaxWidth().padding(24.dp), contentAlignment = Alignment.Center) {
        Text(text, color = if (error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

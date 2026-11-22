package de.estrichone.android.support

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.PackageManager
import android.location.Location
import android.location.LocationManager
import android.os.Build
import android.os.CancellationSignal
import androidx.core.content.ContextCompat
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeoutOrNull
import kotlin.coroutines.resume

/**
 * Eigener Standort für „Meinen Standort teilen“ im Chat. Ohne Google-Dienste (nur LocationManager des Systems), nur nach
 * Freigabe durch die Person; es wird nichts im Hintergrund erfasst.
 */
object DeviceLocation {
    val permissions = arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)

    fun granted(context: Context): Boolean = permissions.any { ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED }

    /** Aktuelle Position (höchstens 15 s warten), sonst die letzte bekannte; `null` ohne Freigabe oder Ortung. */
    @SuppressLint("MissingPermission")
    suspend fun current(context: Context): Location? {
        if (!granted(context)) return null
        val manager = context.getSystemService(LocationManager::class.java) ?: return null
        val providers = listOf(LocationManager.GPS_PROVIDER, LocationManager.NETWORK_PROVIDER).filter { runCatching { manager.isProviderEnabled(it) }.getOrDefault(false) }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            for (provider in providers) {
                val fresh = withTimeoutOrNull(15_000) {
                    suspendCancellableCoroutine { continuation ->
                        val signal = CancellationSignal()
                        continuation.invokeOnCancellation { signal.cancel() }
                        manager.getCurrentLocation(provider, signal, ContextCompat.getMainExecutor(context)) { continuation.resume(it) }
                    }
                }
                if (fresh != null) return fresh
            }
        }
        return providers.mapNotNull { runCatching { manager.getLastKnownLocation(it) }.getOrNull() }.maxByOrNull { it.time }
    }
}

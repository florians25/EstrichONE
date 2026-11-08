package de.estrichone.android.support

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import java.io.File

/** Anrufen, Route, E-Mail, Dateien öffnen und teilen – über die Apps des Geräts. */
object Links {
    private fun start(context: Context, intent: Intent): Boolean = try {
        context.startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        true
    } catch (_: ActivityNotFoundException) {
        false
    }

    fun call(context: Context, phone: String) = start(context, Intent(Intent.ACTION_DIAL, Uri.parse("tel:${phone.filter { it.isDigit() || it == '+' }}")))

    fun mail(context: Context, address: String) = start(context, Intent(Intent.ACTION_SENDTO, Uri.parse("mailto:$address")))

    fun web(context: Context, url: String) = start(context, Intent(Intent.ACTION_VIEW, Uri.parse(if ("://" in url) url else "https://$url")))

    /** Navigation zur Baustelle (Koordinaten, sonst Adresse). */
    fun route(context: Context, address: String?, latitude: Double? = null, longitude: Double? = null): Boolean {
        val target = if (latitude != null && longitude != null) "$latitude,$longitude" else Uri.encode(address ?: return false)
        return start(context, Intent(Intent.ACTION_VIEW, Uri.parse("google.navigation:q=$target")))
            || start(context, Intent(Intent.ACTION_VIEW, Uri.parse("geo:0,0?q=$target")))
            || start(context, Intent(Intent.ACTION_VIEW, Uri.parse("https://www.google.com/maps/dir/?api=1&destination=$target")))
    }

    private fun cacheFile(context: Context, bytes: ByteArray, name: String): Uri {
        val dir = File(context.cacheDir, "shared").apply { mkdirs() }
        val file = File(dir, name.replace('/', '_'))
        file.writeBytes(bytes)
        return FileProvider.getUriForFile(context, "${context.packageName}.files", file)
    }

    /** Datei mit passender App ansehen (PDF-Anzeige, Bildbetrachter). */
    fun open(context: Context, bytes: ByteArray, name: String, mime: String): Boolean {
        val uri = cacheFile(context, bytes, name)
        return start(context, Intent(Intent.ACTION_VIEW).setDataAndType(uri, mime).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
    }

    fun share(context: Context, bytes: ByteArray, name: String, mime: String): Boolean {
        val uri = cacheFile(context, bytes, name)
        val send = Intent(Intent.ACTION_SEND).setType(mime).putExtra(Intent.EXTRA_STREAM, uri).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        return start(context, Intent.createChooser(send, "Teilen"))
    }

    /** Temporäre Ausgabedatei für die Kamera-App (Video). */
    fun captureUri(context: Context, name: String): Pair<File, Uri> {
        val dir = File(context.cacheDir, "capture").apply { mkdirs() }
        val file = File(dir, name)
        return file to FileProvider.getUriForFile(context, "${context.packageName}.files", file)
    }
}

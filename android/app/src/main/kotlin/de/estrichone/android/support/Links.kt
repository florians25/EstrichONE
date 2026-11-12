package de.estrichone.android.support

import android.content.ActivityNotFoundException
import android.content.ClipData
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

    /** Microsoft Teams: zuerst die Teams-App (`msteams:`), sonst Teams im Browser (Teams-App übernimmt per App-Link). */
    fun teams(context: Context, targets: de.estrichone.core.TeamsTargets): Boolean {
        val installed = teamsInstalled(context)
        // 1. Teams-App über msteams:, 2. https-Link ausdrücklich an die Teams-App, 3. Browser.
        if (installed && targets.app != null && start(context, Intent(Intent.ACTION_VIEW, Uri.parse(targets.app)))) return true
        val web = targets.web ?: return false
        if (installed && start(context, Intent(Intent.ACTION_VIEW, Uri.parse(web)).setPackage(de.estrichone.core.TeamsChatLinks.TEAMS_PACKAGE))) return true
        return start(context, Intent(Intent.ACTION_VIEW, Uri.parse(web)))
    }

    /** Ist die Teams-App installiert (Paketabfrage, im Manifest unter <queries> freigegeben)? */
    fun teamsInstalled(context: Context): Boolean =
        runCatching { context.packageManager.getPackageInfo(de.estrichone.core.TeamsChatLinks.TEAMS_PACKAGE, 0) }.isSuccess

    /** Google Play: Microsoft Teams installieren. */
    fun installTeams(context: Context): Boolean =
        start(context, Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=${de.estrichone.core.TeamsChatLinks.TEAMS_PACKAGE}")))
            || web(context, de.estrichone.core.TeamsChatLinks.PLAY_STORE_URL)

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

    /** Eine Datei teilen (WhatsApp, Mail …): echte Datei mit Endung und Typ, Leserecht über den FileProvider. */
    fun share(context: Context, bytes: ByteArray, name: String, mime: String): Boolean = shareAll(context, listOf(Triple(bytes, name, mime)))

    /** Mehrere Dateien teilen (ACTION_SEND_MULTIPLE); Typ gemeinsam, z. B. video/mp4, video/\*, image/\* oder \*\/\*. */
    fun shareAll(context: Context, files: List<Triple<ByteArray, String, String>>): Boolean {
        if (files.isEmpty()) return false
        clearShared(context)
        val uris = files.map { (bytes, name, _) -> cacheFile(context, bytes, name) }
        val mime = commonMime(files.map { it.third })
        val send = if (uris.size == 1) {
            Intent(Intent.ACTION_SEND).setType(mime).putExtra(Intent.EXTRA_STREAM, uris[0])
        } else {
            Intent(Intent.ACTION_SEND_MULTIPLE).setType(mime).putParcelableArrayListExtra(Intent.EXTRA_STREAM, ArrayList(uris))
        }
        // ClipData: das Leserecht gilt so auch für die App, die im Auswahlmenü gewählt wird.
        send.clipData = ClipData.newRawUri(files[0].second, uris[0]).apply { uris.drop(1).forEach { addItem(ClipData.Item(it)) } }
        send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        val chooser = Intent.createChooser(send, if (uris.size == 1) "Teilen" else "${uris.size} Dateien teilen")
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        return start(context, chooser)
    }

    /** Gemeinsamer Typ: gleich → dieser, gleiche Hauptart → „video/\*“, sonst „\*\/\*“. */
    fun commonMime(types: List<String>): String {
        val distinct = types.map { it.lowercase() }.distinct()
        if (distinct.size == 1) return distinct[0]
        val heads = distinct.map { it.substringBefore('/') }.distinct()
        return if (heads.size == 1) "${heads[0]}/*" else "*/*"
    }

    /** Frühere geteilte Kopien entfernen (Videos sind groß); die gerade geteilten bleiben bis zum nächsten Teilen. */
    private fun clearShared(context: Context) {
        File(context.cacheDir, "shared").listFiles()?.forEach { it.delete() }
    }

    /** Temporäre Ausgabedatei für die Kamera-App (Video). */
    fun captureUri(context: Context, name: String): Pair<File, Uri> {
        val dir = File(context.cacheDir, "capture").apply { mkdirs() }
        val file = File(dir, name)
        return file to FileProvider.getUriForFile(context, "${context.packageName}.files", file)
    }
}

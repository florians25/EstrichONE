package de.estrichone.android.work

import android.Manifest
import android.app.PendingIntent
import android.content.Context
import android.content.pm.PackageManager
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import de.estrichone.android.EstrichApp
import de.estrichone.android.NotificationTarget
import de.estrichone.android.R
import de.estrichone.core.SyncOutcome
import de.estrichone.core.TransferStatus
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withTimeoutOrNull
import de.estrichone.core.notifications
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.concurrent.TimeUnit

/**
 * Abgleich im Hintergrund (alle 15 Minuten mit Netz): Outbox und Fotos übertragen, neue Benachrichtigungen des Postfachs als
 * Mitteilung zeigen. Ersetzt Push, solange der Server kein FCM anbietet (docs/reengineering/08, Abschnitt 2).
 */
class SyncWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result {
        val model = (applicationContext as EstrichApp).model
        // Wurde die App beendet, startet WorkManager den Prozess neu: kurz warten, bis die Sitzung wiederhergestellt ist,
        // sonst blieben Fotos liegen, bis die App wieder geöffnet wird. (Poolgeräte brauchen die PIN: dort entfällt der Lauf.)
        val session = model.session.value ?: withTimeoutOrNull(25_000) { model.session.first { it != null } } ?: return Result.success()
        if (session.isDemo) return Result.success()
        val outcome = withContext(Dispatchers.Main) { session.synchronize() }
        // Ausstehende Änderungen oder Fotos und der Abgleich ist gescheitert: WorkManager wiederholt mit wachsendem Abstand.
        val retry = outcome != SyncOutcome.Completed && !session.store.summary().isSettled
        notifyFailures(session)
        val inbox = runCatching { session.api.notifications() }.getOrNull() ?: return if (retry) Result.retry() else Result.success()
        val fresh = inbox.items.filter { !it.isRead }.sortedBy { it.createdAt }
        val last = model.prefs.lastNotified
        val newer = if (last == null) fresh.takeLast(1) else fresh.filter { (it.createdAt ?: "") > last }
        if (newer.isNotEmpty() && ContextCompat.checkSelfPermission(applicationContext, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) {
            newer.takeLast(3).forEach { n ->
                // Tippen öffnet das Ziel (Bauakte, Chat, Urlaub …) statt nur die App; eigener requestCode je Mitteilung.
                val open = PendingIntent.getActivity(applicationContext, n.id.hashCode(), NotificationTarget.intent(applicationContext, n),
                    PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
                val notification = NotificationCompat.Builder(applicationContext, if (n.category == "chat") EstrichApp.CHANNEL_CHAT else EstrichApp.CHANNEL_INBOX)
                    .setSmallIcon(R.drawable.ic_launcher_monochrome).setContentTitle(n.title).setContentText(n.body)
                    .setStyle(NotificationCompat.BigTextStyle().bigText(n.body)).setContentIntent(open).setAutoCancel(true).build()
                NotificationManagerCompat.from(applicationContext).notify(n.id.hashCode(), notification)
            }
        }
        fresh.lastOrNull()?.createdAt?.let { model.prefs.lastNotified = it }
        return if (retry) Result.retry() else Result.success()
    }

    /**
     * Vom Server abgelehnte Uploads bleiben liegen, bis jemand „Erneut senden“ tippt: einmal je neuem Stand darauf hinweisen,
     * damit es nicht erst im Büro auffällt.
     */
    private fun notifyFailures(session: de.estrichone.android.Session) {
        val status = TransferStatus.of(session.store)
        val failed = status.failed.map { it.fileId }.sorted().joinToString(",")
        val backend = session.store.backend
        if (failed == (backend.meta("notified_failed_uploads") ?: "")) return
        backend.transaction { backend.setMeta("notified_failed_uploads", failed.ifEmpty { null }) }
        val text = status.failedText ?: return
        if (ContextCompat.checkSelfPermission(applicationContext, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) {
            val open = PendingIntent.getActivity(applicationContext, 7001,
                android.content.Intent(applicationContext, de.estrichone.android.MainActivity::class.java).setAction(de.estrichone.android.MainActivity.ACTION_SYNC),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            val notification = NotificationCompat.Builder(applicationContext, EstrichApp.CHANNEL_SYNC)
                .setSmallIcon(R.drawable.ic_launcher_monochrome).setContentTitle("Übertragung prüfen").setContentText("$text.")
                .setStyle(NotificationCompat.BigTextStyle().bigText("$text. Öffnen Sie die Synchronisation und tippen Sie auf „Erneut senden“."))
                .setContentIntent(open).setAutoCancel(true).build()
            NotificationManagerCompat.from(applicationContext).notify(7001, notification)
        }
    }

    companion object {
        /**
         * Beim Verlassen der App mit ausstehenden Änderungen oder Fotos: sofort einen Abgleich einplanen, sobald Netz da ist,
         * statt bis zu 15 Minuten auf den regelmäßigen Lauf zu warten. Scheitert er, wiederholt WorkManager mit Backoff.
         */
        fun scheduleSoon(context: Context) {
            val request = OneTimeWorkRequestBuilder<SyncWorker>()
                .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 30, TimeUnit.SECONDS)
                .build()
            WorkManager.getInstance(context).enqueueUniqueWork("sync-soon", ExistingWorkPolicy.REPLACE, request)
        }
    }
}

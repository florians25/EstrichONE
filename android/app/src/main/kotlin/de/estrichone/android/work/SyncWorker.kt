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
        val session = model.session.value ?: return Result.success()
        if (session.isDemo) return Result.success()
        val outcome = withContext(Dispatchers.Main) { session.synchronize() }
        // Ausstehende Änderungen oder Fotos und der Abgleich ist gescheitert: WorkManager wiederholt mit wachsendem Abstand.
        val retry = outcome != SyncOutcome.Completed && !session.store.summary().isSettled
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

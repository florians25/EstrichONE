package de.estrichone.android.work

import android.Manifest
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import de.estrichone.android.EstrichApp
import de.estrichone.android.MainActivity
import de.estrichone.android.R
import de.estrichone.core.notifications
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Abgleich im Hintergrund (alle 15 Minuten mit Netz): Outbox und Fotos übertragen, neue Benachrichtigungen des Postfachs als
 * Mitteilung zeigen. Ersetzt Push, solange der Server kein FCM anbietet (docs/reengineering/08, Abschnitt 2).
 */
class SyncWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result {
        val model = (applicationContext as EstrichApp).model
        val session = model.session.value ?: return Result.success()
        if (session.isDemo) return Result.success()
        withContext(Dispatchers.Main) { session.synchronize() }
        val inbox = runCatching { session.api.notifications() }.getOrNull() ?: return Result.success()
        val fresh = inbox.items.filter { !it.isRead }.sortedBy { it.createdAt }
        val last = model.prefs.lastNotified
        val newer = if (last == null) fresh.takeLast(1) else fresh.filter { (it.createdAt ?: "") > last }
        if (newer.isNotEmpty() && ContextCompat.checkSelfPermission(applicationContext, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) {
            val open = PendingIntent.getActivity(applicationContext, 0, Intent(applicationContext, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
            newer.takeLast(3).forEach { n ->
                val notification = NotificationCompat.Builder(applicationContext, if (n.category == "chat") EstrichApp.CHANNEL_CHAT else EstrichApp.CHANNEL_INBOX)
                    .setSmallIcon(R.drawable.ic_launcher_monochrome).setContentTitle(n.title).setContentText(n.body)
                    .setStyle(NotificationCompat.BigTextStyle().bigText(n.body)).setContentIntent(open).setAutoCancel(true).build()
                NotificationManagerCompat.from(applicationContext).notify(n.id.hashCode(), notification)
            }
        }
        fresh.lastOrNull()?.createdAt?.let { model.prefs.lastNotified = it }
        return Result.success()
    }
}

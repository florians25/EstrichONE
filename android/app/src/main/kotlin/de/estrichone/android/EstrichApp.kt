package de.estrichone.android

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import de.estrichone.android.work.SyncWorker
import java.util.concurrent.TimeUnit

/** Anwendung: hält das [AppModel], legt Mitteilungskanäle an und plant den Abgleich im Hintergrund. */
class EstrichApp : Application() {
    lateinit var model: AppModel
        private set

    override fun onCreate() {
        super.onCreate()
        model = AppModel(this)
        model.start()
        val manager = getSystemService(NotificationManager::class.java)
        listOf(
            Triple(CHANNEL_INBOX, "Benachrichtigungen", "Planfreigaben, Zuweisungen, Antworten der IT"),
            Triple(CHANNEL_CHAT, "Chat", "Neue Nachrichten"),
            Triple(CHANNEL_SYNC, "Synchronisation", "Fehler bei der Übertragung"),
        ).forEach { (id, name, description) ->
            manager.createNotificationChannel(NotificationChannel(id, name, NotificationManager.IMPORTANCE_DEFAULT).apply { this.description = description })
        }
        val request = PeriodicWorkRequestBuilder<SyncWorker>(15, TimeUnit.MINUTES)
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()).build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork("sync", ExistingPeriodicWorkPolicy.KEEP, request)
    }

    companion object {
        const val CHANNEL_INBOX = "inbox"
        const val CHANNEL_CHAT = "chat"
        const val CHANNEL_SYNC = "sync"
    }
}

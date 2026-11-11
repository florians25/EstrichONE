package de.estrichone.android

import android.content.Context
import android.content.Intent
import de.estrichone.core.InboxNotification
import de.estrichone.core.NotificationRoute

/** Ziel einer Mitteilung als Intent-Angaben: Link, Objektart und -ID wie im Postfach (NotificationRoute im Kern). */
object NotificationTarget {
    private const val LINK = "de.estrichone.notification.link"
    private const val TYPE = "de.estrichone.notification.objectType"
    private const val OBJECT = "de.estrichone.notification.objectId"
    private const val ID = "de.estrichone.notification.id"

    fun intent(context: Context, item: InboxNotification): Intent = Intent(context, MainActivity::class.java)
        .setAction("de.estrichone.OPEN_NOTIFICATION")
        .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        .putExtra(LINK, item.link).putExtra(TYPE, item.objectType).putExtra(OBJECT, item.objectId).putExtra(ID, item.id)

    /** Ziel und Benachrichtigungs-ID aus dem Intent; ohne Angaben `null`. Ohne eigenes Ziel öffnet das Postfach. */
    fun from(intent: Intent?): Pair<NotificationRoute, String?>? {
        val extras = intent?.takeIf { it.hasExtra(ID) } ?: return null
        val route = NotificationRoute.from(extras.getStringExtra(LINK), extras.getStringExtra(TYPE), extras.getStringExtra(OBJECT)) ?: NotificationRoute.Inbox
        return route to extras.getStringExtra(ID)
    }
}

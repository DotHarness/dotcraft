package com.dotharness.dotcraft.livesession

import android.annotation.SuppressLint
import android.app.Notification
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.app.NotificationChannelCompat
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat

internal const val ONGOING_ID = 1
internal const val NOTICE_ID = 2

private const val SESSION_CHANNEL = "live-session"
private const val REQUESTS_CHANNEL = "live-requests"
private const val RESULTS_CHANNEL = "live-results"
private const val FLAGS = PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT

internal object LiveNotifications {
  @Volatile
  var ongoing = LiveOngoing()

  fun createChannels(context: Context, names: LiveChannels) {
    NotificationManagerCompat.from(context).createNotificationChannelsCompat(
      listOf(
        NotificationChannelCompat.Builder(SESSION_CHANNEL, NotificationManagerCompat.IMPORTANCE_LOW).setName(names.session).build(),
        NotificationChannelCompat.Builder(REQUESTS_CHANNEL, NotificationManagerCompat.IMPORTANCE_HIGH).setName(names.requests).build(),
        NotificationChannelCompat.Builder(RESULTS_CHANNEL, NotificationManagerCompat.IMPORTANCE_DEFAULT).setName(names.results).build()
      )
    )
  }

  fun ongoing(context: Context): Notification {
    val launch = context.packageManager.getLaunchIntentForPackage(context.packageName)
    return NotificationCompat.Builder(context, SESSION_CHANNEL)
      .setSmallIcon(R.drawable.live_session_icon)
      .setContentTitle(ongoing.title)
      .setContentText(ongoing.text)
      .setOngoing(true)
      .setOnlyAlertOnce(true)
      .setRequestPromotedOngoing(true)
      .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
      .setContentIntent(launch?.let { PendingIntent.getActivity(context, 0, it, FLAGS) })
      .addAction(0, ongoing.end, action(context, "ongoing", "end", null, null))
      .build()
  }

  @SuppressLint("MissingPermission")
  fun showOngoing(context: Context) {
    NotificationManagerCompat.from(context).notify(ONGOING_ID, ongoing(context))
  }

  @SuppressLint("MissingPermission")
  fun post(context: Context, notice: LivePost) {
    val requests = notice.channel == "requests"
    val open = Intent(Intent.ACTION_VIEW, Uri.parse(notice.url)).setPackage(context.packageName)
    val builder = NotificationCompat.Builder(context, if (requests) REQUESTS_CHANNEL else RESULTS_CHANNEL)
      .setSmallIcon(R.drawable.live_session_icon)
      .setContentTitle(notice.title)
      .setContentText(notice.text)
      .setStyle(NotificationCompat.BigTextStyle().bigText(notice.text))
      .setPriority(if (requests) NotificationCompat.PRIORITY_HIGH else NotificationCompat.PRIORITY_DEFAULT)
      .setDefaults(NotificationCompat.DEFAULT_SOUND)
      .setSilent(!notice.alert)
      .setOnlyAlertOnce(true)
      .setAutoCancel(true)
      .setContentIntent(PendingIntent.getActivity(context, 0, open, FLAGS))
    if (notice.allow != null && notice.reject != null) {
      builder
        .addAction(0, notice.allow, action(context, notice.id, "allow", notice.key, notice.requestId))
        .addAction(0, notice.reject, action(context, notice.id, "reject", notice.key, notice.requestId))
    }
    NotificationManagerCompat.from(context).notify(notice.id, NOTICE_ID, builder.build())
  }

  private fun action(context: Context, tag: String, name: String, key: String?, requestId: String?): PendingIntent {
    val intent = Intent(context, LiveSessionActionReceiver::class.java)
      .setAction("$tag/$name")
      .putExtra("action", name)
      .putExtra("key", key)
      .putExtra("requestId", requestId)
    return PendingIntent.getBroadcast(context, 0, intent, FLAGS)
  }
}

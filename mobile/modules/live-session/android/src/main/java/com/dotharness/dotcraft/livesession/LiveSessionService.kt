package com.dotharness.dotcraft.livesession

import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.IBinder
import androidx.core.app.ServiceCompat

class LiveSessionService : Service() {
  override fun onBind(intent: Intent?): IBinder? = null

  override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
    try {
      ServiceCompat.startForeground(this, ONGOING_ID, LiveNotifications.ongoing(this), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
    } catch (_: RuntimeException) {
      stopSelf()
      LiveSessionModule.actions?.invoke("end", null, null)
    }
    return START_NOT_STICKY
  }

  override fun onDestroy() {
    ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
    super.onDestroy()
  }
}

class LiveSessionActionReceiver : BroadcastReceiver() {
  override fun onReceive(context: Context, intent: Intent) {
    val action = intent.getStringExtra("action") ?: return
    LiveSessionModule.actions?.invoke(action, intent.getStringExtra("key"), intent.getStringExtra("requestId"))
  }
}

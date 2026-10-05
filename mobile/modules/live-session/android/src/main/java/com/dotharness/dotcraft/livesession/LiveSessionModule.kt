package com.dotharness.dotcraft.livesession

import android.content.Context
import android.content.Intent
import android.os.Handler
import android.os.Looper
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.facebook.react.bridge.Arguments
import com.facebook.react.bridge.ReactContext
import com.facebook.react.jstasks.HeadlessJsTaskConfig
import com.facebook.react.jstasks.HeadlessJsTaskContext
import expo.modules.kotlin.exception.Exceptions
import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition
import expo.modules.kotlin.records.Field
import expo.modules.kotlin.records.Record

private const val TASK = "DotCraftLiveSession"

class LiveOngoing : Record {
  @Field
  val title: String = ""

  @Field
  val text: String = ""

  @Field
  val subText: String = ""

  @Field
  val chip: String = ""

  @Field
  val url: String = ""

  @Field
  val end: String = ""
}

class LiveChannels : Record {
  @Field
  val session: String = ""

  @Field
  val requests: String = ""

  @Field
  val results: String = ""
}

class LivePost : Record {
  @Field
  val id: String = ""

  @Field
  val channel: String = ""

  @Field
  val title: String = ""

  @Field
  val text: String = ""

  @Field
  val url: String = ""

  @Field
  val alert: Boolean = false

  @Field
  val key: String? = null

  @Field
  val requestId: String? = null

  @Field
  val allow: String? = null

  @Field
  val reject: String? = null
}

class LiveSessionModule : Module() {
  private val main = Handler(Looper.getMainLooper())
  private var task: Int? = null

  private val context: Context
    get() = appContext.reactContext ?: throw Exceptions.ReactContextLost()

  private val tasks: HeadlessJsTaskContext
    get() = HeadlessJsTaskContext.getInstance(context as ReactContext)

  override fun definition() = ModuleDefinition {
    Name("LiveSession")

    Events("action")

    OnCreate {
      actions = { action, key, requestId ->
        this@LiveSessionModule.sendEvent("action", mapOf("action" to action, "key" to key, "requestId" to requestId))
      }
    }

    OnDestroy {
      actions = null
    }

    Function("notificationsEnabled") {
      NotificationManagerCompat.from(context).areNotificationsEnabled()
    }

    Function("start") { ongoing: LiveOngoing, channels: LiveChannels ->
      LiveNotifications.createChannels(context, channels)
      LiveNotifications.ongoing = ongoing
      val started = try {
        ContextCompat.startForegroundService(context, Intent(context, LiveSessionService::class.java))
        true
      } catch (_: IllegalStateException) {
        false
      }
      if (started) main.post { task = tasks.startTask(HeadlessJsTaskConfig(TASK, Arguments.createMap(), 0, true)) }
      started
    }

    Function("update") { ongoing: LiveOngoing ->
      LiveNotifications.ongoing = ongoing
      LiveNotifications.showOngoing(context)
    }

    Function("stop") {
      context.stopService(Intent(context, LiveSessionService::class.java))
      main.post {
        task?.let { tasks.finishTask(it) }
        task = null
      }
    }

    Function("post") { notice: LivePost ->
      LiveNotifications.post(context, notice)
    }

    Function("cancel") { id: String ->
      NotificationManagerCompat.from(context).cancel(id, NOTICE_ID)
    }
  }

  companion object {
    @Volatile
    internal var actions: ((String, String?, String?) -> Unit)? = null
  }
}

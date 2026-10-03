package com.dotharness.dotcraft.pinnedtransport

import expo.modules.kotlin.Promise
import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition
import expo.modules.kotlin.records.Field
import expo.modules.kotlin.records.Record
import java.io.IOException
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import okhttp3.Call
import okhttp3.Callback
import okhttp3.Headers.Companion.toHeaders
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response

class PinnedRequestOptions : Record {
  @Field
  val url: String = ""

  @Field
  val method: String = "GET"

  @Field
  val headers: Map<String, String> = emptyMap()

  @Field
  val body: String? = null

  @Field
  val fingerprint: String = ""

  @Field
  val tunnel: String? = null
}

class PinnedSocketOptions : Record {
  @Field
  val url: String = ""

  @Field
  val headers: Map<String, String> = emptyMap()

  @Field
  val fingerprint: String = ""

  @Field
  val tunnel: String? = null
}

class PinnedTransportModule : Module() {
  private val sockets = ConcurrentHashMap<String, PinnedSocket>()

  override fun definition() = ModuleDefinition {
    Name("PinnedTransport")

    Events("open", "message", "close", "error")

    AsyncFunction("request") { options: PinnedRequestOptions, promise: Promise ->
      val target = PinnedTarget.of(options.url, "https", options.fingerprint)
      val request = Request.Builder()
        .url(RelayTunnels.route(target.url, options.tunnel))
        .headers(options.headers.toHeaders())
        .method(options.method, options.body?.toRequestBody())
        .build()
      PinnedClients.http(target.fingerprint).newCall(request).enqueue(object : Callback {
        override fun onFailure(call: Call, e: IOException) {
          promise.reject(failureCode(e), e.message ?: "The computer could not be reached.", null)
        }

        override fun onResponse(call: Call, response: Response) {
          response.use {
            promise.resolve(mapOf("status" to it.code, "body" to (it.body?.string() ?: "")))
          }
        }
      })
    }

    Function("openSocket") { options: PinnedSocketOptions ->
      val target = PinnedTarget.of(options.url, "wss", options.fingerprint)
      val request = Request.Builder().url(RelayTunnels.route(target.url, options.tunnel)).headers(options.headers.toHeaders()).build()
      val id = UUID.randomUUID().toString()
      val socket = PinnedSocket(id, { name, body -> this@PinnedTransportModule.sendEvent(name, body) }, { sockets.remove(it) })
      sockets[id] = socket
      socket.start(PinnedClients.socket(target.fingerprint), request)
      id
    }

    Function("send") { id: String, text: String ->
      sockets[id]?.send(text)
      Unit
    }

    Function("close") { id: String, code: Int, reason: String ->
      sockets[id]?.close(code, reason)
      Unit
    }

    OnDestroy {
      sockets.values.forEach { it.cancel() }
      sockets.clear()
      RelayTunnels.closeAll()
    }
  }
}

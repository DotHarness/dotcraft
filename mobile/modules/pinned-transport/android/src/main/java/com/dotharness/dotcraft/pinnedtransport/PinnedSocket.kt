package com.dotharness.dotcraft.pinnedtransport

import java.util.concurrent.atomic.AtomicBoolean
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener

internal class PinnedSocket(
  private val id: String,
  private val emit: (String, Map<String, Any?>) -> Unit,
  private val finished: (String) -> Unit
) : WebSocketListener() {
  private val done = AtomicBoolean(false)

  @Volatile
  private var socket: WebSocket? = null

  fun start(client: OkHttpClient, request: Request) {
    socket = client.newWebSocket(request, this)
  }

  fun send(text: String) {
    if (!done.get() && socket?.send(text) != true) {
      fail(UNREACHABLE_CODE, "The message could not be sent.", null)
    }
  }

  fun close(code: Int, reason: String) {
    socket?.close(code, reason)
    finish("close", mapOf("id" to id, "code" to code, "reason" to reason))
  }

  fun cancel() {
    if (done.compareAndSet(false, true)) {
      socket?.cancel()
      finished(id)
    }
  }

  override fun onOpen(webSocket: WebSocket, response: Response) {
    if (!done.get()) emit("open", mapOf("id" to id))
  }

  override fun onMessage(webSocket: WebSocket, text: String) {
    if (!done.get()) emit("message", mapOf("id" to id, "text" to text))
  }

  override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
    webSocket.close(1000, null)
    finish("close", mapOf("id" to id, "code" to code, "reason" to reason))
  }

  override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
    val status = response?.code
    if (status != null && status != 101) {
      fail("ERR_HTTP", "The computer answered $status.", status)
    } else {
      fail(failureCode(t), t.message ?: "The connection closed.", null)
    }
  }

  private fun fail(code: String, message: String, status: Int?) {
    finish("error", mapOf("id" to id, "code" to code, "message" to message, "status" to status))
  }

  private fun finish(event: String, body: Map<String, Any?>) {
    if (!done.compareAndSet(false, true)) return
    emit(event, body)
    if (event == "error") socket?.cancel()
    finished(id)
  }
}

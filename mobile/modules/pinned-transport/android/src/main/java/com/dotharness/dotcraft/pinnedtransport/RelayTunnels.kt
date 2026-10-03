package com.dotharness.dotcraft.pinnedtransport

import java.io.IOException
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URI
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString
import okio.ByteString.Companion.toByteString

private const val LOOPBACK = "127.0.0.1"

internal object RelayTunnels {
  private val client by lazy { OkHttpClient.Builder().pingInterval(20L, TimeUnit.SECONDS).build() }
  private val forwarders = ConcurrentHashMap<String, RelayForwarder>()

  fun route(url: String, tunnel: String?): String {
    if (tunnel == null) return url
    val scheme = runCatching { URI(tunnel).scheme?.lowercase() }.getOrNull()
    if (scheme != "wss" && scheme != "ws") {
      throw PinnedTargetException("ERR_INSECURE_URL", "Only ws and wss relay URLs are allowed.")
    }
    val port = forwarders.computeIfAbsent(tunnel) { RelayForwarder(client, it) }.port
    val target = URI(url)
    return "${target.scheme}://$LOOPBACK:$port${target.rawPath}${target.rawQuery?.let { "?$it" } ?: ""}"
  }

  fun closeAll() {
    forwarders.values.forEach { it.close() }
    forwarders.clear()
  }
}

private class RelayForwarder(client: OkHttpClient, tunnel: String) {
  private val server = ServerSocket(0, 16, InetAddress.getByName(LOOPBACK))
  val port: Int = server.localPort

  init {
    thread(isDaemon = true) {
      while (true) {
        val local = try {
          server.accept()
        } catch (_: IOException) {
          break
        }
        client.newWebSocket(Request.Builder().url(tunnel).build(), RelayTunnel(local))
      }
    }
  }

  fun close() = server.close()
}

private class RelayTunnel(private val local: Socket) : WebSocketListener() {
  override fun onOpen(webSocket: WebSocket, response: Response) {
    thread(isDaemon = true) {
      val buffer = ByteArray(16 * 1024)
      try {
        val input = local.getInputStream()
        while (true) {
          val read = input.read(buffer)
          if (read < 0 || !webSocket.send(buffer.toByteString(0, read))) break
        }
      } catch (_: IOException) {
      }
      webSocket.close(1000, null)
    }
  }

  override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
    try {
      local.getOutputStream().write(bytes.toByteArray())
    } catch (_: IOException) {
      webSocket.cancel()
    }
  }

  override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
    webSocket.close(1000, null)
    local.close()
  }

  override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
    local.close()
  }
}

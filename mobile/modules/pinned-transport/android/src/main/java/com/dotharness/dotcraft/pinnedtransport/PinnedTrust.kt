package com.dotharness.dotcraft.pinnedtransport

import expo.modules.kotlin.exception.CodedException
import java.net.URI
import java.security.MessageDigest
import java.security.cert.Certificate
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLPeerUnverifiedException
import javax.net.ssl.SSLSession
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager
import okhttp3.ConnectionSpec
import okhttp3.OkHttpClient

internal const val MISMATCH_CODE = "ERR_PINNING_MISMATCH"
internal const val UNREACHABLE_CODE = "ERR_UNREACHABLE"

internal class PinMismatchException : CertificateException("The certificate does not match the pinned fingerprint.")

internal class PinnedTargetException(code: String, message: String) : CodedException(code, message, null)

internal fun sha256Hex(certificate: Certificate?): String? {
  val leaf = certificate as? X509Certificate ?: return null
  return MessageDigest.getInstance("SHA-256").digest(leaf.encoded).joinToString("") { "%02x".format(it) }
}

internal class FingerprintTrustManager(private val fingerprint: String) : X509TrustManager {
  override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
    throw CertificateException("Client certificates are not accepted.")
  }

  override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
    if (sha256Hex(chain?.firstOrNull()) != fingerprint) throw PinMismatchException()
  }

  override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
}

internal class FingerprintHostnameVerifier(private val fingerprint: String) : HostnameVerifier {
  override fun verify(hostname: String?, session: SSLSession?): Boolean {
    val leaf = runCatching { session?.peerCertificates?.firstOrNull() }.getOrNull()
    return sha256Hex(leaf) == fingerprint
  }
}

internal data class PinnedTarget(val url: String, val fingerprint: String) {
  companion object {
    private val digest = Regex("^[0-9a-f]{64}$")

    fun of(url: String, scheme: String, fingerprint: String): PinnedTarget {
      val normalized = fingerprint.lowercase()
      if (!digest.matches(normalized)) {
        throw PinnedTargetException("ERR_INVALID_FINGERPRINT", "The pinned fingerprint is not a SHA-256 digest.")
      }
      val parsed = runCatching { URI(url) }.getOrNull()
      if (parsed?.scheme?.lowercase() != scheme || parsed.host.isNullOrEmpty()) {
        throw PinnedTargetException("ERR_INSECURE_URL", "Only $scheme URLs are allowed.")
      }
      return PinnedTarget(url, normalized)
    }
  }
}

internal fun failureCode(error: Throwable): String {
  var current: Throwable? = error
  while (current != null) {
    if (current is PinMismatchException || current is SSLPeerUnverifiedException) return MISMATCH_CODE
    current = current.cause
  }
  return UNREACHABLE_CODE
}

internal object PinnedClients {
  private val base by lazy { OkHttpClient() }
  private val clients = ConcurrentHashMap<String, OkHttpClient>()

  fun http(fingerprint: String): OkHttpClient = clients.getOrPut("http:$fingerprint") { build(fingerprint, socket = false) }

  fun socket(fingerprint: String): OkHttpClient = clients.getOrPut("socket:$fingerprint") { build(fingerprint, socket = true) }

  private fun build(fingerprint: String, socket: Boolean): OkHttpClient {
    val trust = FingerprintTrustManager(fingerprint)
    val context = SSLContext.getInstance("TLS").apply { init(null, arrayOf<TrustManager>(trust), null) }
    return base.newBuilder()
      .sslSocketFactory(context.socketFactory, trust)
      .hostnameVerifier(FingerprintHostnameVerifier(fingerprint))
      .connectionSpecs(listOf(ConnectionSpec.MODERN_TLS))
      .followRedirects(false)
      .retryOnConnectionFailure(false)
      .connectTimeout(3L, TimeUnit.SECONDS)
      .readTimeout(if (socket) 0L else 15L, TimeUnit.SECONDS)
      .pingInterval(if (socket) 20L else 0L, TimeUnit.SECONDS)
      .build()
  }
}

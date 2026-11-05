import { app, shell } from 'electron'
import { openDesktopServiceHandoff } from './desktopServiceHandoff'

export const MAX_EXTERNAL_URL_LENGTH = 4096

export function sanitizeHttpOrHttpsUrl(url: string | undefined): string | null {
  if (url === undefined || typeof url !== 'string') return null
  const trimmed = url.trim()
  if (trimmed === '') return null
  let parsed: URL
  try {
    parsed = new URL(trimmed)
  } catch {
    return null
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null
  return parsed.href
}

/**
 * Allows browser-style URLs and OS-registered custom protocols, but blocks local or
 * scriptable schemes that would turn an untrusted URL into code execution.
 */
export function sanitizeExternalUrl(url: string | undefined): string | null {
  if (url === undefined || typeof url !== 'string') return null
  const trimmed = url.trim()
  if (trimmed === '' || trimmed.length > MAX_EXTERNAL_URL_LENGTH) return null
  let parsed: URL
  try {
    parsed = new URL(trimmed)
  } catch {
    return null
  }
  const protocol = parsed.protocol.toLowerCase()
  if (
    protocol === 'file:' ||
    protocol === 'javascript:' ||
    protocol === 'data:' ||
    protocol === 'vbscript:' ||
    !/^[a-z][a-z0-9+.-]*:$/.test(protocol)
  ) {
    return null
  }
  return parsed.href
}

export async function openExternalUrl(url: string): Promise<void> {
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('Invalid URL')
  }
  if (url.trim().length > MAX_EXTERNAL_URL_LENGTH) {
    throw new Error('URL too long')
  }
  const safe = sanitizeExternalUrl(url)
  if (safe === null) {
    try {
      new URL(url.trim())
    } catch {
      throw new Error('Invalid URL')
    }
    throw new Error('URL scheme is not allowed')
  }
  await shell.openExternal(safe)
}

export async function openExternalHttpUrl(url: string): Promise<void> {
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('Invalid URL')
  }
  const safe = sanitizeHttpOrHttpsUrl(url)
  if (safe === null) {
    try {
      new URL(url.trim())
    } catch {
      throw new Error('Invalid URL')
    }
    throw new Error('Only http(s) URLs are allowed')
  }
  await shell.openExternal(safe)
}

export function isLoopbackHostname(hostname: string): boolean {
  const normalized = hostname.toLowerCase()
  return (
    normalized === 'localhost' ||
    normalized === '::1' ||
    normalized === '[::1]' ||
    /^127(?:\.\d{1,3}){3}$/.test(normalized)
  )
}
async function invokeLoopbackHandoff(url: string): Promise<void> {
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), 15000)
  try {
    const response = await fetch(url, {
      method: 'GET',
      redirect: 'follow',
      signal: controller.signal
    })
    if (!response.ok) {
      throw new Error(`App handoff failed with HTTP ${response.status}`)
    }
  } finally {
    clearTimeout(timeout)
  }
}

/**
 * Loopback HTTP handoffs are invoked in-process so local app servers can complete
 * connect/bind without opening a browser tab.
 */
export async function openAppHandoffUrl(url: string, ownerId?: number): Promise<void> {
  if (typeof url !== 'string' || url.trim() === '') {
    throw new Error('Invalid URL')
  }
  let parsedUrl: URL
  try {
    parsedUrl = new URL(url)
  } catch {
    throw new Error('Invalid URL')
  }
  if (parsedUrl.protocol === 'dotcraft-service:') {
    if (ownerId === undefined) throw new Error('App handoff requires a Desktop window.')
    await openDesktopServiceHandoff(url, ownerId)
    return
  }
  if ((parsedUrl.protocol === 'http:' || parsedUrl.protocol === 'https:') && isLoopbackHostname(parsedUrl.hostname)) {
    await invokeLoopbackHandoff(parsedUrl.href)
    return
  }

  await openExternalUrl(url)
}

export function getProtocolHandlerName(protocol: string): string {
  if (typeof protocol !== 'string' || protocol.trim() === '') return ''
  const normalized = protocol.trim().replace(/:$/, '')
  if (!/^[a-z][a-z0-9+.-]*$/i.test(normalized)) return ''
  try {
    return app.getApplicationNameForProtocol(`${normalized}://`) || ''
  } catch {
    return ''
  }
}

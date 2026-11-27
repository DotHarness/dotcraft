import { app, shell } from 'electron'

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

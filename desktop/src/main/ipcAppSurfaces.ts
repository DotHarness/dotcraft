import type { DesktopAppServerClient } from './DesktopAppServerClient'
import { isLoopbackHostname, MAX_EXTERNAL_URL_LENGTH } from './ipcExternalLinks'

async function readDesktopPluginResponse(response: Response): Promise<unknown> {
  if (!response.ok) {
    throw new Error(`Request failed with HTTP ${response.status}`)
  }
  const text = await response.text()
  if (text.length > 1_000_000) {
    throw new Error('Response is too large')
  }
  return text.trim() === '' ? null : JSON.parse(text)
}

interface ResolvedDesktopAppSurface {
  appId: string
  surfaceId: string
  endpoint: string
  bearer: string
  expiresAt: string
}

function appSurfaceUnavailable(): Error {
  return new Error('AppSurfaceUnavailable')
}

function validateDesktopPluginRelativePath(relativePath: string): URL {
  if (typeof relativePath !== 'string' || relativePath.length === 0 || relativePath.length > MAX_EXTERNAL_URL_LENGTH) {
    throw new Error('Invalid App Surface relative path')
  }
  if (
    !relativePath.startsWith('/') ||
    relativePath.startsWith('//') ||
    relativePath.includes('\\') ||
    relativePath.includes('#')
  ) {
    throw new Error('App Surface path must be origin-relative')
  }
  const pathOnly = relativePath.split('?', 1)[0]
  for (const rawSegment of pathOnly.split('/')) {
    let segment = rawSegment
    for (let depth = 0; depth < 4; depth++) {
      let decoded: string
      try {
        decoded = decodeURIComponent(segment)
      } catch {
        throw new Error('Invalid App Surface relative path')
      }
      if (decoded.split(/[\\/]/).some((part) => part === '.' || part === '..')) {
        throw new Error('App Surface path traversal is not allowed')
      }
      if (decoded === segment) break
      segment = decoded
    }
  }
  const parsed = new URL(relativePath, 'http://dotcraft-app-surface.invalid')
  if (parsed.origin !== 'http://dotcraft-app-surface.invalid' || parsed.username || parsed.password || parsed.hash) {
    throw new Error('App Surface path must be origin-relative')
  }
  return parsed
}

function resolveDesktopPluginAppSurfaceUrl(endpoint: string, relativePath: string): string {
  let base: URL
  try {
    base = new URL(endpoint)
  } catch {
    throw appSurfaceUnavailable()
  }
  if (
    (base.protocol !== 'http:' && base.protocol !== 'https:') ||
    !isLoopbackHostname(base.hostname) ||
    base.username !== '' ||
    base.password !== '' ||
    base.hash !== ''
  ) {
    throw appSurfaceUnavailable()
  }
  const relative = validateDesktopPluginRelativePath(relativePath)
  const basePath = base.pathname.endsWith('/') ? base.pathname : `${base.pathname}/`
  const target = new URL(base.href)
  target.pathname = `${basePath}${relative.pathname.slice(1)}`
  target.search = relative.search
  target.hash = ''
  if (target.origin !== base.origin || !target.pathname.startsWith(basePath)) {
    throw new Error('App Surface path must stay within the resolved endpoint base path')
  }
  return target.href
}

async function resolveDesktopPluginAppSurface(
  client: DesktopAppServerClient,
  appId: string,
  surfaceId: string
): Promise<ResolvedDesktopAppSurface> {
  const result: unknown = await client.sendRequest('app/surface/resolve', { appId, surfaceId }, 20_000)
  if (result == null || typeof result !== 'object' || Array.isArray(result)) throw appSurfaceUnavailable()
  const surface = result as Partial<ResolvedDesktopAppSurface>
  if (
    surface.appId !== appId ||
    surface.surfaceId !== surfaceId ||
    typeof surface.endpoint !== 'string' ||
    typeof surface.bearer !== 'string' ||
    surface.bearer.trim() === '' ||
    typeof surface.expiresAt !== 'string' ||
    !Number.isFinite(Date.parse(surface.expiresAt))
  ) {
    throw appSurfaceUnavailable()
  }
  return surface as ResolvedDesktopAppSurface
}

export async function requestDesktopPluginAppSurfaceJson(
  client: DesktopAppServerClient,
  method: 'GET' | 'POST',
  appId: string,
  surfaceId: string,
  relativePath: string,
  body?: unknown,
  timeoutMs?: number
): Promise<unknown> {
  const surface = await resolveDesktopPluginAppSurface(client, appId, surfaceId)
  if (Date.parse(surface.expiresAt) <= Date.now()) throw appSurfaceUnavailable()
  const url = resolveDesktopPluginAppSurfaceUrl(surface.endpoint, relativePath)
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), Math.min(Math.max(timeoutMs ?? 10_000, 1_000), 30_000))
  try {
    const response = await fetch(url, {
      method,
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${surface.bearer}`,
        ...(method === 'POST' ? { 'Content-Type': 'application/json' } : {})
      },
      ...(method === 'POST' ? { body: JSON.stringify(body ?? {}) } : {}),
      redirect: 'error',
      signal: controller.signal
    })
    return await readDesktopPluginResponse(response)
  } finally {
    clearTimeout(timeout)
  }
}

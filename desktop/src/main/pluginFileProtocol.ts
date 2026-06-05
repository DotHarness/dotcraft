import { net, protocol } from 'electron'
import { promises as fs } from 'fs'
import * as path from 'path'
import { pathToFileURL } from 'url'

export const PLUGIN_FILE_SCHEME = 'dotcraft-plugin'

let defaultProtocolHandlerInstalled = false
const authorizedPluginRoots = new Map<string, string>()

export function registerPluginFileScheme(): void {
  protocol.registerSchemesAsPrivileged([
    {
      scheme: PLUGIN_FILE_SCHEME,
      privileges: {
        standard: true,
        secure: true,
        supportFetchAPI: true,
        bypassCSP: false,
        stream: true,
        corsEnabled: true
      }
    }
  ])
}

export function installPluginFileProtocolHandler(): void {
  if (defaultProtocolHandlerInstalled) return
  defaultProtocolHandlerInstalled = true
  protocol.handle(PLUGIN_FILE_SCHEME, handlePluginFileRequest)
}

export async function authorizePluginRoot(pluginId: string, rootPath: string): Promise<void> {
  const id = normalizePluginId(pluginId)
  if (!id) {
    throw new Error('Plugin id is required')
  }
  if (!path.isAbsolute(rootPath)) {
    throw new Error('Plugin root must be an absolute path')
  }

  const resolved = await fs.realpath(path.resolve(rootPath))
  const manifest = path.join(resolved, '.craft-plugin', 'plugin.json')
  const stat = await fs.stat(manifest)
  if (!stat.isFile()) {
    throw new Error(`Plugin manifest not found for ${pluginId}`)
  }
  const manifestJson = JSON.parse(await fs.readFile(manifest, 'utf8')) as { id?: unknown }
  const manifestPluginId = typeof manifestJson.id === 'string' ? normalizePluginId(manifestJson.id) : ''
  if (manifestPluginId !== id) {
    throw new Error(`Plugin root id mismatch for ${pluginId}`)
  }

  authorizedPluginRoots.set(id, resolved)
}

export function clearAuthorizedPluginRoots(): void {
  authorizedPluginRoots.clear()
}

export function buildPluginFileUrl(pluginId: string, absolutePath: string): string {
  const id = normalizePluginId(pluginId)
  if (!id) {
    throw new Error('Plugin id is required')
  }
  if (!isAbsoluteFilePath(absolutePath)) {
    throw new Error('Plugin file path must be absolute')
  }

  const normalized = absolutePath.replace(/\\/g, '/')
  const withLeadingSlash = normalized.startsWith('/') ? normalized : `/${normalized}`
  const encodedPath = withLeadingSlash
    .split('/')
    .map((segment) => encodeURIComponent(segment))
    .join('/')
  return `${PLUGIN_FILE_SCHEME}://${encodeURIComponent(id)}${encodedPath}`
}

export async function handlePluginFileRequest(request: Request): Promise<Response> {
  try {
    const corsOrigin = resolveCorsOrigin(request)
    if (!corsOrigin) {
      return new Response(null, { status: 403 })
    }

    const { pluginId, absolutePath } = pluginUrlToPath(request.url)
    const root = authorizedPluginRoots.get(pluginId)
    if (!root) {
      return new Response(null, { status: 403 })
    }

    const resolvedPath = await fs.realpath(path.resolve(absolutePath))
    if (!isPathWithin(resolvedPath, root)) {
      return new Response(null, { status: 403 })
    }

    const stat = await fs.stat(resolvedPath)
    if (!stat.isFile()) {
      return new Response(null, { status: 403 })
    }

    const response = await net.fetch(pathToFileURL(resolvedPath).toString())
    const headers = new Headers(response.headers)
    const contentType = mimeTypeForPath(resolvedPath)
    if (contentType) {
      headers.set('Content-Type', contentType)
    }
    applyCorsHeaders(headers, corsOrigin)
    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers
    })
  } catch {
    return new Response(null, { status: 500 })
  }
}

export function pluginUrlToPath(url: string): { pluginId: string; absolutePath: string } {
  const parsed = new URL(url)
  if (parsed.protocol !== `${PLUGIN_FILE_SCHEME}:`) {
    throw new Error('Invalid plugin file URL scheme')
  }
  const pluginId = normalizePluginId(decodeURIComponent(parsed.hostname))
  if (!pluginId) {
    throw new Error('Invalid plugin file URL host')
  }

  const decodedPath = parsed.pathname
    .split('/')
    .map((segment) => decodeURIComponent(segment))
    .join('/')
  return {
    pluginId,
    absolutePath: stripWindowsPathLeadingSlash(decodedPath)
  }
}

function normalizePluginId(value: string): string {
  return value.trim().toLowerCase()
}

function isAbsoluteFilePath(value: string): boolean {
  return path.isAbsolute(value) || /^[a-zA-Z]:[\\/]/.test(value)
}

function resolveCorsOrigin(request: Request): string | null {
  const origin = request.headers.get('Origin')
  if (!origin) {
    return '*'
  }
  if (origin === 'null') {
    return origin
  }

  try {
    const parsed = new URL(origin)
    if ((parsed.protocol === 'http:' || parsed.protocol === 'https:') && isLoopbackHost(parsed.hostname)) {
      return origin
    }
    if (parsed.protocol === 'file:') {
      return origin
    }
  } catch {
    return null
  }

  return null
}

function isLoopbackHost(hostname: string): boolean {
  return hostname === 'localhost' ||
    hostname === '127.0.0.1' ||
    hostname === '::1' ||
    hostname === '[::1]'
}

function applyCorsHeaders(headers: Headers, origin: string): void {
  headers.set('Access-Control-Allow-Origin', origin)
  headers.set('Access-Control-Allow-Methods', 'GET, HEAD')
  headers.set('Vary', appendVaryOrigin(headers.get('Vary')))
}

function appendVaryOrigin(value: string | null): string {
  if (!value) return 'Origin'
  const parts = value.split(',').map((part) => part.trim().toLowerCase())
  return parts.includes('origin') ? value : `${value}, Origin`
}

function stripWindowsPathLeadingSlash(decodedPath: string): string {
  if (/^\/[a-zA-Z]:\//.test(decodedPath)) {
    return decodedPath.slice(1)
  }
  return decodedPath
}

function isPathWithin(targetPath: string, rootPath: string): boolean {
  const rel = path.relative(rootPath, targetPath)
  return rel === '' || (!!rel && !rel.startsWith('..') && !path.isAbsolute(rel))
}

function mimeTypeForPath(filePath: string): string | null {
  switch (path.extname(filePath).toLowerCase()) {
    case '.js':
    case '.mjs':
      return 'text/javascript; charset=utf-8'
    case '.css':
      return 'text/css; charset=utf-8'
    case '.json':
      return 'application/json; charset=utf-8'
    case '.svg':
      return 'image/svg+xml'
    case '.png':
      return 'image/png'
    case '.jpg':
    case '.jpeg':
      return 'image/jpeg'
    case '.webp':
      return 'image/webp'
    default:
      return null
  }
}

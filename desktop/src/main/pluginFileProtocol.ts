import { createHash } from 'crypto'
import { net, protocol } from 'electron'
import { promises as fs } from 'fs'
import { tmpdir } from 'os'
import * as path from 'path'
import { pathToFileURL } from 'url'

export const PLUGIN_FILE_SCHEME = 'dotcraft-plugin'

const REVISION_DOMAIN = Buffer.from('DotCraft.PluginDesktopRevision\0v1\0', 'utf8')
const DESKTOP_OUTPUT_PREFIX = './desktop/dist/'
const MAX_TREE_ENTRIES = 20_000
const MAX_TREE_FILES = 10_000
const MAX_TREE_DEPTH = 64
const MAX_TREE_BYTES = 512 * 1024 * 1024

export interface DesktopPluginModuleRequest {
  pluginId: string
  version: string
  revision: string
  rootPath: string
}

export interface DesktopPluginModuleRoute {
  entryUrl: string
  styleUrls: string[]
}

export interface DesktopPluginModuleRouteOptions {
  remote?: boolean
  packagedPluginRoots?: readonly string[]
}

interface DesktopPluginManifest {
  entry: string
  styles: string[]
}

interface ModuleRoute {
  distRoot: string
  snapshotRoot?: string
}

interface TreeEntry {
  relativePath: string
  absolutePath: string
  directory: boolean
}

let defaultProtocolHandlerInstalled = false
const moduleRoutes = new Map<string, ModuleRoute>()

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

export async function registerDesktopPluginModuleRoute(
  request: DesktopPluginModuleRequest,
  options: DesktopPluginModuleRouteOptions = {}
): Promise<DesktopPluginModuleRoute> {
  const pluginId = normalizePluginId(request.pluginId)
  if (!pluginId) throw new Error('Desktop Plugin id is invalid.')
  if (!request.version) throw new Error('Desktop Plugin version is required.')
  if (!/^[0-9a-f]{64}$/.test(request.revision)) throw new Error('Desktop Plugin revision is invalid.')

  const rootPath = options.remote === true
    ? await findPackagedPluginRoot(pluginId, options.packagedPluginRoots ?? [])
    : await resolveLocalPluginRoot(request.rootPath)
  const bundle = await readDesktopPluginBundle(rootPath)
  if (normalizePluginId(bundle.pluginId) !== pluginId) {
    throw new Error('Desktop Plugin root id does not match the snapshot.')
  }
  if (bundle.version !== request.version) throw new Error('Desktop Plugin version does not match local code.')
  if (bundle.revision !== request.revision) throw new Error('Desktop Plugin revision does not match local code.')

  const key = routeKey(pluginId, request.revision)
  if (!moduleRoutes.has(key)) {
    const snapshotRoot = options.remote === true
      ? undefined
      : await materializeDesktopSnapshot(bundle.distRoot, bundle.desktop, request.revision)
    const route = snapshotRoot
      ? { distRoot: snapshotRoot, snapshotRoot }
      : { distRoot: bundle.distRoot }
    const existing = moduleRoutes.get(key)
    if (existing) disposeModuleRoute(route)
    else moduleRoutes.set(key, route)
  }
  return {
    entryUrl: buildPluginFileUrl(pluginId, request.revision, distRelativePath(bundle.desktop.entry)),
    styleUrls: bundle.desktop.styles.map((style) =>
      buildPluginFileUrl(pluginId, request.revision, distRelativePath(style)))
  }
}

export function removeDesktopPluginModuleRoute(pluginId: string, revision: string): void {
  const key = routeKey(normalizePluginId(pluginId), revision)
  const route = moduleRoutes.get(key)
  if (!route) return
  moduleRoutes.delete(key)
  disposeModuleRoute(route)
}

export function clearDesktopPluginModuleRoutes(): void {
  for (const route of moduleRoutes.values()) disposeModuleRoute(route)
  moduleRoutes.clear()
}

export function buildPluginFileUrl(pluginId: string, revision: string, relativePath: string): string {
  const id = normalizePluginId(pluginId)
  if (!id) throw new Error('Desktop Plugin id is required.')
  if (!/^[0-9a-f]{64}$/.test(revision)) throw new Error('Desktop Plugin revision is invalid.')
  const normalized = normalizeRouteRelativePath(relativePath)
  const encodedPath = normalized.split('/').map((segment) => encodeURIComponent(segment)).join('/')
  return `${PLUGIN_FILE_SCHEME}://${encodeURIComponent(id)}/${revision}/${encodedPath}`
}

export async function handlePluginFileRequest(request: Request): Promise<Response> {
  try {
    const corsOrigin = resolveCorsOrigin(request)
    if (!corsOrigin) return new Response(null, { status: 403 })

    const target = pluginUrlToRoute(request.url)
    const route = moduleRoutes.get(routeKey(target.pluginId, target.revision))
    if (!route) return new Response(null, { status: 403 })

    const resolvedPath = await fs.realpath(path.join(route.distRoot, ...target.relativePath.split('/')))
    if (!isPathWithin(resolvedPath, route.distRoot)) return new Response(null, { status: 403 })
    if (!(await fs.stat(resolvedPath)).isFile()) return new Response(null, { status: 403 })

    const response = await net.fetch(pathToFileURL(resolvedPath).toString())
    const headers = new Headers(response.headers)
    const contentType = mimeTypeForPath(resolvedPath)
    if (contentType) headers.set('Content-Type', contentType)
    applyCorsHeaders(headers, corsOrigin)
    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers
    })
  } catch {
    return new Response(null, { status: 404 })
  }
}

export function pluginUrlToRoute(url: string): {
  pluginId: string
  revision: string
  relativePath: string
} {
  const parsed = new URL(url)
  if (parsed.protocol !== `${PLUGIN_FILE_SCHEME}:`) throw new Error('Invalid Desktop Plugin URL scheme.')
  const pluginId = normalizePluginId(decodeURIComponent(parsed.hostname))
  if (!pluginId) throw new Error('Invalid Desktop Plugin URL host.')
  const segments = parsed.pathname.split('/').filter(Boolean).map((segment) => decodeURIComponent(segment))
  const revision = segments.shift() ?? ''
  if (!/^[0-9a-f]{64}$/.test(revision)) throw new Error('Invalid Desktop Plugin URL revision.')
  return {
    pluginId,
    revision,
    relativePath: normalizeRouteRelativePath(segments.join('/'))
  }
}

export async function computeDesktopPluginRevision(
  distRoot: string,
  desktop: DesktopPluginManifest
): Promise<string> {
  const hash = createHash('sha256')
  hash.update(REVISION_DOMAIN)
  appendLengthPrefixedUtf8(hash, desktop.entry)
  const styleCount = Buffer.allocUnsafe(4)
  styleCount.writeInt32LE(desktop.styles.length)
  hash.update(styleCount)
  for (const style of desktop.styles) appendLengthPrefixedUtf8(hash, style)

  let totalBytes = 0
  for (const entry of await enumerateDesktopTree(distRoot)) {
    const pathBytes = Buffer.from(entry.relativePath, 'utf8')
    if (entry.directory) {
      hash.update(treeEntryHeader(0, pathBytes.length, 0))
      hash.update(pathBytes)
      continue
    }

    await rejectFilesystemLink(entry.absolutePath)
    const file = await fs.open(entry.absolutePath, 'r')
    try {
      const contentLength = (await file.stat()).size
      hash.update(treeEntryHeader(1, pathBytes.length, contentLength))
      hash.update(pathBytes)
      const buffer = Buffer.allocUnsafe(81920)
      let fileBytes = 0
      let count: number
      do {
        ({ bytesRead: count } = await file.read(buffer, 0, buffer.length, null))
        if (count > 0) {
          totalBytes += count
          fileBytes += count
          if (totalBytes > MAX_TREE_BYTES) {
            throw new Error('Desktop Plugin output exceeds the 512 MiB content limit.')
          }
          hash.update(buffer.subarray(0, count))
        }
      } while (count > 0)
      if (fileBytes !== contentLength || (await file.stat()).size !== contentLength) {
        throw new Error('Desktop Plugin output changed while its revision was being computed.')
      }
    } finally {
      await file.close()
    }
    await rejectFilesystemLink(entry.absolutePath)
  }
  return hash.digest('hex')
}

async function readDesktopPluginBundle(rootPath: string): Promise<{
  pluginId: string
  version: string
  desktop: DesktopPluginManifest
  revision: string
  distRoot: string
}> {
  const manifestPath = path.join(rootPath, '.craft-plugin', 'plugin.json')
  const manifest = JSON.parse(await fs.readFile(manifestPath, 'utf8')) as Record<string, unknown>
  const pluginId = typeof manifest.id === 'string' ? manifest.id : ''
  const version = typeof manifest.version === 'string' ? manifest.version : ''
  const desktop = parseDesktopManifest(manifest.desktop)
  const declaredDistRoot = path.join(rootPath, 'desktop', 'dist')
  await rejectFilesystemLink(declaredDistRoot)
  const distRoot = await fs.realpath(declaredDistRoot)
  if (!isPathWithin(distRoot, rootPath)) throw new Error('Desktop Plugin output must stay inside its plugin root.')
  await requireDeclaredFile(rootPath, desktop.entry)
  for (const style of desktop.styles) await requireDeclaredFile(rootPath, style)
  return {
    pluginId,
    version,
    desktop,
    revision: await computeDesktopPluginRevision(distRoot, desktop),
    distRoot
  }
}

function parseDesktopManifest(value: unknown): DesktopPluginManifest {
  if (!isRecord(value)) throw new Error('Desktop Plugin manifest is missing desktop.')
  if (typeof value.entry !== 'string') throw new Error('Desktop Plugin entry is required.')
  validateDesktopPath(value.entry, '.mjs')
  const rawStyles = value.styles == null ? [] : value.styles
  if (!Array.isArray(rawStyles)) throw new Error('Desktop Plugin styles must be an array.')
  const styles: string[] = []
  for (const style of rawStyles) {
    if (typeof style !== 'string') throw new Error('Desktop Plugin style path is invalid.')
    validateDesktopPath(style, '.css')
    if (styles.includes(style)) throw new Error(`Desktop Plugin style '${style}' is duplicated.`)
    styles.push(style)
  }
  return { entry: value.entry, styles }
}

function validateDesktopPath(value: string, extension: '.mjs' | '.css'): void {
  if (!value.startsWith(DESKTOP_OUTPUT_PREFIX) || !value.endsWith(extension) || value.includes('\\')) {
    throw new Error(`Desktop Plugin path must be a ${extension} file under ./desktop/dist/.`)
  }
  const segments = value.slice(DESKTOP_OUTPUT_PREFIX.length).split('/')
  if (segments.some((segment) => segment === '' || segment === '.' || segment === '..')) {
    throw new Error('Desktop Plugin path contains an invalid segment.')
  }
}

async function requireDeclaredFile(rootPath: string, manifestPath: string): Promise<void> {
  const outputRoot = path.join(rootPath, 'desktop', 'dist')
  const resolved = await fs.realpath(path.join(rootPath, ...manifestPath.slice(2).split('/')))
  if (!isPathWithin(resolved, outputRoot)) throw new Error('Desktop Plugin file escaped desktop/dist.')
  if (!(await fs.stat(resolved)).isFile()) throw new Error('Desktop Plugin path must reference a file.')
}

async function enumerateDesktopTree(distRoot: string): Promise<TreeEntry[]> {
  await rejectFilesystemLink(distRoot)
  const result: TreeEntry[] = []
  const pending = [{ absolutePath: distRoot, depth: 0 }]
  let entryCount = 0
  let fileCount = 0
  while (pending.length > 0) {
    const current = pending.pop()!
    if (current.depth >= MAX_TREE_DEPTH) {
      throw new Error('Desktop Plugin output exceeds the directory depth limit.')
    }
    for (const child of await fs.readdir(current.absolutePath, { withFileTypes: true })) {
      const absolutePath = path.join(current.absolutePath, child.name)
      const stat = await fs.lstat(absolutePath)
      if (stat.isSymbolicLink()) throw new Error('Desktop Plugin output cannot contain filesystem links.')
      entryCount += 1
      if (entryCount > MAX_TREE_ENTRIES) {
        throw new Error('Desktop Plugin output contains too many filesystem entries.')
      }
      const relativePath = path.relative(distRoot, absolutePath).replace(/\\/g, '/')
      if (child.isDirectory()) {
        result.push({ relativePath, absolutePath, directory: true })
        pending.push({ absolutePath, depth: current.depth + 1 })
      } else if (child.isFile()) {
        fileCount += 1
        if (fileCount > MAX_TREE_FILES) throw new Error('Desktop Plugin output contains too many files.')
        result.push({ relativePath, absolutePath, directory: false })
      }
    }
  }
  return result.sort((left, right) => ordinalCompare(left.relativePath, right.relativePath))
}

async function materializeDesktopSnapshot(
  distRoot: string,
  desktop: DesktopPluginManifest,
  expectedRevision: string
): Promise<string> {
  const createdRoot = await fs.mkdtemp(path.join(tmpdir(), 'dotcraft-plugin-route-'))
  try {
    const snapshotRoot = await fs.realpath(createdRoot)
    let totalBytes = 0
    for (const entry of await enumerateDesktopTree(distRoot)) {
      const targetPath = path.join(snapshotRoot, ...entry.relativePath.split('/'))
      if (entry.directory) {
        await fs.mkdir(targetPath, { recursive: true })
        continue
      }

      await fs.mkdir(path.dirname(targetPath), { recursive: true })
      await rejectFilesystemLink(entry.absolutePath)
      const source = await fs.open(entry.absolutePath, 'r')
      try {
        const target = await fs.open(targetPath, 'wx')
        try {
          const contentLength = (await source.stat()).size
          const buffer = Buffer.allocUnsafe(81920)
          let fileBytes = 0
          let count: number
          do {
            ({ bytesRead: count } = await source.read(buffer, 0, buffer.length, null))
            if (count > 0) {
              totalBytes += count
              fileBytes += count
              if (totalBytes > MAX_TREE_BYTES) {
                throw new Error('Desktop Plugin output exceeds the 512 MiB content limit.')
              }
              await target.writeFile(buffer.subarray(0, count))
            }
          } while (count > 0)
          if (fileBytes !== contentLength || (await source.stat()).size !== contentLength) {
            throw new Error('Desktop Plugin output changed while its snapshot was being created.')
          }
        } finally {
          await target.close()
        }
      } finally {
        await source.close()
      }
      await rejectFilesystemLink(entry.absolutePath)
    }

    if (await computeDesktopPluginRevision(snapshotRoot, desktop) !== expectedRevision) {
      throw new Error('Desktop Plugin output changed while its snapshot was being created.')
    }
    return snapshotRoot
  } catch (error) {
    await fs.rm(createdRoot, { recursive: true, force: true })
    throw error
  }
}

function disposeModuleRoute(route: ModuleRoute): void {
  if (!route.snapshotRoot) return
  void fs.rm(route.snapshotRoot, { recursive: true, force: true }).catch(() => {})
}

async function resolveLocalPluginRoot(rootPath: string): Promise<string> {
  if (!path.isAbsolute(rootPath)) throw new Error('Desktop Plugin root must be absolute.')
  await rejectFilesystemLink(rootPath)
  return fs.realpath(path.resolve(rootPath))
}

async function findPackagedPluginRoot(pluginId: string, roots: readonly string[]): Promise<string> {
  for (const root of roots) {
    if (!root) continue
    try {
      const candidate = path.join(root, pluginId)
      await rejectFilesystemLink(candidate)
      return await fs.realpath(candidate)
    } catch (error) {
      if (!(error instanceof Error && 'code' in error && error.code === 'ENOENT')) throw error
    }
  }
  throw new Error(`Desktop Plugin '${pluginId}' is not packaged on this client.`)
}

function appendLengthPrefixedUtf8(hash: ReturnType<typeof createHash>, value: string): void {
  const bytes = Buffer.from(value, 'utf8')
  const length = Buffer.allocUnsafe(4)
  length.writeInt32LE(bytes.length)
  hash.update(length)
  hash.update(bytes)
}

function treeEntryHeader(kind: 0 | 1, pathLength: number, contentLength: number): Buffer {
  const header = Buffer.allocUnsafe(13)
  header.writeUInt8(kind, 0)
  header.writeInt32LE(pathLength, 1)
  header.writeBigInt64LE(BigInt(contentLength), 5)
  return header
}

async function rejectFilesystemLink(targetPath: string): Promise<void> {
  if ((await fs.lstat(targetPath)).isSymbolicLink()) {
    throw new Error('Desktop Plugin output cannot contain filesystem links.')
  }
}

function distRelativePath(manifestPath: string): string {
  return manifestPath.slice(DESKTOP_OUTPUT_PREFIX.length)
}

function normalizeRouteRelativePath(value: string): string {
  if (!value || value.includes('\\')) throw new Error('Desktop Plugin route path is invalid.')
  const segments = value.split('/')
  if (segments.some((segment) => !segment || segment === '.' || segment === '..')) {
    throw new Error('Desktop Plugin route path is invalid.')
  }
  return segments.join('/')
}

function routeKey(pluginId: string, revision: string): string {
  return `${pluginId}\0${revision}`
}

function normalizePluginId(value: string): string {
  const normalized = value.trim().toLowerCase()
  return /^[a-z0-9][a-z0-9._:-]*$/.test(normalized) ? normalized : ''
}

function ordinalCompare(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function resolveCorsOrigin(request: Request): string | null {
  const origin = request.headers.get('Origin')
  if (!origin) return '*'
  if (origin === 'null') return origin
  try {
    const parsed = new URL(origin)
    if ((parsed.protocol === 'http:' || parsed.protocol === 'https:') && isLoopbackHost(parsed.hostname)) return origin
    if (parsed.protocol === 'file:') return origin
  } catch {
    return null
  }
  return null
}

function isLoopbackHost(hostname: string): boolean {
  return hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1' || hostname === '[::1]'
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

function isPathWithin(targetPath: string, rootPath: string): boolean {
  const rel = path.relative(rootPath, targetPath)
  return rel === '' || (!!rel && !rel.startsWith('..') && !path.isAbsolute(rel))
}

function mimeTypeForPath(filePath: string): string | null {
  switch (path.extname(filePath)) {
    case '.js':
    case '.mjs': return 'text/javascript; charset=utf-8'
    case '.css': return 'text/css; charset=utf-8'
    case '.json': return 'application/json; charset=utf-8'
    case '.svg': return 'image/svg+xml'
    case '.png': return 'image/png'
    case '.jpg':
    case '.jpeg': return 'image/jpeg'
    case '.webp': return 'image/webp'
    default: return null
  }
}

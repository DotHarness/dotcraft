import { protocol, net } from 'electron'
import { createReadStream, promises as fs } from 'fs'
import * as path from 'path'
import { Readable } from 'stream'
import { pathToFileURL } from 'url'
import { chatFolderContaining } from './chatFolders'
import { isDefaultChatWorkspace } from './defaultChatWorkspace'
import { mimeForExtension } from './viewerMime'
import {
  bindWindowFileSession, getWindowFileAccess, isCurrentWindowFileAccess, isWindowFileAccessValid,
  registerWindowFileAccess, requireWindowFileAccess, scopedWindowFileAccess, windowFileAccessForSession,
  type WindowFileAccess, type WindowFileAccessOptions
} from './windowFileAccess'
import { clearDesktopPluginModuleRoutes, installPluginFileProtocolHandlerForSession } from './pluginFileProtocol'

export const VIEWER_SCHEME = 'dotcraft-viewer'
const VIEWER_HOST = 'workspace'

let defaultProtocolHandlerInstalled = false
const installedSessionProtocols = new WeakSet<object>()

/** Must be called before `app.whenReady()` to mark the scheme as privileged. */
export function registerViewerScheme(): void {
  protocol.registerSchemesAsPrivileged([
    {
      scheme: VIEWER_SCHEME,
      privileges: {
        standard: true,
        secure: true,
        supportFetchAPI: true,
        bypassCSP: false,
        stream: true,
        corsEnabled: false
      }
    }
  ])
}

/** Must be called after `app.whenReady()`. */
export function installViewerProtocolHandler(): void {
  if (defaultProtocolHandlerInstalled) return
  defaultProtocolHandlerInstalled = true
  protocol.handle(VIEWER_SCHEME, () => new Response(null, { status: 403 }))
}

export function bindViewerWindow(
  win: Pick<Electron.BrowserWindow, 'webContents'>,
  options: WindowFileAccessOptions
): () => void {
  const ownerId = win.webContents.id
  const release = registerWindowFileAccess(ownerId, options)
  try {
    installViewerProtocolHandlerForSession(win.webContents.session, ownerId)
    installPluginFileProtocolHandlerForSession(win.webContents.session, ownerId)
  } catch (error) {
    release()
    throw error
  }
  return () => {
    release()
    clearDesktopPluginModuleRoutes(ownerId)
  }
}

export function installViewerProtocolHandlerForSession(targetSession: Electron.Session, ownerId: number, workspaceRoot?: string): void {
  bindWindowFileSession(targetSession, ownerId, workspaceRoot)
  const targetProtocol = targetSession.protocol
  if (installedSessionProtocols.has(targetProtocol)) return
  installedSessionProtocols.add(targetProtocol)
  targetProtocol.handle(VIEWER_SCHEME, (request) =>
    handleViewerFileRequestWithContext(request, windowFileAccessForSession(targetSession)))
}

export function handleViewerFileRequest(request: Request, ownerId?: number): Promise<Response> {
  return handleViewerFileRequestWithContext(request, getWindowFileAccess(ownerId))
}

async function handleViewerFileRequestWithContext(request: Request, context?: WindowFileAccess): Promise<Response> {
  try {
    const absPath = viewerUrlToPath(request.url)

    if (!absPath) {
      return new Response(null, { status: 400 })
    }

    const root = context?.getWorkspaceRoot()
    if (!context || !root) {
      return new Response(null, { status: 403 })
    }

    const epoch = context.authorizationEpoch
    const insideWorkspace = await isPathInsideContextRoots(absPath, context)
    const resolvedExternalPath = insideWorkspace ? null : await resolveAuthorizedExternalFile(absPath, context)
    if (!insideWorkspace && !resolvedExternalPath) {
      return new Response(null, { status: 403 })
    }

    const filePath = resolvedExternalPath ?? absPath
    const stat = await fs.stat(filePath)
    if (!stat.isFile() || !isWindowFileAccessValid(context, root, epoch)) {
      return new Response(null, { status: 403 })
    }

    const range = parseByteRange(request.headers.get('range'), stat.size)
    if (range === 'unsatisfiable') {
      return new Response(null, { status: 416, headers: { 'Content-Range': `bytes */${stat.size}` } })
    }
    if (range) {
      const body = Readable.toWeb(createReadStream(filePath, range)) as ReadableStream<Uint8Array>
      return new Response(body, {
        status: 206,
        headers: {
          'Accept-Ranges': 'bytes',
          'Content-Type': mimeForExtension(path.extname(filePath)),
          'Content-Length': String(range.end - range.start + 1),
          'Content-Range': `bytes ${range.start}-${range.end}/${stat.size}`
        }
      })
    }
    return net.fetch(pathToFileURL(filePath).toString())
  } catch {
    return new Response(null, { status: 500 })
  }
}

/** Multi-range and malformed headers yield null so the whole file is served. */
function parseByteRange(
  header: string | null,
  size: number
): { start: number; end: number } | 'unsatisfiable' | null {
  const match = header ? /^bytes=(\d*)-(\d*)$/.exec(header.trim()) : null
  if (!match || (!match[1] && !match[2])) return null
  if (!match[1]) {
    const suffix = Number(match[2])
    return suffix === 0 || size === 0 ? 'unsatisfiable' : { start: Math.max(0, size - suffix), end: size - 1 }
  }
  const start = Number(match[1])
  const end = match[2] ? Math.min(Number(match[2]), size - 1) : size - 1
  return start >= size || end < start ? 'unsatisfiable' : { start, end }
}

export function getViewerWorkspaceRoot(ownerId: number): string {
  return getWindowFileAccess(ownerId)?.getWorkspaceRoot() ?? ''
}

export function viewerRootsFor(workspaceRoot: string, targetPath: string | undefined, ownerId: number): string[] {
  const context = getWindowFileAccess(ownerId)
  if (!context || !workspaceRoot || context.getWorkspaceRoot() !== workspaceRoot) return []
  return contextRootsFor(context, targetPath)
}

function contextRootsFor(context: WindowFileAccess, targetPath?: string): string[] {
  const workspaceRoot = context.getWorkspaceRoot()
  const roots: string[] = []
  const seen = new Set<string>()
  let chatFolder: string | null = null
  if (targetPath && path.isAbsolute(targetPath) && isDefaultChatWorkspace(workspaceRoot)) {
    for (const root of context.getChatFolderRoots?.() ?? []) {
      chatFolder = chatFolderContaining(targetPath, root)
      if (chatFolder) break
    }
  }
  for (const candidate of [workspaceRoot, ...(context.getSecondaryRoots?.(workspaceRoot) ?? []), ...(chatFolder ? [chatFolder] : [])]) {
    if (!candidate) continue
    const resolved = path.resolve(candidate)
    const key = process.platform === 'win32' ? resolved.toLowerCase() : resolved
    if (seen.has(key)) continue
    seen.add(key)
    roots.push(resolved)
  }
  return roots
}

export async function isPathInsideViewerRoots(targetPath: string, workspaceRoot: string, ownerId: number): Promise<boolean> {
  for (const root of viewerRootsFor(workspaceRoot, targetPath, ownerId)) {
    if (await isPathInsideWorkspace(targetPath, root)) return true
  }
  return false
}

async function isPathInsideContextRoots(targetPath: string, context: WindowFileAccess): Promise<boolean> {
  for (const root of contextRootsFor(context, targetPath)) {
    if (await isPathInsideWorkspace(targetPath, root)) return true
  }
  return false
}

export async function resolveScopedViewerFileForAccess(
  absolutePath: string,
  workspaceRoot: string,
  ownerId: number
): Promise<string> {
  const context = scopedWindowFileAccess(ownerId, workspaceRoot)
  const epoch = context.authorizationEpoch
  if (!path.isAbsolute(absolutePath)) throw new Error('Viewer access requires an absolute file path')
  const inside = await isPathInsideContextRoots(absolutePath, context)
  const resolved = inside ? await fs.realpath(path.resolve(absolutePath)) : await resolveAuthorizedExternalFile(absolutePath, context)
  if (!resolved || !isWindowFileAccessValid(context, context.getWorkspaceRoot(), epoch)) {
    throw new Error(`Viewer access denied: ${absolutePath}`)
  }
  return resolved
}

export async function isPathInsideWorkspace(targetPath: string, workspaceRoot: string): Promise<boolean> {
  if (!workspaceRoot) return false
  try {
    const resolvedRoot = await fs.realpath(path.resolve(workspaceRoot))
    const resolvedTarget = await fs.realpath(path.resolve(targetPath))
    const rel = path.relative(resolvedRoot, resolvedTarget)
    return rel === '' || (!!rel && !rel.startsWith('..') && !path.isAbsolute(rel))
  } catch {
    return false
  }
}

export async function authorizeViewerFile(absolutePath: string, ownerId: number): Promise<string> {
  const context = requireWindowFileAccess(ownerId)
  const root = context.getWorkspaceRoot()
  const epoch = context.authorizationEpoch
  if (!root) throw new Error('Viewer access requires a selected workspace.')
  if (!path.isAbsolute(absolutePath)) {
    throw new Error('Viewer authorization requires an absolute file path')
  }
  const resolved = await fs.realpath(path.resolve(absolutePath))
  const stat = await fs.stat(resolved)
  if (!stat.isFile()) {
    throw new Error(`Not a file: ${absolutePath}`)
  }
  if (!isCurrentWindowFileAccess(context, root, epoch)) throw new Error('Viewer workspace changed.')
  context.authorizedExternalFiles.add(resolved)
  return resolved
}

async function resolveAuthorizedExternalFile(absolutePath: string, context: WindowFileAccess): Promise<string | null> {
  try {
    const resolved = await fs.realpath(path.resolve(absolutePath))
    return context.authorizedExternalFiles.has(resolved) ? resolved : null
  } catch {
    return null
  }
}

export async function resolveViewerFileForAccess(
  absolutePath: string,
  workspaceRoot: string,
  ownerId: number
): Promise<string> {
  const context = requireWindowFileAccess(ownerId)
  const epoch = context.authorizationEpoch
  if (!workspaceRoot || context.getWorkspaceRoot() !== workspaceRoot) throw new Error('Viewer workspace changed.')
  if (!path.isAbsolute(absolutePath)) {
    throw new Error('Viewer access requires an absolute file path')
  }
  if (await isPathInsideViewerRoots(absolutePath, workspaceRoot, ownerId)) {
    const resolved = await fs.realpath(path.resolve(absolutePath))
    if (!isCurrentWindowFileAccess(context, workspaceRoot, epoch)) throw new Error('Viewer workspace changed.')
    return resolved
  }
  const authorized = await resolveAuthorizedExternalFile(absolutePath, context)
  if (!authorized || !isCurrentWindowFileAccess(context, workspaceRoot, epoch)) {
    throw new Error(`Viewer access denied: ${absolutePath}`)
  }
  return authorized
}

/** The path still has to be workspace-scoped or authorized before the handler serves it. */
export function buildViewerUrl(absolutePath: string): string {
  const normalized = normalizeAbsolutePathForViewerUrl(absolutePath)
  return `${VIEWER_SCHEME}://${VIEWER_HOST}${encodeViewerPath(normalized)}`
}

export function viewerUrlToPath(viewerUrl: string): string {
  const parsed = new URL(viewerUrl)
  if (parsed.protocol !== `${VIEWER_SCHEME}:`) {
    throw new Error('Invalid viewer URL scheme')
  }
  if (parsed.hostname !== VIEWER_HOST) throw new Error('Invalid viewer URL host')
  return stripWindowsPathLeadingSlash(decodeViewerPath(parsed.pathname))
}

function normalizeAbsolutePathForViewerUrl(absolutePath: string): string {
  const normalizedSeparators = absolutePath.replace(/\\/g, '/')
  if (normalizedSeparators.startsWith('/') || /^[a-zA-Z]:\//.test(normalizedSeparators)) {
    return normalizedSeparators
  }
  return path.resolve(absolutePath).replace(/\\/g, '/')
}

function encodeViewerPath(absolutePath: string): string {
  const withLeadingSlash = absolutePath.startsWith('/') ? absolutePath : `/${absolutePath}`
  return withLeadingSlash
    .split('/')
    .map((segment) => encodeURIComponent(segment))
    .join('/')
}

function decodeViewerPath(urlPathname: string): string {
  return urlPathname
    .split('/')
    .map((segment) => decodeURIComponent(segment))
    .join('/')
}

function stripWindowsPathLeadingSlash(decodedPath: string): string {
  if (/^\/[a-zA-Z]:\//.test(decodedPath)) {
    return decodedPath.slice(1)
  }
  return decodedPath
}


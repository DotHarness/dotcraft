/**
 * Security contract for the `dotcraft-viewer://` scheme:
 *  - A workspace must be selected; when cleared, all requests return 403.
 *  - Workspace files are served by workspace boundary; external files require
 *    an explicit per-file authorization from a renderer action.
 *  - The requested path must resolve to a regular file.
 *  - Path traversal through malformed URL payloads is rejected by path decoding.
 *
 * URLs look like `dotcraft-viewer://workspace/<encoded absolute path>`; on Windows the
 * drive colon becomes its own segment, e.g. `dotcraft-viewer://workspace/E%3A/index.html`.
 */
import { protocol, net } from 'electron'
import { promises as fs } from 'fs'
import * as path from 'path'
import { pathToFileURL } from 'url'

export const VIEWER_SCHEME = 'dotcraft-viewer'
const VIEWER_HOST = 'workspace'

let currentWorkspaceRoot = ''
let defaultProtocolHandlerInstalled = false
const installedSessionProtocols = new WeakSet<object>()
const authorizedExternalFiles = new Set<string>()

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
  protocol.handle(VIEWER_SCHEME, handleViewerFileRequest)
}

export function installViewerProtocolHandlerForSession(targetSession: Electron.Session): void {
  const targetProtocol = targetSession.protocol
  if (installedSessionProtocols.has(targetProtocol)) return
  installedSessionProtocols.add(targetProtocol)
  targetProtocol.handle(VIEWER_SCHEME, handleViewerFileRequest)
}

export async function handleViewerFileRequest(request: Request): Promise<Response> {
  try {
    const absPath = viewerUrlToPath(request.url)

    if (!absPath) {
      return new Response(null, { status: 400 })
    }

    const root = currentWorkspaceRoot
    if (!root) {
      return new Response(null, { status: 403 })
    }

    const insideWorkspace = await isPathInsideWorkspace(absPath, root)
    const resolvedExternalPath = insideWorkspace ? null : await resolveAuthorizedExternalFile(absPath)
    if (!insideWorkspace && !resolvedExternalPath) {
      return new Response(null, { status: 403 })
    }

    const filePath = resolvedExternalPath ?? absPath
    const stat = await fs.stat(filePath)
    if (!stat.isFile()) {
      return new Response(null, { status: 403 })
    }

    return net.fetch(pathToFileURL(filePath).toString())
  } catch {
    return new Response(null, { status: 500 })
  }
}

/** Update the allowed workspace root. Pass '' to deny all requests. */
export function setViewerWorkspaceRoot(workspaceRoot: string): void {
  authorizedExternalFiles.clear()
  currentWorkspaceRoot = workspaceRoot
}

export function getViewerWorkspaceRoot(): string {
  return currentWorkspaceRoot
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

export async function authorizeViewerFile(absolutePath: string): Promise<string> {
  if (!path.isAbsolute(absolutePath)) {
    throw new Error('Viewer authorization requires an absolute file path')
  }
  const resolved = await fs.realpath(path.resolve(absolutePath))
  const stat = await fs.stat(resolved)
  if (!stat.isFile()) {
    throw new Error(`Not a file: ${absolutePath}`)
  }
  authorizedExternalFiles.add(resolved)
  return resolved
}

export function clearAuthorizedViewerFiles(): void {
  authorizedExternalFiles.clear()
}

async function resolveAuthorizedExternalFile(absolutePath: string): Promise<string | null> {
  try {
    const resolved = await fs.realpath(path.resolve(absolutePath))
    return authorizedExternalFiles.has(resolved) ? resolved : null
  } catch {
    return null
  }
}

/** The path still has to be workspace-scoped or authorized before the handler serves it. */
export function buildViewerUrl(absolutePath: string): string {
  const normalized = normalizeAbsolutePathForViewerUrl(absolutePath)
  return `${VIEWER_SCHEME}://${VIEWER_HOST}${encodeViewerPath(normalized)}`
}

/** Also accepts legacy URLs created before the fixed host was introduced. */
export function viewerUrlToPath(viewerUrl: string): string {
  const parsed = new URL(viewerUrl)
  if (parsed.protocol !== `${VIEWER_SCHEME}:`) {
    throw new Error('Invalid viewer URL scheme')
  }

  const decodedPath = decodeViewerPath(parsed.pathname)

  if (parsed.hostname === VIEWER_HOST || parsed.hostname === '') {
    return stripWindowsPathLeadingSlash(decodedPath)
  }

  if (/^[a-z]$/i.test(parsed.hostname)) {
    return `${parsed.hostname.toUpperCase()}:${decodedPath.startsWith('/') ? decodedPath : `/${decodedPath}`}`
  }

  throw new Error('Invalid viewer URL host')
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


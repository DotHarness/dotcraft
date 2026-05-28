import { mkdtempSync, rmSync, writeFileSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { pathToFileURL } from 'url'
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

const {
  protocolHandleMock,
  protocolRegisterSchemesAsPrivilegedMock,
  netFetchMock
} = vi.hoisted(() => ({
  protocolHandleMock: vi.fn(),
  protocolRegisterSchemesAsPrivilegedMock: vi.fn(),
  netFetchMock: vi.fn()
}))

vi.mock('electron', () => ({
  protocol: {
    registerSchemesAsPrivileged: protocolRegisterSchemesAsPrivilegedMock,
    handle: protocolHandleMock
  },
  net: {
    fetch: netFetchMock
  }
}))
import {
  VIEWER_SCHEME,
  authorizeViewerFile,
  buildViewerUrl,
  clearAuthorizedViewerFiles,
  getViewerWorkspaceRoot,
  handleViewerFileRequest,
  installViewerProtocolHandlerForSession,
  isPathInsideWorkspace,
  setViewerWorkspaceRoot,
  viewerUrlToPath
} from '../viewerFileProtocol'

const tempDirs: string[] = []

beforeEach(() => {
  setViewerWorkspaceRoot('')
  clearAuthorizedViewerFiles()
  netFetchMock.mockReset()
  netFetchMock.mockResolvedValue(new Response('ok', { status: 200 }))
})

afterEach(() => {
  for (const dir of tempDirs.splice(0)) {
    rmSync(dir, { recursive: true, force: true })
  }
})

function createTempDir(): string {
  const dir = mkdtempSync(join(tmpdir(), 'viewer-file-protocol-'))
  tempDirs.push(dir)
  return dir
}

describe('VIEWER_SCHEME', () => {
  it('is "dotcraft-viewer"', () => {
    expect(VIEWER_SCHEME).toBe('dotcraft-viewer')
  })
})

describe('setViewerWorkspaceRoot / getViewerWorkspaceRoot', () => {
  it('starts empty', () => {
    expect(getViewerWorkspaceRoot()).toBe('')
  })

  it('stores the provided path', () => {
    setViewerWorkspaceRoot('/home/user/project')
    expect(getViewerWorkspaceRoot()).toBe('/home/user/project')
  })

  it('can be cleared by passing an empty string', () => {
    setViewerWorkspaceRoot('/some/path')
    setViewerWorkspaceRoot('')
    expect(getViewerWorkspaceRoot()).toBe('')
  })

  it('replaces the previous value', () => {
    setViewerWorkspaceRoot('/first')
    setViewerWorkspaceRoot('/second')
    expect(getViewerWorkspaceRoot()).toBe('/second')
  })
})

describe('buildViewerUrl', () => {
  it('returns a dotcraft-viewer:// URL', () => {
    const url = buildViewerUrl('/home/user/project/src/main.ts')
    expect(url.startsWith(`${VIEWER_SCHEME}://workspace/`)).toBe(true)
  })

  it('uses path-like URLs so relative HTML assets can resolve', () => {
    const url = buildViewerUrl('/home/user/my project/file name.ts')
    expect(url).toContain('/home/user/my%20project/file%20name.ts')
    expect(url).not.toContain('my%20project%2Ffile')
  })

  it('normalizes Windows backslashes to forward slashes', () => {
    const url = buildViewerUrl('C:\\Users\\user\\project\\src\\index.ts')
    expect(url).not.toContain('\\')
    expect(url).toContain('/C%3A/Users/user/project/src/index.ts')
  })

  it('handles paths with special characters', () => {
    const abs = '/home/user/special chars/resume.md'
    const url = buildViewerUrl(abs)
    expect(decodeURI(url)).toContain('/home/user/special chars/resume.md')
  })
})

describe('viewerUrlToPath', () => {
  it('decodes fixed-host Windows viewer URLs without creating UNC paths', () => {
    expect(viewerUrlToPath(`${VIEWER_SCHEME}://workspace/E%3A/workspace/index.html`)).toBe(
      'E:/workspace/index.html'
    )
  })

  it('decodes legacy drive-host URLs defensively', () => {
    expect(viewerUrlToPath(`${VIEWER_SCHEME}://e/workspace/index.html`)).toBe(
      'E:/workspace/index.html'
    )
  })

  it('decodes legacy empty-host URLs', () => {
    expect(viewerUrlToPath(`${VIEWER_SCHEME}:///E:/workspace/index.html`)).toBe(
      'E:/workspace/index.html'
    )
  })
})

describe('isPathInsideWorkspace', () => {
  it('rejects missing workspace roots', async () => {
    await expect(isPathInsideWorkspace('/tmp/project/index.html', '')).resolves.toBe(false)
  })
})

describe('installViewerProtocolHandlerForSession', () => {
  it('registers the viewer protocol handler on custom sessions once', () => {
    const handle = vi.fn()
    const fakeSession = {
      protocol: { handle }
    } as unknown as Electron.Session

    installViewerProtocolHandlerForSession(fakeSession)
    installViewerProtocolHandlerForSession(fakeSession)

    expect(handle).toHaveBeenCalledTimes(1)
    expect(handle).toHaveBeenCalledWith(VIEWER_SCHEME, expect.any(Function))
  })
})

describe('handleViewerFileRequest external authorization', () => {
  it('serves workspace files without explicit authorization', async () => {
    const root = createTempDir()
    const file = join(root, 'inside.png')
    writeFileSync(file, 'inside')
    setViewerWorkspaceRoot(root)

    const response = await handleViewerFileRequest(new Request(buildViewerUrl(file)))

    expect(response.status).toBe(200)
    expect(netFetchMock).toHaveBeenCalledWith(pathToFileURL(file).toString())
  })

  it('rejects workspace-external files until they are explicitly authorized', async () => {
    const root = createTempDir()
    const externalRoot = createTempDir()
    const file = join(externalRoot, 'outside.png')
    writeFileSync(file, 'outside')
    setViewerWorkspaceRoot(root)

    const response = await handleViewerFileRequest(new Request(buildViewerUrl(file)))

    expect(response.status).toBe(403)
    expect(netFetchMock).not.toHaveBeenCalled()
  })

  it('serves an explicitly authorized external file', async () => {
    const root = createTempDir()
    const externalRoot = createTempDir()
    const file = join(externalRoot, 'outside.png')
    writeFileSync(file, 'outside')
    setViewerWorkspaceRoot(root)

    const authorizedPath = await authorizeViewerFile(file)
    const response = await handleViewerFileRequest(new Request(buildViewerUrl(file)))

    expect(response.status).toBe(200)
    expect(netFetchMock).toHaveBeenCalledWith(pathToFileURL(authorizedPath).toString())
  })

  it('does not authorize sibling files in the same external directory', async () => {
    const root = createTempDir()
    const externalRoot = createTempDir()
    const authorized = join(externalRoot, 'allowed.png')
    const sibling = join(externalRoot, 'blocked.png')
    writeFileSync(authorized, 'allowed')
    writeFileSync(sibling, 'blocked')
    setViewerWorkspaceRoot(root)
    await authorizeViewerFile(authorized)

    const response = await handleViewerFileRequest(new Request(buildViewerUrl(sibling)))

    expect(response.status).toBe(403)
    expect(netFetchMock).not.toHaveBeenCalled()
  })

  it('clears external authorization when the workspace root changes', async () => {
    const root = createTempDir()
    const nextRoot = createTempDir()
    const externalRoot = createTempDir()
    const file = join(externalRoot, 'outside.png')
    writeFileSync(file, 'outside')
    setViewerWorkspaceRoot(root)
    await authorizeViewerFile(file)
    setViewerWorkspaceRoot(nextRoot)

    const response = await handleViewerFileRequest(new Request(buildViewerUrl(file)))

    expect(response.status).toBe(403)
    expect(netFetchMock).not.toHaveBeenCalled()
  })
})

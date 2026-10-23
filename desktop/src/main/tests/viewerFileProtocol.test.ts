import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'fs'
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
  handleViewerFileRequest,
  installViewerProtocolHandlerForSession,
  isPathInsideViewerRoots,
  resolveViewerFileForAccess,
  setViewerSecondaryRootsResolver,
  setViewerWorkspaceRoot,
  viewerRootsFor,
  viewerUrlToPath
} from '../viewerFileProtocol'
import { setLocalChatsFolderRootsResolver } from '../chatFolderAccess'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'

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

describe('viewer URLs', () => {
  it('builds path-like URLs that round-trip Windows and spaced paths', () => {
    const url = buildViewerUrl('C:\\Users\\user\\my project\\file name.ts')
    expect(url).toBe(`${VIEWER_SCHEME}://workspace/C%3A/Users/user/my%20project/file%20name.ts`)
    expect(viewerUrlToPath(url)).toBe('C:/Users/user/my project/file name.ts')
  })

  it.each([
    [`${VIEWER_SCHEME}://workspace/E%3A/workspace/index.html`],
    [`${VIEWER_SCHEME}://e/workspace/index.html`],
    [`${VIEWER_SCHEME}:///E:/workspace/index.html`]
  ])('decodes %s without creating UNC paths', (url) => {
    expect(viewerUrlToPath(url)).toBe('E:/workspace/index.html')
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
  it('serves workspace files and only explicitly authorized external files until the root changes', async () => {
    const root = createTempDir()
    const externalRoot = createTempDir()
    const inside = join(root, 'inside.png')
    const file = join(externalRoot, 'outside.png')
    const sibling = join(externalRoot, 'blocked.png')
    writeFileSync(inside, 'inside')
    writeFileSync(file, 'outside')
    writeFileSync(sibling, 'blocked')
    setViewerWorkspaceRoot(root)
    const status = async (path: string) => (await handleViewerFileRequest(new Request(buildViewerUrl(path)))).status

    expect(await status(inside)).toBe(200)
    expect(netFetchMock).toHaveBeenCalledWith(pathToFileURL(inside).toString())
    expect(await status(file)).toBe(403)

    const authorizedPath = await authorizeViewerFile(file)
    expect(await status(file)).toBe(200)
    expect(netFetchMock).toHaveBeenLastCalledWith(pathToFileURL(authorizedPath).toString())
    expect(await status(sibling)).toBe(403)

    setViewerWorkspaceRoot(createTempDir())
    expect(await status(file)).toBe(403)
    expect(netFetchMock).toHaveBeenCalledTimes(2)
  })
})

describe('handleViewerFileRequest chat folders', () => {
  afterEach(() => setLocalChatsFolderRootsResolver(() => []))

  function chatFolderFixture(): { chatsRoot: string; inChat: string; inRoot: string } {
    const chatsRoot = createTempDir()
    const chatFolder = join(chatsRoot, '2026-10-11', 'plan-a-trip')
    mkdirSync(join(chatFolder, 'outputs'), { recursive: true })
    const inChat = join(chatFolder, 'outputs', 'report.png')
    const inRoot = join(chatsRoot, 'loose.png')
    writeFileSync(inChat, 'report')
    writeFileSync(inRoot, 'loose')
    setViewerWorkspaceRoot(resolveDefaultChatWorkspacePath())
    return { chatsRoot, inChat, inRoot }
  }

  it('serves a chat folder under the Chats folder root while Chats is the local foreground', async () => {
    const { chatsRoot, inChat, inRoot } = chatFolderFixture()
    setLocalChatsFolderRootsResolver(() => [chatsRoot])

    expect((await handleViewerFileRequest(new Request(buildViewerUrl(inChat)))).status).toBe(200)
    expect((await handleViewerFileRequest(new Request(buildViewerUrl(inRoot)))).status).toBe(403)
  })

  it('keeps chat folders closed when Chats is not the local foreground', async () => {
    const { chatsRoot, inChat } = chatFolderFixture()
    setLocalChatsFolderRootsResolver(() => [])
    expect((await handleViewerFileRequest(new Request(buildViewerUrl(inChat)))).status).toBe(403)

    setLocalChatsFolderRootsResolver(() => [chatsRoot])
    setViewerWorkspaceRoot(createTempDir())
    expect((await handleViewerFileRequest(new Request(buildViewerUrl(inChat)))).status).toBe(403)
  })
})

describe('resolveViewerFileForAccess', () => {
  it('treats files in attached Project folders as inside the workspace', async () => {
    const root = createTempDir()
    const secondary = createTempDir()
    const unrelated = createTempDir()
    const inSecondary = join(secondary, 'shared.png')
    const outside = join(unrelated, 'other.png')
    writeFileSync(inSecondary, 'shared')
    writeFileSync(outside, 'other')
    setViewerSecondaryRootsResolver((primary) => (primary === root ? [secondary] : []))
    try {
      setViewerWorkspaceRoot(root)
      expect(viewerRootsFor(root)).toHaveLength(2)
      await expect(isPathInsideViewerRoots(inSecondary, root)).resolves.toBe(true)
      await expect(resolveViewerFileForAccess(inSecondary, root)).resolves.toBe(realpathSync.native(inSecondary))
      await expect(resolveViewerFileForAccess(outside, root)).rejects.toThrow('denied')
      expect((await handleViewerFileRequest(new Request(buildViewerUrl(inSecondary)))).status).toBe(200)
      expect((await handleViewerFileRequest(new Request(buildViewerUrl(outside)))).status).toBe(403)
    } finally {
      setViewerSecondaryRootsResolver(() => [])
    }
  })
})

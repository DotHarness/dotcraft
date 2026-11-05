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
  bindViewerWindow,
  handleViewerFileRequest as handleViewerFileRequestForOwner,
  installViewerProtocolHandlerForSession,
  isPathInsideViewerRoots,
  resolveViewerFileForAccess,
  viewerRootsFor,
  viewerUrlToPath
} from '../viewerFileProtocol'
import { registerWindowFileAccess } from '../windowFileAccess'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'

const tempDirs: string[] = []
const OWNER = 1
let workspaceRoot = ''
let secondaryRoots: (root: string) => readonly string[] = () => []
let chatRoots: () => readonly string[] = () => []
let release: () => void
const setViewerWorkspaceRoot = (root: string) => { workspaceRoot = root }
const setViewerSecondaryRootsResolver = (resolver: typeof secondaryRoots) => { secondaryRoots = resolver }
const setLocalChatsFolderRootsResolver = (resolver: typeof chatRoots) => { chatRoots = resolver }
const handleViewerFileRequest = (request: Request) => handleViewerFileRequestForOwner(request, OWNER)

beforeEach(() => {
  workspaceRoot = ''
  secondaryRoots = () => []
  chatRoots = () => []
  release = registerWindowFileAccess(OWNER, {
    getWorkspaceRoot: () => workspaceRoot,
    getSecondaryRoots: root => secondaryRoots(root),
    getChatFolderRoots: () => chatRoots()
  })
  clearAuthorizedViewerFiles(OWNER)
  netFetchMock.mockReset()
  netFetchMock.mockResolvedValue(new Response('ok', { status: 200 }))
})

afterEach(() => {
  release()
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

    installViewerProtocolHandlerForSession(fakeSession, OWNER)
    installViewerProtocolHandlerForSession(fakeSession, OWNER)

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

    const authorizedPath = await authorizeViewerFile(file, OWNER)
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
      expect(viewerRootsFor(root, undefined, OWNER)).toHaveLength(2)
      await expect(isPathInsideViewerRoots(inSecondary, root, OWNER)).resolves.toBe(true)
      await expect(resolveViewerFileForAccess(inSecondary, root, OWNER)).resolves.toBe(realpathSync.native(inSecondary))
      await expect(resolveViewerFileForAccess(outside, root, OWNER)).rejects.toThrow('denied')
      expect((await handleViewerFileRequest(new Request(buildViewerUrl(inSecondary)))).status).toBe(200)
      expect((await handleViewerFileRequest(new Request(buildViewerUrl(outside)))).status).toBe(403)
    } finally {
      setViewerSecondaryRootsResolver(() => [])
    }
  })
})

describe('window-scoped protocol authorization', () => {
  function windowFixture(ownerId: number, root: string, secondary: string[] = [], chats: string[] = []) {
    const handlers = new Map<string, (request: Request) => Promise<Response>>()
    const targetSession = {
      protocol: { handle: (scheme: string, handler: (request: Request) => Promise<Response>) => handlers.set(scheme, handler) }
    } as unknown as Electron.Session
    const win = { webContents: { id: ownerId, session: targetSession } } as Electron.BrowserWindow
    const dispose = bindViewerWindow(win, {
      getWorkspaceRoot: () => root,
      getSecondaryRoots: () => secondary,
      getChatFolderRoots: () => chats
    })
    return { targetSession, dispose, request: (request: Request) => handlers.get(VIEWER_SCHEME)!(request) }
  }

  it('uses the bound session rather than URL, headers or a claimed webContents id', async () => {
    const firstRoot = createTempDir()
    const secondRoot = createTempDir()
    const firstFile = join(firstRoot, 'one.txt')
    const secondFile = join(secondRoot, 'two.txt')
    writeFileSync(firstFile, 'first')
    writeFileSync(secondFile, 'second')
    const first = windowFixture(11, firstRoot)
    const second = windowFixture(12, secondRoot)
    try {
      expect((await first.request(new Request(buildViewerUrl(firstFile)))).status).toBe(200)
      const forged = Object.assign(new Request(buildViewerUrl(secondFile), {
        headers: { Origin: 'file://', Referer: buildViewerUrl(secondFile) }
      }), { webContentsId: 12 })
      expect((await first.request(forged)).status).toBe(403)
      expect((await handleViewerFileRequestForOwner(forged)).status).toBe(403)
      expect((await second.request(new Request(buildViewerUrl(secondFile)))).status).toBe(200)
      first.dispose()
      expect((await first.request(new Request(buildViewerUrl(firstFile)))).status).toBe(403)
      expect((await second.request(new Request(buildViewerUrl(secondFile)))).status).toBe(200)
    } finally {
      first.dispose()
      second.dispose()
    }
  })

  it('keeps attached folders and explicit file grants within their window', async () => {
    const root = createTempDir()
    const attached = createTempDir()
    const outside = createTempDir()
    const attachedFile = join(attached, 'attached.txt')
    const outsideFile = join(outside, 'granted.txt')
    writeFileSync(attachedFile, 'attached')
    writeFileSync(outsideFile, 'outside')
    const first = windowFixture(11, root, [attached])
    const second = windowFixture(12, root)
    try {
      await authorizeViewerFile(outsideFile, 11)
      for (const file of [attachedFile, outsideFile]) {
        expect((await first.request(new Request(buildViewerUrl(file)))).status).toBe(200)
        expect((await second.request(new Request(buildViewerUrl(file)))).status).toBe(403)
      }
      expect(() => installViewerProtocolHandlerForSession(first.targetSession, 12)).toThrow('cannot be shared')
      clearAuthorizedViewerFiles(11)
      expect((await first.request(new Request(buildViewerUrl(outsideFile)))).status).toBe(403)
    } finally {
      first.dispose()
      second.dispose()
    }
  })

  it('does not restore a pending file grant after a workspace transition', async () => {
    const root = createTempDir()
    const file = join(createTempDir(), 'pending.txt')
    writeFileSync(file, 'outside')
    const first = windowFixture(11, root)
    try {
      const pending = authorizeViewerFile(file, 11)
      clearAuthorizedViewerFiles(11)
      await expect(pending).rejects.toThrow('workspace changed')
      expect((await first.request(new Request(buildViewerUrl(file)))).status).toBe(403)
    } finally { first.dispose() }
  })

  it('does not grant one window another window\'s Chats folders', async () => {
    const chatsRoot = createTempDir()
    const chat = join(chatsRoot, '2026-10-11', 'plan-a-trip')
    mkdirSync(chat, { recursive: true })
    const file = join(chat, 'report.txt')
    writeFileSync(file, 'chat')
    const first = windowFixture(11, resolveDefaultChatWorkspacePath(), [], [chatsRoot])
    const second = windowFixture(12, resolveDefaultChatWorkspacePath())
    try {
      expect((await first.request(new Request(buildViewerUrl(file)))).status).toBe(200)
      expect((await second.request(new Request(buildViewerUrl(file)))).status).toBe(403)
    } finally {
      first.dispose()
      second.dispose()
    }
  })
})

describe('handleViewerFileRequest byte ranges', () => {
  function videoFixture(): string {
    const root = createTempDir()
    const file = join(root, 'clip.mp4')
    writeFileSync(file, '0123456789')
    setViewerWorkspaceRoot(root)
    return file
  }

  const request = (file: string, range: string) =>
    handleViewerFileRequest(new Request(buildViewerUrl(file), { headers: { Range: range } }))

  it.each([
    ['bytes=2-5', '2345', 'bytes 2-5/10'],
    ['bytes=7-', '789', 'bytes 7-9/10'],
    ['bytes=-3', '789', 'bytes 7-9/10'],
    ['bytes=8-99', '89', 'bytes 8-9/10']
  ])('answers %s with partial content', async (range, body, contentRange) => {
    const response = await request(videoFixture(), range)
    expect(response.status).toBe(206)
    expect(response.headers.get('content-range')).toBe(contentRange)
    expect(response.headers.get('content-type')).toBe('video/mp4')
    expect(await response.text()).toBe(body)
    expect(netFetchMock).not.toHaveBeenCalled()
  })

  it('rejects ranges past the end and serves the whole file for unsupported range forms', async () => {
    const file = videoFixture()
    const past = await request(file, 'bytes=10-')
    expect(past.status).toBe(416)
    expect(past.headers.get('content-range')).toBe('bytes */10')
    expect((await request(file, 'bytes=0-1,4-5')).status).toBe(200)
    expect(netFetchMock).toHaveBeenCalledWith(pathToFileURL(file).toString())
  })

  it('keeps the access boundary for range requests', async () => {
    const file = videoFixture()
    const outside = join(createTempDir(), 'other.mp4')
    writeFileSync(outside, '0123456789')
    expect((await request(outside, 'bytes=0-3')).status).toBe(403)
    setViewerWorkspaceRoot('')
    expect((await request(file, 'bytes=0-3')).status).toBe(403)
  })
})

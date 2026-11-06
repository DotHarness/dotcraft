import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mkdtempSync, mkdirSync, realpathSync, rmSync, writeFileSync } from 'node:fs'
import { readFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const electron = vi.hoisted(() => ({ fromPartition: vi.fn() }))
vi.mock('electron', () => ({
  app: { getName: () => 'DotCraft', getPreferredSystemLanguages: () => ['en'], getLocale: () => 'en' },
  session: { fromPartition: electron.fromPartition },
  protocol: { handle: vi.fn() },
  net: { fetch: async (url: string) => new Response(await readFile(fileURLToPath(url))) }
}))

import { BrowserSessionPolicy } from '../browserSessionPolicy'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import {
  authorizeViewerFile, buildViewerUrl, handleViewerFileRequest,
  installViewerProtocolHandlerForSession, resolveScopedViewerFileForAccess, resolveViewerFileForAccess
} from '../viewerFileProtocol'
import {
  invalidateForegroundFileAccess, registerWindowFileAccess, revokeAllWindowFileAccess
} from '../windowFileAccess'

function sessionFixture() {
  const handlers = new Map<string, (request: Request) => Promise<Response>>()
  return {
    handlers,
    protocol: { handle: (scheme: string, handler: (request: Request) => Promise<Response>) => handlers.set(scheme, handler) },
    getUserAgent: () => 'Mozilla/5.0 Chrome/148 Safari/537.36', setUserAgent: vi.fn(),
    on: vi.fn(), setPermissionCheckHandler: vi.fn(), setPermissionRequestHandler: vi.fn(),
    webRequest: { onBeforeSendHeaders: vi.fn(), onBeforeRequest: vi.fn() }
  }
}

describe('retained local-preview authority', () => {
  let directory: string
  let foreground: string
  let firstRoot: string
  let secondRoot: string
  let release: () => void
  const ownerId = 7201
  const sessions = new Map<string, ReturnType<typeof sessionFixture>>()
  const page = (root: string) => join(root, 'index.html')
  const external = (name: string) => join(directory, name)
  const window = { webContents: { id: ownerId } } as Electron.BrowserWindow

  beforeEach(() => {
    directory = mkdtempSync(join(tmpdir(), 'dotcraft-preview-authority-'))
    firstRoot = join(directory, 'a')
    secondRoot = join(directory, 'b')
    for (const root of [firstRoot, secondRoot]) {
      mkdirSync(root)
      writeFileSync(page(root), root)
    }
    writeFileSync(external('first.txt'), 'first grant')
    writeFileSync(external('second.txt'), 'second grant')
    foreground = firstRoot
    release = registerWindowFileAccess(ownerId, { getWorkspaceRoot: () => foreground })
    sessions.clear()
    electron.fromPartition.mockImplementation((partition: string) => {
      let value = sessions.get(partition)
      if (!value) sessions.set(partition, value = sessionFixture())
      return value
    })
  })

  afterEach(() => {
    release()
    rmSync(directory, { recursive: true, force: true })
  })

  function preview(policy: BrowserSessionPolicy, root: string) {
    const selected = policy.select(window, root, buildViewerUrl(page(root)))
    const session = sessions.get(selected.partition)!
    return {
      partition: selected.partition,
      session,
      request: (file: string) => session.handlers.get('dotcraft-viewer')!(new Request(buildViewerUrl(file)))
    }
  }

  it('keeps both previews bound to their own roots and exact grants through project switches', async () => {
    const policy = new BrowserSessionPolicy(() => {})
    await authorizeViewerFile(external('first.txt'), ownerId)
    const first = preview(policy, firstRoot)
    invalidateForegroundFileAccess(ownerId)
    foreground = secondRoot
    const second = preview(policy, secondRoot)
    await authorizeViewerFile(external('second.txt'), ownerId)

    expect(first.partition).not.toBe(second.partition)
    expect((await first.request(page(firstRoot))).status).toBe(200)
    expect((await first.request(external('first.txt'))).status).toBe(200)
    expect((await first.request(page(secondRoot))).status).toBe(403)
    expect((await first.request(external('second.txt'))).status).toBe(403)
    expect((await second.request(page(secondRoot))).status).toBe(200)
    expect((await second.request(external('second.txt'))).status).toBe(200)
    expect((await second.request(page(firstRoot))).status).toBe(403)
    expect((await second.request(external('first.txt'))).status).toBe(403)
    expect((await handleViewerFileRequest(new Request(buildViewerUrl(page(firstRoot))), ownerId)).status).toBe(403)
    await expect(resolveViewerFileForAccess(page(firstRoot), firstRoot, ownerId)).rejects.toThrow('workspace changed')

    invalidateForegroundFileAccess(ownerId)
    foreground = firstRoot
    expect((await handleViewerFileRequest(new Request(buildViewerUrl(external('first.txt'))), ownerId)).status).toBe(200)
    expect((await second.request(external('second.txt'))).status).toBe(200)
  })

  it('creates its first Chats preview in the background with only the owning workspace chat roots', async () => {
    const chatsWorkspace = resolveDefaultChatWorkspacePath()
    const chatsRoot = join(directory, 'chats')
    const foreignChatsRoot = join(directory, 'foreign-chats')
    const report = (root: string) => join(root, '2026-11-06', 'trip', 'report.html')
    for (const root of [chatsRoot, foreignChatsRoot]) {
      mkdirSync(join(root, '2026-11-06', 'trip'), { recursive: true })
      writeFileSync(report(root), root)
    }
    release()
    foreground = secondRoot
    const resolveChatRoots = (root?: string) => root === chatsWorkspace ? [chatsRoot] : [foreignChatsRoot]
    release = registerWindowFileAccess(ownerId, {
      getWorkspaceRoot: () => foreground,
      getChatFolderRoots: resolveChatRoots
    })
    const policy = new BrowserSessionPolicy(() => {})
    const first = preview(policy, chatsWorkspace)

    expect((await first.request(report(chatsRoot))).status).toBe(200)
    expect((await first.request(report(foreignChatsRoot))).status).toBe(403)
    expect((await first.request(page(secondRoot))).status).toBe(403)
    await expect(resolveScopedViewerFileForAccess(report(chatsRoot), chatsWorkspace, ownerId)).resolves.toBe(realpathSync.native(report(chatsRoot)))
    expect((await handleViewerFileRequest(new Request(buildViewerUrl(report(chatsRoot))), ownerId)).status).toBe(403)
  })

  it('does not admit an old foreground grant after an A/B/A transition', async () => {
    const pending = authorizeViewerFile(external('first.txt'), ownerId)
    invalidateForegroundFileAccess(ownerId)
    foreground = secondRoot
    invalidateForegroundFileAccess(ownerId)
    foreground = firstRoot
    await expect(pending).rejects.toThrow('workspace changed')
    await expect(resolveScopedViewerFileForAccess(external('first.txt'), firstRoot, ownerId)).rejects.toThrow('denied')
  })

  it('validates snapshot paths without granting access and revokes every retained scope explicitly', async () => {
    const policy = new BrowserSessionPolicy(() => {})
    const first = preview(policy, firstRoot)
    await authorizeViewerFile(external('first.txt'), ownerId)
    invalidateForegroundFileAccess(ownerId)
    foreground = secondRoot
    const second = preview(policy, secondRoot)
    await authorizeViewerFile(external('second.txt'), ownerId)

    await expect(resolveScopedViewerFileForAccess(page(firstRoot), firstRoot, ownerId)).resolves.toBe(realpathSync.native(page(firstRoot)))
    await expect(resolveScopedViewerFileForAccess(page(secondRoot), firstRoot, ownerId)).rejects.toThrow('denied')
    await expect(resolveScopedViewerFileForAccess(external('second.txt'), firstRoot, ownerId)).rejects.toThrow('denied')
    await expect(resolveScopedViewerFileForAccess('relative.txt', firstRoot, ownerId)).rejects.toThrow('absolute')
    revokeAllWindowFileAccess(ownerId)
    expect((await first.request(external('first.txt'))).status).toBe(403)
    expect((await second.request(external('second.txt'))).status).toBe(403)
  })

  it('does not carry scoped grants across windows, session rebinding, or a new registration', async () => {
    const policy = new BrowserSessionPolicy(() => {})
    await authorizeViewerFile(external('first.txt'), ownerId)
    const first = preview(policy, firstRoot)
    const releaseOther = registerWindowFileAccess(ownerId + 1, { getWorkspaceRoot: () => firstRoot })
    try {
      await expect(resolveScopedViewerFileForAccess(external('first.txt'), firstRoot, ownerId + 1)).rejects.toThrow('denied')
      expect(() => installViewerProtocolHandlerForSession(first.session as unknown as Electron.Session, ownerId + 1, firstRoot)).toThrow('cannot be shared')
      expect(() => installViewerProtocolHandlerForSession(first.session as unknown as Electron.Session, ownerId, secondRoot)).toThrow('cannot be shared')
      release()
      expect((await first.request(page(firstRoot))).status).toBe(403)
      release = registerWindowFileAccess(ownerId, { getWorkspaceRoot: () => firstRoot })
      await expect(resolveScopedViewerFileForAccess(external('first.txt'), firstRoot, ownerId)).rejects.toThrow('denied')
      expect((await first.request(page(firstRoot))).status).toBe(403)
    } finally {
      releaseOther()
    }
  })
})

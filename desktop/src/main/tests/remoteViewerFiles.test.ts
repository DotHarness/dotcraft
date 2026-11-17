import { afterEach, describe, expect, it, vi } from 'vitest'
import type { WindowIpcContext } from '../ipcBridgeContracts'
import type { DesktopAppServerClient, NotificationCallback } from '../DesktopAppServerClient'

vi.mock('electron', () => ({
  app: { getPath: () => '', getName: () => 'DotCraft', getPreferredSystemLanguages: () => ['en'], getLocale: () => 'en' },
  protocol: { handle: vi.fn(), registerSchemesAsPrivileged: vi.fn() },
  net: { fetch: vi.fn() }
}))
import { registerWorkspaceFileHandlers } from '../ipcWorkspaceFiles'
import { buildViewerUrl, handleViewerFileRequest } from '../viewerFileProtocol'
import { remoteViewerFilesFor } from '../remoteViewerFiles'
import { registerWindowFileAccess } from '../windowFileAccess'

interface HostFile { bytes: Buffer; mtime: number }

function remoteHost(files: Record<string, string>, directories: Record<string, Array<{ fileName: string; isDirectory: boolean }>> = {}) {
  const disk = new Map<string, HostFile>(Object.entries(files).map(([key, text], index) => [key, { bytes: Buffer.from(text), mtime: 1000 + index }]))
  const listeners: NotificationCallback[] = []
  const sendRequest = vi.fn(async (method: string, params: Record<string, unknown>) => {
    const target = params.path as string
    const file = disk.get(target)
    switch (method) {
      case 'fs/getMetadata':
        if (!file && !directories[target]) throw new Error('FileNotFound')
        return { isFile: !!file, isDirectory: !file, isSymlink: false, createdAtMs: 0, modifiedAtMs: file?.mtime ?? 0 }
      case 'fs/readFile':
        if (!file) throw new Error('FileNotFound')
        return { dataBase64: file.bytes.toString('base64') }
      case 'fs/writeFile':
        disk.set(target, { bytes: Buffer.from(params.dataBase64 as string, 'base64'), mtime: (file?.mtime ?? 0) + 1 })
        return {}
      case 'fs/readDirectory':
        return { entries: (directories[target] ?? []).map((entry) => ({ ...entry, isFile: !entry.isDirectory })) }
      case 'fs/watch':
      case 'fs/unwatch':
        return {}
      default:
        throw new Error(`Unexpected ${method}`)
    }
  })
  const client = {
    connectionId: 'remote-a',
    isRemoteHost: true,
    sendRequest,
    onNotification: (callback: NotificationCallback) => { listeners.push(callback); return () => {} }
  } as unknown as DesktopAppServerClient
  const notify = (method: string, params: unknown) => { for (const listener of listeners) listener(method, params) }
  return { client, disk, sendRequest, notify }
}

let ownerSeed = 9100
const releases: Array<() => void> = []
afterEach(() => { while (releases.length) releases.pop()!() })

function handlersFor(client: DesktopAppServerClient) {
  const handlers = new Map<string, (event: unknown, params: unknown) => Promise<unknown>>()
  const context = {
    workspacePath: 'C:\\local\\project',
    owner: { webContents: { id: ++ownerSeed } },
    getWireClient: () => client,
    callbacks: { getSettings: () => ({ locale: 'en' }), getConnectionContext: () => ({ workspacePath: '/srv/app' }) },
    handleSafe: (channel: string, fn: never) => handlers.set(channel, fn)
  } as unknown as WindowIpcContext
  registerWorkspaceFileHandlers(context)
  return (channel: string, params: unknown, event: unknown = {}) => handlers.get(channel)!(event, params)
}

describe('remote viewer files', () => {
  it('opens host files through the connection and only previews text, images and PDFs', async () => {
    const host = remoteHost({ '/srv/app/main.cs': 'class A {}\r\n', '/srv/app/data.xlsx': 'PK' })
    const invoke = handlersFor(host.client)

    expect(await invoke('workspace:viewer:authorize-file', { absolutePath: '/srv/app/main.cs', workspacePath: '/srv/app' }))
      .toEqual({ absolutePath: '/srv/app/main.cs' })
    expect(await invoke('workspace:viewer:classify', { absolutePath: '/srv/app/main.cs' }))
      .toMatchObject({ contentClass: 'text', sizeBytes: 12, mtimeMs: 1000 })
    expect(await invoke('workspace:viewer:classify', { absolutePath: '/srv/app/data.xlsx' }))
      .toMatchObject({ contentClass: 'unsupported' })
    expect(await invoke('workspace:viewer:read-text', { absolutePath: '/srv/app/main.cs' }))
      .toMatchObject({ text: 'class A {}\r\n', lineEnding: 'crlf', mtimeMs: 1000, truncated: false })
  })

  it('saves only when the host still reports the opened modification time', async () => {
    const host = remoteHost({ '/srv/app/notes.md': 'old' })
    const invoke = handlersFor(host.client)
    const write = { absolutePath: '/srv/app/notes.md', text: 'a\nb', hasUtf8Bom: false, lineEnding: 'crlf' }

    expect(await invoke('workspace:viewer:write-text', { ...write, expectedMtimeMs: 1 }))
      .toMatchObject({ outcome: 'conflict', current: { text: 'old', mtimeMs: 1000 } })
    expect(host.disk.get('/srv/app/notes.md')!.bytes.toString()).toBe('old')

    expect(await invoke('workspace:viewer:write-text', { ...write, expectedMtimeMs: 1000 }))
      .toEqual({ outcome: 'saved', mtimeMs: 1001, sizeBytes: 4 })
    expect(host.disk.get('/srv/app/notes.md')!.bytes.toString()).toBe('a\r\nb')
  })

  it('lists host directories relative to the tree root', async () => {
    const host = remoteHost({}, {
      '/srv/app/src': [
        { fileName: 'b.ts', isDirectory: false },
        { fileName: '.git', isDirectory: true },
        { fileName: 'lib', isDirectory: true },
        { fileName: 'A.ts', isDirectory: false }
      ]
    })
    const invoke = handlersFor(host.client)
    const result = await invoke('workspace:viewer:list-dir', { dirPath: '/srv/app/src', rootPath: '/srv/app' }) as { entries: unknown[] }
    expect(result.entries).toEqual([
      { name: 'lib', relativePath: 'src/lib', absolutePath: '/srv/app/src/lib', isDir: true },
      { name: 'A.ts', relativePath: 'src/A.ts', absolutePath: '/srv/app/src/A.ts', isDir: false },
      { name: 'b.ts', relativePath: 'src/b.ts', absolutePath: '/srv/app/src/b.ts', isDir: false }
    ])
  })

  it('reports host changes to the watching window until it unwatches', async () => {
    const host = remoteHost({ '/srv/app/main.cs': 'v1' })
    const invoke = handlersFor(host.client)
    const send = vi.fn()
    const sender = { id: 77, send, once: vi.fn(), removeListener: vi.fn(), isDestroyed: () => false }
    const { subscriptionId } = await invoke('workspace:viewer:watch-text', { absolutePath: '/srv/app/main.cs' }, { sender }) as { subscriptionId: string }
    expect(host.sendRequest).toHaveBeenCalledWith('fs/watch', { watchId: subscriptionId, path: '/srv/app/main.cs' })

    host.disk.set('/srv/app/main.cs', { bytes: Buffer.from('v22'), mtime: 2000 })
    host.notify('fs/changed', { watchId: subscriptionId, changedPaths: ['/srv/app/main.cs'] })
    await vi.waitFor(() => expect(send).toHaveBeenCalledWith('workspace:viewer:text-changed', {
      subscriptionId, absolutePath: '/srv/app/main.cs', mtimeMs: 2000, sizeBytes: 3
    }))

    await invoke('workspace:viewer:unwatch-text', { subscriptionId }, { sender })
    expect(host.sendRequest).toHaveBeenCalledWith('fs/unwatch', { watchId: subscriptionId })
  })

  it('serves host bytes for viewer URLs, including byte ranges', async () => {
    const host = remoteHost({ '/srv/app/shot.png': '0123456789' })
    const owner = ++ownerSeed
    releases.push(registerWindowFileAccess(owner, { getWorkspaceRoot: () => '', getRemoteFiles: () => remoteViewerFilesFor(host.client) }))
    const url = buildViewerUrl('/srv/app/shot.png')

    const whole = await handleViewerFileRequest(new Request(url), owner)
    expect(whole.status).toBe(200)
    expect(whole.headers.get('content-type')).toBe('image/png')
    expect(await whole.text()).toBe('0123456789')

    const partial = await handleViewerFileRequest(new Request(url, { headers: { range: 'bytes=2-4' } }), owner)
    expect(partial.status).toBe(206)
    expect(await partial.text()).toBe('234')
  })
})

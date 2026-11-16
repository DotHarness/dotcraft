import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mkdtemp, open, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import type { WindowIpcContext } from '../ipcBridgeContracts'
import type { DesktopAppServerClient } from '../DesktopAppServerClient'
import { MAX_ATTACHMENT_BYTES } from '../../shared/attachments'

const temporary = vi.hoisted(() => ({ root: '' }))
vi.mock('electron', () => ({
  app: { getPath: () => temporary.root, getName: () => 'DotCraft', getPreferredSystemLanguages: () => ['en'], getLocale: () => 'en' },
  protocol: { handle: vi.fn() }
}))
import { WorkspaceAttachmentAccess } from '../workspaceAttachments'
import { registerWorkspaceFileHandlers } from '../ipcWorkspaceFiles'

beforeEach(async () => { temporary.root = await mkdtemp(join(tmpdir(), 'dotcraft-attachments-')) })
afterEach(async () => { await rm(temporary.root, { recursive: true, force: true }) })

function fixture() {
  const bytes = Buffer.from([0x89, 0x50, 0x4e, 0x47])
  const client = {
    connectionId: 'connection-a', isRemoteHost: true,
    sendRequest: vi.fn(async (_method: string, _params: unknown) => ({ dataBase64: bytes.toString('base64') }))
  }
  const otherClient = { ...client, connectionId: 'connection-other', isRemoteHost: false }
  let currentClient = client
  const handlers = new Map<string, (_event: unknown, params: unknown) => Promise<unknown>>()
  const context = {
    workspacePath: '/host/project', owner: { webContents: { id: 7501 } }, getWireClient: () => currentClient,
    callbacks: {
      getConnectionClient: (key: string) => key === 'host-a' ? client as unknown as DesktopAppServerClient : null,
      getConnectionContext: (selected: typeof client) => ({ workspacePath: selected === client ? '/host/project' : join(temporary.root, 'other-project') }),
      getSettings: () => ({ locale: 'en' })
    },
    handleSafe: (channel: string, fn: never) => handlers.set(channel, fn)
  } as unknown as WindowIpcContext
  const access = new WorkspaceAttachmentAccess(context)
  registerWorkspaceFileHandlers(context)
  return { access, client, bytes, handlers, switchWorkspace: () => { currentClient = otherClient } }
}

describe('connection-owned image attachments', () => {
  it('stages remote paste bytes on the client and reads them without a remote request', async () => {
    const { access, client, bytes } = fixture()
    const saved = await access.saveImage({ dataUrl: `data:image/png;base64,${bytes.toString('base64')}`, fileName: 'paste.png' })
    expect(saved.path.startsWith(join(temporary.root, 'dotcraft-client-attachments'))).toBe(true)
    expect(await readFile(saved.path)).toEqual(bytes)
    expect((await access.readImage({ path: saved.path })).dataUrl).toBe(`data:image/png;base64,${bytes.toString('base64')}`)
    expect(client.sendRequest).not.toHaveBeenCalled()
  })

  it('keeps staging on the source captured before the foreground changed', async () => {
    const { bytes, handlers, switchWorkspace } = fixture()
    const source = { connectionKey: 'host-a', connectionId: 'connection-a' }
    switchWorkspace()
    const result = await handlers.get('workspace:save-image-to-temp')!({}, {
      dataUrl: `data:image/png;base64,${bytes.toString('base64')}`, fileName: 'captured.png', source
    }) as { path: string }
    expect(result.path.startsWith(join(temporary.root, 'dotcraft-client-attachments'))).toBe(true)
    expect(await readFile(result.path)).toEqual(bytes)
  })

  it('reads an image from its captured host and refuses another workspace or stale lifetime', async () => {
    const { access, client } = fixture()
    const source = { connectionKey: 'host-a', connectionId: 'connection-a' }
    const path = '/host/project/.craft/attachments/images/photo.png'
    await expect(access.readImage({ path, source })).resolves.toEqual({ dataUrl: 'data:image/png;base64,iVBORw==' })
    expect(client.sendRequest).toHaveBeenCalledWith('fs/readFile', { path })
    await expect(access.readImage({ path: '/other/.craft/attachments/images/photo.png', source })).rejects.toThrow('outside')
    client.connectionId = 'connection-b'
    await expect(access.readImage({ path, source })).rejects.toThrow('no longer active')
  })

  it('rejects a response from a connection lifetime that ended while reading', async () => {
    const { access, client } = fixture()
    let finish!: (value: { dataBase64: string }) => void
    const requested = Promise.withResolvers<void>()
    client.sendRequest.mockImplementation(() => { requested.resolve(); return new Promise(resolve => { finish = resolve }) })
    const operation = access.readImage({ path: '/host/project/.craft/attachments/images/photo.png',
      source: { connectionKey: 'host-a', connectionId: 'connection-a' } })
    await requested.promise
    client.connectionId = 'reconnected'
    finish({ dataBase64: 'iVBORw==' })
    await expect(operation).rejects.toThrow('no longer active')
  })
})

describe('client-local attachment reads', () => {
  it('returns metadata and complete bytes, including empty files, without a server connection', async () => {
    const access = new WorkspaceAttachmentAccess({} as WindowIpcContext)
    const path = join(temporary.root, 'file.bin')
    await writeFile(path, Buffer.from([1, 2, 3, 4, 5]))
    expect(await access.statLocalFile({ path })).toEqual({ sizeBytes: 5 })
    expect(await access.readLocalFile({ path })).toEqual({ dataBase64: 'AQIDBAU=' })
    await writeFile(path, '')
    expect(await access.readLocalFile({ path })).toEqual({ dataBase64: '' })
  })

  it('reports an oversized file size for localized preflight but refuses to read its contents', async () => {
    const access = new WorkspaceAttachmentAccess({} as WindowIpcContext)
    const path = join(temporary.root, 'large.bin')
    const file = await open(path, 'w')
    await file.truncate(MAX_ATTACHMENT_BYTES + 1)
    await file.close()
    expect(await access.statLocalFile({ path })).toEqual({ sizeBytes: MAX_ATTACHMENT_BYTES + 1 })
    await expect(access.readLocalFile({ path })).rejects.toThrow('limit')
  })
})

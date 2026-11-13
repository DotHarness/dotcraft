import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import type { IpcMain } from 'electron'
import type { ClassifyResult, ReadTextResult } from '../../shared/viewer/types'
import type { WindowIpcContext } from '../ipcBridgeContracts'

vi.mock('electron', () => ({
  app: { getName: () => 'DotCraft', getPreferredSystemLanguages: () => ['en'], getLocale: () => 'en' },
  protocol: { handle: vi.fn() }
}))

import { registerWorkspaceFileHandlers } from '../ipcWorkspaceFiles'
import { ThreadPanelStateStore } from '../threadPanelStateStore'
import { registerWindowFileAccess } from '../windowFileAccess'
import { prepareFileViewer } from '../../renderer/utils/fileViewer'

type Handler = Parameters<IpcMain['handle']>[1]
const releases: Array<() => void> = []
let directory: string
let project: string
let otherProject: string
let outside: string

function windowFiles(ownerId: number, getRoot: () => string) {
  releases.push(registerWindowFileAccess(ownerId, { getWorkspaceRoot: getRoot }))
  const handlers = new Map<string, Handler>()
  const bind = () => registerWorkspaceFileHandlers({
    owner: { webContents: { id: ownerId } },
    workspacePath: getRoot(),
    callbacks: { getSettings: () => ({ locale: 'en' }) },
    handleSafe: (channel, listener) => { handlers.set(channel, listener) }
  } as WindowIpcContext)
  bind()
  const invoke = <T>(channel: string, params: unknown): Promise<T> =>
    Promise.resolve(handlers.get(channel)!({} as Electron.IpcMainInvokeEvent, params))
  const viewer = {
    authorizeFile: (params: { absolutePath: string; workspacePath: string }) =>
      invoke<{ absolutePath: string }>('workspace:viewer:authorize-file', params),
    classify: (params: { absolutePath: string }) => invoke<ClassifyResult>('workspace:viewer:classify', params),
    readText: (params: { absolutePath: string }) => invoke<ReadTextResult>('workspace:viewer:read-text', params)
  }
  return { viewer, bind }
}

beforeEach(() => {
  directory = mkdtempSync(join(tmpdir(), 'dotcraft-file-reopen-'))
  project = join(directory, 'project')
  otherProject = join(directory, 'other-project')
  mkdirSync(project)
  mkdirSync(otherProject)
  outside = join(directory, 'outside.md')
  writeFileSync(outside, '# Reopened file')
})
afterEach(() => {
  for (const release of releases.splice(0)) release()
  vi.unstubAllGlobals()
  rmSync(directory, { recursive: true, force: true })
})

describe('file viewer reopening through the host file boundary', () => {
  it('reopens a saved external file in a new window without replaying historical grants', async () => {
    const first = windowFiles(8001, () => project)
    vi.stubGlobal('window', { api: { workspace: { viewer: first.viewer } } })
    const prepared = await prepareFileViewer(outside, project)
    expect((await first.viewer.readText(prepared)).text).toBe('# Reopened file')
    const statePath = join(directory, 'panels.json')
    const scope = { projectKey: project, threadId: 'thread-a' }
    new ThreadPanelStateStore(statePath).update(scope, {
      version: 1,
      tabs: [{ id: 'file-a', kind: 'file', label: 'outside.md', relativePath: outside, ...prepared }],
      systemTabs: [], activeTab: { kind: 'viewer', id: 'file-a' }, visible: true, fullView: false, width: 600
    })
    releases.pop()!()

    const restored = new ThreadPanelStateStore(statePath).read(scope)!.tabs[0]
    if (restored.kind !== 'file') throw new Error('Expected a restored file route')
    const second = windowFiles(8002, () => project)
    vi.stubGlobal('window', { api: { workspace: { viewer: second.viewer } } })
    await expect(second.viewer.readText(restored)).rejects.toThrow('Viewer access denied')
    const reopened = await prepareFileViewer(restored.absolutePath, project)
    expect(reopened).toMatchObject({ absolutePath: realpathSync.native(outside), contentClass: 'text', sizeBytes: 15 })
    expect((await second.viewer.readText(reopened)).text).toBe('# Reopened file')

    const sibling = join(directory, 'not-opened.md')
    writeFileSync(sibling, 'not authorized')
    await expect(second.viewer.readText({ absolutePath: sibling })).rejects.toThrow('Viewer access denied')
    const third = windowFiles(8003, () => project)
    await expect(third.viewer.readText(reopened)).rejects.toThrow('Viewer access denied')
  })

  it('rejects a queued reopen for a different foreground project without granting it access', async () => {
    let foreground = project
    const files = windowFiles(8001, () => foreground)
    vi.stubGlobal('window', { api: { workspace: { viewer: files.viewer } } })
    foreground = otherProject
    await expect(prepareFileViewer(outside, project)).rejects.toThrow('Viewer workspace changed')
    files.bind()
    await expect(prepareFileViewer(outside, project)).rejects.toThrow('Viewer workspace changed')
    await expect(files.viewer.readText({ absolutePath: outside })).rejects.toThrow('Viewer access denied')

    const reopened = await prepareFileViewer(outside, otherProject)
    expect((await files.viewer.readText(reopened)).text).toBe('# Reopened file')
    foreground = project
    files.bind()
    await expect(files.viewer.readText(reopened)).rejects.toThrow('Viewer access denied')
  })

})

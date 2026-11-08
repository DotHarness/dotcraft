import { describe, expect, it, vi } from 'vitest'
import type { IpcMainInvokeEvent } from 'electron'
import type { WindowIpcContext } from '../ipcBridgeContracts'
import { registerWorkspaceHandlers } from '../ipcWorkspace'

vi.mock('electron', () => ({ app: {}, dialog: {} }))
vi.mock('../chatFoldersIpc', () => ({ registerChatFoldersHandlers: vi.fn() }))
vi.mock('../linkPreviews', () => ({ registerLinkPreviewHandlers: vi.fn() }))

describe('workspace project action routing', () => {
  it('preserves target connection identity for archive and disconnect without requiring the foreground project', async () => {
    const handlers = new Map<string, Parameters<WindowIpcContext['handleSafe']>[1]>()
    const archiveThreadInWorkspace = vi.fn().mockResolvedValue(undefined)
    const onDisconnectRemoteProject = vi.fn().mockResolvedValue(undefined)
    registerWorkspaceHandlers({
      handleSafe: (channel: string, handler: Parameters<WindowIpcContext['handleSafe']>[1]) => { handlers.set(channel, handler) },
      callbacks: { archiveThreadInWorkspace, onDisconnectRemoteProject }
    } as unknown as WindowIpcContext)
    const event = {} as IpcMainInvokeEvent
    const key = 'remote:device:computer-a:project-a'

    await handlers.get('workspace:archive-thread')!(event, { workspacePath: '/same/path', threadId: 'shared-id', connectionKey: key })
    await handlers.get('workspace:disconnect-remote')!(event, key)

    expect(archiveThreadInWorkspace).toHaveBeenCalledWith('/same/path', 'shared-id', key)
    expect(onDisconnectRemoteProject).toHaveBeenCalledWith(key)
  })
})

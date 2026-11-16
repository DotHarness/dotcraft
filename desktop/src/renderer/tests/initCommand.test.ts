import { afterEach, describe, expect, it, vi } from 'vitest'
import { expandInitCommand } from '../utils/initCommand'
import { captureComposerAttachmentTarget } from '../utils/prepareComposerAttachments'
import { registerThreadConnection, releaseThreadConnection } from '../stores/threadConnectionStore'
import { useConnectionStore } from '../stores/connectionStore'
import { installDesktopApiMock } from './desktopApiMock'

afterEach(() => {
  releaseThreadConnection('init-thread')
  useConnectionStore.getState().reset()
  vi.unstubAllGlobals()
})

describe('initialization command routing', () => {
  it('expands on the captured owning connection even after foreground navigation', async () => {
    vi.stubGlobal('window', {})
    const sendConnectionRequest = vi.fn(async () => ({ handled: true, expandedPrompt: 'Initialize this workspace' }))
    const sendRequest = vi.fn()
    installDesktopApiMock({ appServer: { sendConnectionRequest, sendRequest } })
    registerThreadConnection('init-thread', {
      threadId: 'init-thread', workspaceKey: 'host-A', connectionKey: 'host-A', connectionId: 'A-1', capabilities: {}
    })
    const target = captureComposerAttachmentTarget('init-thread')
    useConnectionStore.setState({ connectionKey: 'host-B', connectionId: 'B-1' })
    await expect(expandInitCommand('init-thread', target.request)).resolves.toBe('Initialize this workspace')
    expect(sendConnectionRequest).toHaveBeenCalledWith('host-A', 'command/execute', {
      threadId: 'init-thread', command: '/init', arguments: []
    }, undefined, { connectionId: 'A-1' })
    expect(sendRequest).not.toHaveBeenCalled()
  })
})

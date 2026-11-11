import { describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'

vi.mock('../browserUseManager', () => ({ browserUseManager: {} }))
vi.mock('../computerUse/runtime', () => ({ computerUseManager: {} }))
vi.mock('../defaultChatWorkspace', () => ({ isDefaultChatWorkspace: () => false }))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: {} }))
vi.mock('../ipcBridge', () => ({ broadcastNotification: vi.fn() }))
vi.mock('../ripgrepRuntime', () => ({ resolveDotCraftRuntimeTools: vi.fn() }))
vi.mock('../voice/voiceIpc', () => ({ refreshVoiceChatGptAvailability: vi.fn() }))
vi.mock('../workspaceSetup', () => ({ getWorkspaceStatus: vi.fn() }))
vi.mock('../nodeReplManager', () => ({ nodeReplManager: {} }))
vi.mock('../threadPanelStateIpc', () => ({ forgetThreadPanelState: vi.fn() }))
import { startLocalProjectConnection } from '../windows/windowSecondaryConnections'

function fixture() {
  let ready!: () => void
  const pending = new Promise<{ endpoints: { appServerWebSocket: string } }>((resolve) => {
    ready = () => resolve({ endpoints: { appServerWebSocket: 'ws://project' } })
  })
  const hub = { ensureAppServer: vi.fn(() => pending) }
  const ctx = {
    app: { createHubClient: () => hub, localProjects: { has: () => true } },
    runtimeIsStopping: () => false, isWorkspaceForeground: () => false,
    getWorkspaceConnection: vi.fn(() => undefined),
    getManagedAppServerEndpoint: (response: { endpoints: { appServerWebSocket: string } }) => response.endpoints.appServerWebSocket,
    createSecondaryWorkspaceConnection: vi.fn(() => ({ lastUsedAt: 0 })),
    disposeWorkspaceConnection: vi.fn()
  } as unknown as DesktopWindowRuntime
  return { ctx, hub, ready }
}

describe('starting a recent project connection', () => {
  it('ensures a saved project without foregrounding it or recording an open', async () => {
    const { ctx, hub, ready } = fixture()
    const starting = startLocalProjectConnection(ctx, '/project')
    expect(hub.ensureAppServer).toHaveBeenCalledExactlyOnceWith('/project', expect.any(Object))
    expect(ctx.createSecondaryWorkspaceConnection).not.toHaveBeenCalled()
    ready()
    await starting
    expect(ctx.createSecondaryWorkspaceConnection).toHaveBeenCalledExactlyOnceWith('/project', 'ws://project')
  })

  it('reuses a warm connection without ensuring or disposing its server', async () => {
    const { ctx, hub } = fixture()
    ctx.getWorkspaceConnection = () => ({ connected: true }) as never
    await startLocalProjectConnection(ctx, '/project')
    expect(hub.ensureAppServer).not.toHaveBeenCalled()
    expect(ctx.disposeWorkspaceConnection).not.toHaveBeenCalled()
  })

  it.each(['shutdown', 'remote-switch', 'foreground', 'removed', 'already-connected'])(
    'ignores a late start result after %s', async (reason) => {
      const { ctx, ready } = fixture()
      let allowed = true
      const starting = startLocalProjectConnection(ctx, '/project', () => allowed)
      if (reason === 'shutdown') ctx.runtimeIsStopping = () => true
      if (reason === 'remote-switch') allowed = false
      if (reason === 'foreground') ctx.isWorkspaceForeground = () => true
      if (reason === 'removed') ctx.app.localProjects.has = () => false
      if (reason === 'already-connected') ctx.getWorkspaceConnection = () => ({ connected: true }) as never
      ready()
      await starting
      expect(ctx.createSecondaryWorkspaceConnection).not.toHaveBeenCalled()
      expect(ctx.disposeWorkspaceConnection).not.toHaveBeenCalled()
    }
  )
})

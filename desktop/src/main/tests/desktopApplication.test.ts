import { afterEach, expect, it, vi } from 'vitest'
import { DesktopApplication } from '../desktopApplication'
import type { HubEvent, HubProject } from '../desktopHub'

const fixture = vi.hoisted(() => ({
  hub: {
    listProjects: vi.fn(),
    subscribeEvents: vi.fn()
  }
}))

vi.mock('electron', () => ({
  app: { getPath: () => '/unused', isPackaged: false },
  nativeTheme: { shouldUseDarkColors: false }
}))
vi.mock('../AppServerManager', () => ({}))
vi.mock('../appUpdate', () => ({}))
vi.mock('../browserUseManager', () => ({ browserUseManager: { setPolicyHost: vi.fn() } }))
vi.mock('../computerUse/runtime', () => ({
  setComputerUseRuntimeHost: vi.fn(), disposeComputerUse: vi.fn()
}))
vi.mock('../desktopHub', () => ({
  createDesktopHubClient: () => fixture.hub,
  DesktopHubError: class extends Error {}
}))
vi.mock('../desktopMenu', () => ({
  registerDesktopApplicationIpc: vi.fn(), refreshDesktopMenu: vi.fn()
}))
vi.mock('../desktopWindowManager', () => ({
  DesktopWindowManager: class {
    windows = new Map()
    routeLaunch = async () => {}
    dispose = async () => {}
  }
}))
vi.mock('../mobile/mobileHubBridge', () => ({ disposeMobileHubBridge: vi.fn() }))
vi.mock('../nativeThemeSource', () => ({ applyNativeThemeSource: vi.fn() }))
vi.mock('../nodeReplManager', () => ({ nodeReplManager: { disposeAll: vi.fn() } }))
vi.mock('../satellites/satellitesHubBridge', () => ({ disposeSatellitesHubBridge: vi.fn() }))
vi.mock('../screenView/screenViewManager', () => ({ closeAllScreenViews: vi.fn() }))
vi.mock('../settings', () => ({
  loadSettings: () => ({ connectionMode: 'remote' }), saveSettings: vi.fn()
}))
vi.mock('../threadTasks/threadTasksIpc', () => ({ createDesktopThreadTasks: vi.fn() }))
vi.mock('../trayManager', () => ({
  createDesktopTray: () => ({ refresh: async () => {}, dispose: vi.fn() })
}))
vi.mock('../voice/voiceIpc', () => ({
  refreshVoiceChatGptAvailability: vi.fn(), shutdownVoiceService: vi.fn()
}))
vi.mock('../whatsNewCatalog', () => ({ WhatsNewCatalog: class {} }))
vi.mock('../whatsNewMediaCache', () => ({}))
vi.mock('../windowAttentionNotifier', () => ({ createWindowAttentionNotifier: vi.fn() }))
vi.mock('../windowTheme', () => ({}))

let application: DesktopApplication | undefined
afterEach(async () => {
  await application?.dispose()
  application = undefined
  vi.restoreAllMocks()
})

it('loads and watches local projects when the saved foreground connection is remote', async () => {
  const project: HubProject = {
    path: '/work/one', displayName: 'one', addedAt: '2026-11-05T00:00:00Z',
    lastOpenedAt: '2026-11-05T00:00:00Z', running: false
  }
  let projects = [project]
  let emit!: (event: HubEvent) => void
  let subscribed!: () => void
  const watching = new Promise<void>((resolve) => { subscribed = resolve })
  fixture.hub.listProjects.mockImplementation(async () => projects)
  fixture.hub.subscribeEvents.mockImplementation((listener, signal: AbortSignal) => {
    emit = listener
    subscribed()
    return new Promise<void>((resolve) => signal.addEventListener('abort', () => resolve(), { once: true }))
  })

  application = new DesktopApplication()
  await application.start([])
  await watching
  expect(application.localProjects.list()).toEqual([project])

  let refreshed!: () => void
  const changed = new Promise<void>((resolve) => { refreshed = resolve })
  const window = {
    emitWorkspaceProjects: () => refreshed(),
    runtimeIsStopping: () => true
  }
  application.windows.set(1, window as never)
  projects = []
  emit({ kind: 'projects.changed', at: '2026-11-05T00:01:00Z' })
  await changed
  expect(application.localProjects.list()).toEqual([])
  expect(fixture.hub.listProjects).toHaveBeenCalledTimes(2)
})

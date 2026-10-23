import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { HubEvent } from '../desktopHub'
import type { WorkspaceWindowState } from '../desktopActivation'
import type { AppSettings } from '../settings'
import type { WorkspaceLockStatus } from '../workspaceLock'
import { existsSync } from 'fs'
import {
  ensureTrayProcess,
  openDesktopWindow,
  parseHubNotificationPayload,
  resolveNotificationIconPath,
  resolveTrayIconPath,
  runTrayProcess,
  showHubNotification,
  showHubNotificationForSettings,
  spawnDesktopWindow,
  trayWorkspaceName
} from '../trayManager'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import { translate } from '../../shared/locales'

const childProcessMocks = vi.hoisted(() => ({
  spawn: vi.fn<(
    command: string,
    args: string[],
    options: Record<string, unknown>
  ) => { unref: () => void }>(() => ({ unref: vi.fn() }))
}))

const workspaceLockMocks = vi.hoisted(() => ({
  checkWorkspaceLock: vi.fn<() => WorkspaceLockStatus>(() => ({ locked: false }))
}))

const settingsMocks = vi.hoisted(() => ({
  loadSettings: vi.fn<() => AppSettings>(() => ({}))
}))

const trayLockMocks = vi.hoisted(() => {
  const release = vi.fn()
  return {
    release,
    tryAcquireTrayLock: vi.fn(async () => ({ release })),
    requestTrayShutdown: vi.fn(async () => false)
  }
})

const hubClientMocks = vi.hoisted(() => {
  const getStatus = vi.fn(async () => ({}))
  const listAppServers = vi.fn(async (): Promise<unknown[]> => [])
  const listSatellites = vi.fn(async (): Promise<unknown[]> => [])
  const subscribeEvents = vi.fn(async () => {})
  const shutdownHub = vi.fn(async () => {})
  const restartAppServer = vi.fn(async () => {})
  const stopAppServer = vi.fn(async () => {})
  const createDesktopHubClient = vi.fn().mockImplementation(() => ({
    getStatus,
    listAppServers,
    listSatellites,
    subscribeEvents,
    shutdownHub,
    restartAppServer,
    stopAppServer
  }))
  return {
    createDesktopHubClient,
    getStatus,
    listAppServers,
    listSatellites,
    subscribeEvents,
    shutdownHub,
    restartAppServer,
    stopAppServer
  }
})

const recentThreadCatalogMocks = vi.hoisted(() => ({
  refresh: vi.fn(async (): Promise<unknown[]> => []),
  dispose: vi.fn()
}))

const activationMocks = vi.hoisted(() => ({
  requestWorkspaceActivation: vi.fn<() => Promise<boolean>>(async () => false),
  requestWorkspaceWindowState: vi.fn<() => Promise<WorkspaceWindowState | null>>(async () => null)
}))

const desktopActivationLockMocks = vi.hoisted(() => ({
  getDesktopActivationEndpoint: vi.fn((): unknown => null)
}))

const desktopProcessRegistryMocks = vi.hoisted(() => ({
  quitRegisteredDesktopProcesses: vi.fn(async () => ({ requested: 0, acknowledged: 0, failed: 0 }))
}))

const electronMocks = vi.hoisted(() => {
  const show = vi.fn()
  let notificationClickHandler: (() => void) | null = null
  let trayClickHandler: (() => void) | null = null
  const beforeQuitHandlers: Array<() => void> = []
  const notificationOn = vi.fn((event: string, handler: () => void) => {
    if (event === 'click') notificationClickHandler = handler
  })
  const trayOn = vi.fn((event: string, handler: () => void) => {
    if (event === 'click') trayClickHandler = handler
  })
  const appOn = vi.fn((event: string, handler: () => void) => {
    if (event === 'before-quit') beforeQuitHandlers.push(handler)
  })
  const appQuit = vi.fn()
  const openExternal = vi.fn()
  const setToolTip = vi.fn()
  const setContextMenu = vi.fn()
  const destroy = vi.fn()
  const nativeTheme = { themeSource: 'system' as 'system' | 'light' | 'dark' }
  return {
    show,
    notificationOn,
    trayOn,
    appOn,
    appQuit,
    setToolTip,
    setContextMenu,
    destroy,
    nativeTheme,
    triggerClick: () => {
      notificationClickHandler?.()
    },
    triggerTrayClick: () => {
      trayClickHandler?.()
    },
    triggerBeforeQuit: () => {
      for (const handler of beforeQuitHandlers.splice(0)) handler()
    },
    resetHandlers: () => {
      notificationClickHandler = null
      trayClickHandler = null
      beforeQuitHandlers.splice(0)
      nativeTheme.themeSource = 'system'
    },
    openExternal,
    Notification: vi.fn().mockImplementation(() => ({ show, on: notificationOn }))
  }
})

vi.mock('electron', () => ({
  app: {
    isPackaged: false,
    resourcesPath: 'resources',
    quit: electronMocks.appQuit,
    on: electronMocks.appOn
  },
  Menu: { buildFromTemplate: vi.fn((template) => ({ template })) },
  nativeTheme: electronMocks.nativeTheme,
  nativeImage: {
    createFromPath: vi.fn(() => ({
      setTemplateImage: vi.fn()
    })),
    createEmpty: vi.fn(() => ({}))
  },
  Notification: Object.assign(electronMocks.Notification, { isSupported: vi.fn(() => true) }),
  shell: { openExternal: electronMocks.openExternal },
  Tray: vi.fn().mockImplementation(() => ({
    on: electronMocks.trayOn,
    setToolTip: electronMocks.setToolTip,
    setContextMenu: electronMocks.setContextMenu,
    destroy: electronMocks.destroy
  }))
}))

vi.mock('child_process', () => childProcessMocks)

vi.mock('../workspaceLock', () => workspaceLockMocks)

vi.mock('../settings', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../settings')>()
  return {
    ...actual,
    loadSettings: settingsMocks.loadSettings
  }
})

vi.mock('../trayLock', () => trayLockMocks)

vi.mock('../desktopHub', () => ({ createDesktopHubClient: hubClientMocks.createDesktopHubClient }))

vi.mock('../trayRecentThreads', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../trayRecentThreads')>()
  return {
    ...actual,
    TrayRecentThreadCatalog: vi.fn().mockImplementation(() => recentThreadCatalogMocks)
  }
})

vi.mock('../desktopActivation', () => activationMocks)

vi.mock('../desktopActivationLock', () => desktopActivationLockMocks)

vi.mock('../desktopProcessRegistry', () => desktopProcessRegistryMocks)

vi.mock('fs', () => ({
  existsSync: vi.fn(() => true)
}))

beforeEach(() => {
  settingsMocks.loadSettings.mockReturnValue({})
  trayLockMocks.tryAcquireTrayLock.mockResolvedValue({ release: trayLockMocks.release })
  trayLockMocks.requestTrayShutdown.mockResolvedValue(false)
  hubClientMocks.getStatus.mockResolvedValue({})
  hubClientMocks.listAppServers.mockResolvedValue([])
  hubClientMocks.listSatellites.mockResolvedValue([])
  hubClientMocks.subscribeEvents.mockResolvedValue(undefined)
  hubClientMocks.shutdownHub.mockResolvedValue(undefined)
  desktopProcessRegistryMocks.quitRegisteredDesktopProcesses.mockResolvedValue({
    requested: 0,
    acknowledged: 0,
    failed: 0
  })
  recentThreadCatalogMocks.refresh.mockResolvedValue([])
})

afterEach(() => {
  electronMocks.triggerBeforeQuit()
  electronMocks.resetHandlers()
})

const lockedWorkspace: WorkspaceLockStatus = {
  locked: true,
  pid: 123,
  activation: { host: '127.0.0.1', port: 456, token: 'token', protocolVersion: 1 }
}

function notificationEvent(data: Record<string, unknown>): HubEvent {
  return {
    kind: 'notification.requested',
    at: new Date().toISOString(),
    workspacePath: 'F:/examples/workspace',
    data
  }
}

function windowState(focused: boolean): WorkspaceWindowState {
  return { ok: true, focused, visible: true, minimized: false }
}

const notificationIcon = expect.stringMatching(/[\\/]icon\.png$/)

describe('trayManager icon resolution', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(existsSync).mockImplementation(() => true)
  })

  it('picks the platform tray icon, falls back to the shared PNG, and prefers the PNG for notifications', () => {
    expect(resolveTrayIconPath('win32')).toContain('icon.ico')
    expect(resolveTrayIconPath('darwin')).toContain('icon-macTemplate.png')
    expect(resolveNotificationIconPath('win32')).toMatch(/[\\/]icon\.png$/)

    vi.mocked(existsSync).mockImplementation((path) => String(path).endsWith('icon.png'))
    expect(resolveTrayIconPath('win32')).toContain('icon.png')
  })
})

describe('trayManager notifications', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue({ locked: false })
    activationMocks.requestWorkspaceActivation.mockResolvedValue(false)
    activationMocks.requestWorkspaceWindowState.mockResolvedValue(null)
    desktopActivationLockMocks.getDesktopActivationEndpoint.mockReturnValue(null)
  })

  it('lists the default Chat workspace AppServer as Chats rather than its folder', () => {
    expect(trayWorkspaceName(resolveDefaultChatWorkspacePath(), 'zh-Hans')).toBe(translate('zh-Hans', 'chatsRail.title'))
    expect(trayWorkspaceName('F:/examples/workspace', 'zh-Hans')).toBe('workspace')
  })

  it('parses Hub notification events and localizes keyed text', () => {
    expect(parseHubNotificationPayload(notificationEvent({
      title: 'Done',
      body: 'Finished',
      threadId: 'thread_1',
      openDesktopOnClick: true
    }))).toMatchObject({
      workspacePath: 'F:/examples/workspace',
      threadId: 'thread_1',
      title: 'Done',
      body: 'Finished',
      openDesktopOnClick: true
    })

    expect(parseHubNotificationPayload(notificationEvent({
      titleKey: 'hub.notification.turn_completed.title',
      fallbackTitle: 'DotCraft task completed',
      bodyKey: 'hub.notification.turn_completed.body',
      fallbackBody: '"Current chat" finished.',
      params: { name: 'Current chat' }
    }), 'zh-Hans')).toMatchObject({
      title: 'DotCraft 任务已完成',
      body: '“Current chat” 已完成。'
    })
  })

  it.each([
    ['turnCompleted', 'never', true, false, false],
    ['turnFailed', 'never', false, false, false],
    ['turnCompleted', 'always', true, true, false],
    ['turnCompleted', 'whenUnfocused', true, false, true],
    ['turnCompleted', 'whenUnfocused', false, true, true],
    ['turnCompleted', 'whenUnfocused', null, true, true],
    ['custom', 'never', true, true, false]
  ] as const)('%s with %s mode and focused=%s shows=%s', async (kind, taskCompletionMode, focused, shows, queriesWindow) => {
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue(lockedWorkspace)
    activationMocks.requestWorkspaceWindowState.mockResolvedValue(focused === null ? null : windowState(focused))

    const shown = await showHubNotificationForSettings(
      notificationEvent({ kind, title: 'Done', body: 'Finished' }),
      { notifications: { taskCompletionMode } }
    )

    expect(shown).toBe(shows)
    if (shows) {
      expect(electronMocks.Notification).toHaveBeenCalledWith({ title: 'Done', body: 'Finished', icon: notificationIcon })
      expect(electronMocks.show).toHaveBeenCalled()
    } else {
      expect(electronMocks.Notification).not.toHaveBeenCalled()
    }
    if (queriesWindow) {
      expect(activationMocks.requestWorkspaceWindowState).toHaveBeenCalledWith(
        expect.objectContaining({ port: 456, token: 'token' }),
        'F:/examples/workspace'
      )
    } else {
      expect(activationMocks.requestWorkspaceWindowState).not.toHaveBeenCalled()
    }
  })

  it.each([
    ['approvalRequested', 'hub.notification.approval_requested.body', 'Needs your approval', { approvalRequests: false }],
    ['inputRequested', 'hub.notification.input_requested.body', 'Has a question for you', { questions: false }]
  ])('shows %s notifications only while the workspace is unfocused and the kind is enabled', async (kind, bodyKey, body, disabled) => {
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue(lockedWorkspace)
    const event = notificationEvent({ kind, title: 'Fix login', bodyKey, fallbackBody: body, params: { name: 'Fix login' } })
    const settings: AppSettings = { notifications: { taskCompletionMode: 'never' } }

    activationMocks.requestWorkspaceWindowState.mockResolvedValue(windowState(true))
    expect(await showHubNotificationForSettings(event, settings)).toBe(false)
    expect(electronMocks.Notification).not.toHaveBeenCalled()

    activationMocks.requestWorkspaceWindowState.mockResolvedValue(windowState(false))
    expect(await showHubNotificationForSettings(event, { notifications: disabled })).toBe(false)
    expect(electronMocks.Notification).not.toHaveBeenCalled()

    expect(await showHubNotificationForSettings(event, settings)).toBe(true)
    expect(electronMocks.Notification).toHaveBeenCalledOnce()
    expect(electronMocks.Notification).toHaveBeenCalledWith({ title: 'Fix login', body, icon: notificationIcon })
  })

  it('does not launch Desktop when notification click disables Desktop opening', async () => {
    showHubNotification(notificationEvent({ title: 'Done', body: 'Finished', openDesktopOnClick: false }))

    electronMocks.triggerClick()
    await Promise.resolve()

    expect(childProcessMocks.spawn).not.toHaveBeenCalled()
    expect(electronMocks.openExternal).not.toHaveBeenCalled()
  })

  it('activates an existing Desktop workspace for dotcraft workspace links', async () => {
    activationMocks.requestWorkspaceActivation.mockResolvedValue(true)
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue(lockedWorkspace)
    showHubNotification(notificationEvent({
      title: 'Done',
      body: 'Finished',
      actionUrl: 'dotcraft://workspace/open?path=F%3A%2Fexamples%2Fworkspace&threadId=thread_1',
      openDesktopOnClick: true
    }))

    electronMocks.triggerClick()
    await vi.waitFor(() => expect(activationMocks.requestWorkspaceActivation).toHaveBeenCalledWith(
      expect.objectContaining({ port: 456, token: 'token' }),
      { workspacePath: 'F:/examples/workspace', threadId: 'thread_1' }
    ), { interval: 1 })
    expect(childProcessMocks.spawn).not.toHaveBeenCalled()
  })
})

describe('trayManager process launches', () => {
  const originalPlatform = process.platform
  const originalArgv = process.argv

  beforeEach(() => {
    vi.clearAllMocks()
    desktopActivationLockMocks.getDesktopActivationEndpoint.mockReturnValue(null)
  })

  afterEach(() => {
    Object.defineProperty(process, 'platform', { value: originalPlatform })
    process.argv = originalArgv
  })

  const spawnCall = (index: number) => {
    const [, args, options] = childProcessMocks.spawn.mock.calls[index]
    return { args, options }
  }

  it('launches visible Desktop windows for a workspace, a thread deep link, or no workspace', () => {
    process.argv = ['electron', 'main.js', '--no-workspace']

    spawnDesktopWindow('E:/examples/workspace')
    spawnDesktopWindow('E:/examples/workspace', 'thread 1')
    spawnDesktopWindow()

    const workspace = spawnCall(0)
    expect(workspace.args).toEqual(expect.arrayContaining(['--workspace', 'E:/examples/workspace']))
    expect(workspace.args).not.toContain('--no-workspace')
    expect(workspace.options).toEqual({ detached: true, stdio: 'ignore' })

    const deepLink = spawnCall(1)
    expect(deepLink.args).toContain('dotcraft://workspace/open?path=E%3A%2Fexamples%2Fworkspace&threadId=thread+1')
    expect(deepLink.args).not.toContain('--no-workspace')

    const empty = spawnCall(2)
    expect(empty.args).not.toContain('--workspace')
    expect(empty.args).not.toContain('--tray')
    expect(empty.args).toContain('--no-workspace')
  })

  it('keeps the background tray process hidden and skips it on macOS when the menu bar item is off', () => {
    ensureTrayProcess({})

    expect(spawnCall(0).args).toContain('--tray')
    expect(spawnCall(0).options).toEqual({ detached: true, stdio: 'ignore', windowsHide: true })

    Object.defineProperty(process, 'platform', { value: 'darwin' })
    ensureTrayProcess({ showInMenuBar: false })
    expect(childProcessMocks.spawn).toHaveBeenCalledOnce()
  })

  it('reuses the existing macOS Desktop window before spawning a new process', async () => {
    Object.defineProperty(process, 'platform', { value: 'darwin' })
    activationMocks.requestWorkspaceActivation.mockResolvedValue(true)
    desktopActivationLockMocks.getDesktopActivationEndpoint.mockReturnValue({
      host: '127.0.0.1',
      port: 456,
      token: 'desktop-token',
      protocolVersion: 1
    })

    await openDesktopWindow('E:/examples/workspace')

    expect(activationMocks.requestWorkspaceActivation).toHaveBeenCalledWith(
      expect.objectContaining({ port: 456, token: 'desktop-token' }),
      { workspacePath: 'E:/examples/workspace', threadId: null }
    )
    expect(childProcessMocks.spawn).not.toHaveBeenCalled()
  })
})

describe('trayManager Hub refresh', () => {
  const appServer = {
    workspacePath: 'E:/examples/workspace',
    canonicalWorkspacePath: 'E:/examples/workspace',
    state: 'running',
    endpoints: {},
    serviceStatus: {}
  }

  const lastMenuTemplate = (): Array<Record<string, unknown>> => {
    const calls = electronMocks.setContextMenu.mock.calls
    return (calls[calls.length - 1]?.[0] as { template: Array<Record<string, unknown>> }).template
  }

  const workspaceSubmenu = (): Array<Record<string, unknown>> => {
    const entry = lastMenuTemplate().find((item) => item.label === 'Workspace')
    return (entry?.submenu ?? []) as Array<Record<string, unknown>>
  }

  const clickMenuItem = (label: string) => {
    const item = lastMenuTemplate().find((entry) => entry.label === label)
    ;(item?.click as (() => void) | undefined)?.()
  }

  beforeEach(() => {
    vi.clearAllMocks()
    hubClientMocks.listAppServers.mockResolvedValue([appServer])
  })

  it('keeps the workspace submenu when a Hub without the satellite surface rejects', async () => {
    hubClientMocks.listSatellites.mockRejectedValue(new Error('HTTP 404 Not Found'))

    await runTrayProcess()

    expect(workspaceSubmenu()[0]?.label).toContain('workspace')
  })

  it('still builds a menu when every Hub call fails', async () => {
    hubClientMocks.getStatus.mockRejectedValue(new Error('hub down'))
    hubClientMocks.listAppServers.mockRejectedValue(new Error('hub down'))
    hubClientMocks.listSatellites.mockRejectedValue(new Error('hub down'))

    await runTrayProcess()

    expect(workspaceSubmenu()[0]?.label).toBe('No managed workspaces')
    expect(hubClientMocks.subscribeEvents).not.toHaveBeenCalled()
  })

  it('quits every Desktop process before shutting down Hub and the tray', async () => {
    await runTrayProcess()

    clickMenuItem('Exit')

    await vi.waitFor(() => expect(electronMocks.appQuit).toHaveBeenCalledOnce(), { interval: 1 })
    expect(desktopProcessRegistryMocks.quitRegisteredDesktopProcesses).toHaveBeenCalledOnce()
    expect(hubClientMocks.shutdownHub).toHaveBeenCalledOnce()
    expect(
      desktopProcessRegistryMocks.quitRegisteredDesktopProcesses.mock.invocationCallOrder[0]
    ).toBeLessThan(hubClientMocks.shutdownHub.mock.invocationCallOrder[0]!)
    expect(hubClientMocks.shutdownHub.mock.invocationCallOrder[0]).toBeLessThan(
      electronMocks.appQuit.mock.invocationCallOrder[0]!
    )
  })

  it('still shuts down Hub when a Desktop process cannot be notified', async () => {
    desktopProcessRegistryMocks.quitRegisteredDesktopProcesses.mockRejectedValueOnce(
      new Error('registry unavailable')
    )
    await runTrayProcess()

    clickMenuItem('Exit')

    await vi.waitFor(() => expect(electronMocks.appQuit).toHaveBeenCalledOnce(), { interval: 1 })
    expect(hubClientMocks.shutdownHub).toHaveBeenCalledOnce()
  })

  it('shows three recent threads directly, puts overflow in More, and opens them through workspace activation', async () => {
    recentThreadCatalogMocks.refresh.mockResolvedValue([
      { id: 'thread-1', displayName: 'First', lastActiveAt: '2026-09-22T09:00:00Z', workspacePath: 'F:/work/one', workspaceName: 'one' },
      { id: 'thread-2', displayName: 'Second', lastActiveAt: '2026-09-22T08:00:00Z', workspacePath: 'F:/work/two', workspaceName: 'two' },
      { id: 'thread-3', displayName: null, lastActiveAt: '2026-09-22T07:00:00Z', workspacePath: 'F:/work/three', workspaceName: 'three' },
      { id: 'thread-4', displayName: 'Fourth', lastActiveAt: '2026-09-22T06:00:00Z', workspacePath: 'F:/work/four', workspaceName: 'four' }
    ])
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue(lockedWorkspace)
    activationMocks.requestWorkspaceActivation.mockResolvedValue(true)

    await runTrayProcess()

    expect(lastMenuTemplate().slice(0, 5)).toMatchObject([
      { label: 'Recent', enabled: false },
      { label: 'First', sublabel: 'one' },
      { label: 'Second', sublabel: 'two' },
      { label: 'New conversation', sublabel: 'three' },
      { label: 'More', submenu: [{ label: 'Fourth', sublabel: 'four' }] }
    ])

    clickMenuItem('First')
    await vi.waitFor(() => expect(activationMocks.requestWorkspaceActivation).toHaveBeenCalledWith(
      expect.objectContaining({ port: 456, token: 'token' }),
      { workspacePath: 'F:/work/one', threadId: 'thread-1' }
    ), { interval: 1 })
    expect(childProcessMocks.spawn).not.toHaveBeenCalled()
  })

  it.each([
    [[{ peerId: 'sat_qa', online: false }], undefined],
    [[{ peerId: 'sat_studio', online: true }], '1 satellite online'],
    [[
      { peerId: 'sat_studio', online: true },
      { peerId: 'sat_art', online: true },
      { peerId: 'sat_qa', online: false }
    ], '2 satellites online']
  ])('summarizes online satellites in one disabled line (%#)', async (satellites, label) => {
    hubClientMocks.listSatellites.mockResolvedValue(satellites)

    await runTrayProcess()

    const line = lastMenuTemplate().find((item) => String(item.label ?? '').includes('satellite'))
    if (label) expect(line).toMatchObject({ label, enabled: false })
    else expect(line).toBeUndefined()
  })
})

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { HubEvent } from '../desktopHub'
import type { WorkspaceWindowState } from '../desktopActivation'
import type { AppSettings } from '../settings'
import type { WorkspaceLockStatus } from '../workspaceLock'

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
  const listAppServers = vi.fn(async () => [])
  const subscribeEvents = vi.fn(async () => {})
  const shutdownHub = vi.fn(async () => {})
  const restartAppServer = vi.fn(async () => {})
  const stopAppServer = vi.fn(async () => {})
  const createDesktopHubClient = vi.fn().mockImplementation(() => ({
    getStatus,
    listAppServers,
    subscribeEvents,
    shutdownHub,
    restartAppServer,
    stopAppServer
  }))
  return {
    createDesktopHubClient,
    getStatus,
    listAppServers,
    subscribeEvents,
    shutdownHub,
    restartAppServer,
    stopAppServer
  }
})

const activationMocks = vi.hoisted(() => ({
  requestWorkspaceActivation: vi.fn<() => Promise<boolean>>(async () => false),
  requestWorkspaceWindowState: vi.fn<() => Promise<WorkspaceWindowState | null>>(async () => null)
}))

const desktopActivationLockMocks = vi.hoisted(() => ({
  getDesktopActivationEndpoint: vi.fn(() => null)
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

vi.mock('../desktopActivation', () => activationMocks)

vi.mock('../desktopActivationLock', () => desktopActivationLockMocks)

vi.mock('fs', () => ({
  existsSync: vi.fn(() => true)
}))

beforeEach(() => {
  settingsMocks.loadSettings.mockReturnValue({})
  trayLockMocks.tryAcquireTrayLock.mockResolvedValue({ release: trayLockMocks.release })
  trayLockMocks.requestTrayShutdown.mockResolvedValue(false)
  hubClientMocks.getStatus.mockResolvedValue({})
  hubClientMocks.listAppServers.mockResolvedValue([])
  hubClientMocks.subscribeEvents.mockResolvedValue(undefined)
  hubClientMocks.shutdownHub.mockResolvedValue(undefined)
})

afterEach(() => {
  electronMocks.triggerBeforeQuit()
  electronMocks.resetHandlers()
})

describe('trayManager icon resolution', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('uses the Windows app icon for the tray', async () => {
    const { existsSync } = await import('fs')
    const { resolveTrayIconPath } = await import('../trayManager')

    const path = resolveTrayIconPath('win32')

    expect(path).toContain('icon.ico')
    expect(existsSync).toHaveBeenCalledWith(expect.stringContaining('icon.ico'))
  })

  it('uses the mac template version of the app icon for the tray', async () => {
    const { existsSync } = await import('fs')
    const { resolveTrayIconPath } = await import('../trayManager')

    const path = resolveTrayIconPath('darwin')

    expect(path).toContain('icon-macTemplate.png')
    expect(existsSync).toHaveBeenCalledWith(expect.stringContaining('icon-macTemplate.png'))
  })

  it('falls back to the shared PNG when the Windows ICO is missing', async () => {
    const { existsSync } = await import('fs')
    vi.mocked(existsSync).mockImplementation((path) => String(path).endsWith('icon.png'))
    const { resolveTrayIconPath } = await import('../trayManager')

    const path = resolveTrayIconPath('win32')

    expect(path).toContain('icon.png')
    expect(existsSync).toHaveBeenCalledWith(expect.stringContaining('icon.ico'))
    expect(existsSync).toHaveBeenCalledWith(expect.stringContaining('icon.png'))
  })

  it('prefers the full app icon for notification assets', async () => {
    const { existsSync } = await import('fs')
    const { resolveNotificationIconPath } = await import('../trayManager')

    const path = resolveNotificationIconPath('win32')

    expect(path).toMatch(/[\\/]icon\.png$/)
    expect(existsSync).toHaveBeenCalledWith(expect.stringMatching(/[\\/]icon\.png$/))
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

  it('parses Hub notification events', async () => {
    const { parseHubNotificationPayload } = await import('../trayManager')
    const event: HubEvent = {
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: {
        title: 'Done',
        body: 'Finished',
        threadId: 'thread_1',
        openDesktopOnClick: true
      }
    }

    expect(parseHubNotificationPayload(event)).toMatchObject({
      workspacePath: 'F:/examples/workspace',
      threadId: 'thread_1',
      title: 'Done',
      body: 'Finished',
      openDesktopOnClick: true
    })
  })

  it('localizes Hub notification keys with fallback text', async () => {
    const { parseHubNotificationPayload } = await import('../trayManager')
    const event: HubEvent = {
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: {
        titleKey: 'hub.notification.turn_completed.title',
        fallbackTitle: 'DotCraft task completed',
        bodyKey: 'hub.notification.turn_completed.body',
        fallbackBody: '"Current chat" finished.',
        params: { name: 'Current chat' }
      }
    }

    expect(parseHubNotificationPayload(event, 'zh-Hans')).toMatchObject({
      title: 'DotCraft 任务已完成',
      body: '“Current chat” 已完成。'
    })
  })

  it('shows supported notification events', async () => {
    const { showHubNotification } = await import('../trayManager')
    const shown = showHubNotification({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: { title: 'Done', body: 'Finished' }
    })

    expect(shown).toBe(true)
    expect(electronMocks.Notification).toHaveBeenCalledWith({
      title: 'Done',
      body: 'Finished',
      icon: expect.stringMatching(/[\\/]icon\.png$/)
    })
    expect(electronMocks.show).toHaveBeenCalled()
  })

  it('suppresses turn result notifications when task completion notifications are disabled', async () => {
    const { showHubNotificationForSettings } = await import('../trayManager')

    const completed = await showHubNotificationForSettings({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: { kind: 'turnCompleted', title: 'Done', body: 'Finished' }
    }, {
      notifications: { taskCompletionMode: 'never' }
    })
    const failed = await showHubNotificationForSettings({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: { kind: 'turnFailed', title: 'Failed', body: 'Try again' }
    }, {
      notifications: { taskCompletionMode: 'never' }
    })

    expect(completed).toBe(false)
    expect(failed).toBe(false)
    expect(electronMocks.Notification).not.toHaveBeenCalled()
    expect(activationMocks.requestWorkspaceWindowState).not.toHaveBeenCalled()
  })

  it('shows turn result notifications in always mode even when the workspace window is focused', async () => {
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue({
      locked: true,
      pid: 123,
      activation: {
        host: '127.0.0.1',
        port: 456,
        token: 'token',
        protocolVersion: 1
      }
    })
    activationMocks.requestWorkspaceWindowState.mockResolvedValue({
      ok: true,
      focused: true,
      visible: true,
      minimized: false
    })
    const { showHubNotificationForSettings } = await import('../trayManager')

    const shown = await showHubNotificationForSettings({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: { kind: 'turnCompleted', title: 'Done', body: 'Finished' }
    }, {
      notifications: { taskCompletionMode: 'always' }
    })

    expect(shown).toBe(true)
    expect(electronMocks.Notification).toHaveBeenCalledWith({
      title: 'Done',
      body: 'Finished',
      icon: expect.stringMatching(/[\\/]icon\.png$/)
    })
    expect(activationMocks.requestWorkspaceWindowState).not.toHaveBeenCalled()
  })

  it('suppresses when-unfocused turn result notifications while the workspace window is focused', async () => {
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue({
      locked: true,
      pid: 123,
      activation: {
        host: '127.0.0.1',
        port: 456,
        token: 'token',
        protocolVersion: 1
      }
    })
    activationMocks.requestWorkspaceWindowState.mockResolvedValue({
      ok: true,
      focused: true,
      visible: true,
      minimized: false
    })
    const { showHubNotificationForSettings } = await import('../trayManager')

    const shown = await showHubNotificationForSettings({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: { kind: 'turnCompleted', title: 'Done', body: 'Finished' }
    }, {
      notifications: { taskCompletionMode: 'whenUnfocused' }
    })

    expect(shown).toBe(false)
    expect(electronMocks.Notification).not.toHaveBeenCalled()
    expect(activationMocks.requestWorkspaceWindowState).toHaveBeenCalledWith(
      expect.objectContaining({ port: 456, token: 'token' }),
      'F:/examples/workspace'
    )
  })

  it('shows when-unfocused turn result notifications when the workspace is not focused', async () => {
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue({
      locked: true,
      pid: 123,
      activation: {
        host: '127.0.0.1',
        port: 456,
        token: 'token',
        protocolVersion: 1
      }
    })
    activationMocks.requestWorkspaceWindowState.mockResolvedValue({
      ok: true,
      focused: false,
      visible: true,
      minimized: false
    })
    const { showHubNotificationForSettings } = await import('../trayManager')

    const shown = await showHubNotificationForSettings({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: { kind: 'turnCompleted', title: 'Done', body: 'Finished' }
    }, {
      notifications: { taskCompletionMode: 'whenUnfocused' }
    })

    expect(shown).toBe(true)
    expect(electronMocks.Notification).toHaveBeenCalledWith({
      title: 'Done',
      body: 'Finished',
      icon: expect.stringMatching(/[\\/]icon\.png$/)
    })
  })

  it('shows when-unfocused turn result notifications when window state cannot be queried', async () => {
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue({
      locked: true,
      pid: 123,
      activation: {
        host: '127.0.0.1',
        port: 456,
        token: 'token',
        protocolVersion: 1
      }
    })
    activationMocks.requestWorkspaceWindowState.mockResolvedValue(null)
    const { showHubNotificationForSettings } = await import('../trayManager')

    const shown = await showHubNotificationForSettings({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: { kind: 'turnCompleted', title: 'Done', body: 'Finished' }
    }, {
      notifications: { taskCompletionMode: 'whenUnfocused' }
    })

    expect(shown).toBe(true)
    expect(electronMocks.Notification).toHaveBeenCalled()
  })

  it('does not apply task completion notification settings to unrelated Hub notifications', async () => {
    const { showHubNotificationForSettings } = await import('../trayManager')

    const shown = await showHubNotificationForSettings({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: { kind: 'custom', title: 'Done', body: 'Finished' }
    }, {
      notifications: { taskCompletionMode: 'never' }
    })

    expect(shown).toBe(true)
    expect(electronMocks.Notification).toHaveBeenCalledWith({
      title: 'Done',
      body: 'Finished',
      icon: expect.stringMatching(/[\\/]icon\.png$/)
    })
  })

  it('does not launch Desktop when notification click disables Desktop opening', async () => {
    const { showHubNotification } = await import('../trayManager')
    showHubNotification({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: { title: 'Done', body: 'Finished', openDesktopOnClick: false }
    })

    electronMocks.triggerClick()
    await Promise.resolve()

    expect(childProcessMocks.spawn).not.toHaveBeenCalled()
    expect(electronMocks.openExternal).not.toHaveBeenCalled()
  })

  it('activates an existing Desktop workspace for dotcraft workspace links', async () => {
    activationMocks.requestWorkspaceActivation.mockResolvedValue(true)
    workspaceLockMocks.checkWorkspaceLock.mockReturnValue({
      locked: true,
      pid: 123,
      activation: {
        host: '127.0.0.1',
        port: 456,
        token: 'token',
        protocolVersion: 1
      }
    })
    const { showHubNotification } = await import('../trayManager')
    showHubNotification({
      kind: 'notification.requested',
      at: new Date().toISOString(),
      workspacePath: 'F:/examples/workspace',
      data: {
        title: 'Done',
        body: 'Finished',
        actionUrl: 'dotcraft://workspace/open?path=F%3A%2Fexamples%2Fworkspace&threadId=thread_1',
        openDesktopOnClick: true
      }
    })

    electronMocks.triggerClick()
    await Promise.resolve()
    await Promise.resolve()

    expect(activationMocks.requestWorkspaceActivation).toHaveBeenCalledWith(
      expect.objectContaining({ port: 456, token: 'token' }),
      { workspacePath: 'F:/examples/workspace', threadId: 'thread_1' }
    )
    expect(childProcessMocks.spawn).not.toHaveBeenCalled()
  })
})

describe('trayManager native theme sync', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    electronMocks.nativeTheme.themeSource = 'system'
  })

  it('applies the persisted app theme to Electron native tray UI', async () => {
    const { applyTrayNativeThemeSource } = await import('../trayManager')

    applyTrayNativeThemeSource({ theme: 'dark' })
    expect(electronMocks.nativeTheme.themeSource).toBe('dark')

    applyTrayNativeThemeSource({ theme: 'system' })
    expect(electronMocks.nativeTheme.themeSource).toBe('system')
  })
})

describe('trayManager process launches', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    desktopActivationLockMocks.getDesktopActivationEndpoint.mockReturnValue(null)
  })

  it('launches Desktop windows visibly with a workspace argument', async () => {
    const { spawnDesktopWindow } = await import('../trayManager')

    spawnDesktopWindow('E:/examples/workspace')

    const [, args, options] = childProcessMocks.spawn.mock.calls[0]
    expect(args).toEqual(expect.arrayContaining(['--workspace', 'E:/examples/workspace']))
    expect(args).not.toContain('--no-workspace')
    expect(options).toEqual({
      detached: true,
      stdio: 'ignore'
    })
  })

  it('launches Desktop windows with a workspace deep link when a thread is provided', async () => {
    const { spawnDesktopWindow } = await import('../trayManager')

    spawnDesktopWindow('E:/examples/workspace', 'thread 1')

    const [, args, options] = childProcessMocks.spawn.mock.calls[0]
    expect(args).toEqual(expect.arrayContaining([
      'dotcraft://workspace/open?path=E%3A%2Fexamples%2Fworkspace&threadId=thread+1'
    ]))
    expect(args).not.toContain('--no-workspace')
    expect(options).toEqual({
      detached: true,
      stdio: 'ignore'
    })
  })

  it('drops inherited no-workspace flags when launching a specific workspace', async () => {
    const originalArgv = process.argv
    process.argv = ['electron', 'main.js', '--no-workspace']
    try {
      const { spawnDesktopWindow } = await import('../trayManager')

      spawnDesktopWindow('E:/examples/workspace')

      const [, args] = childProcessMocks.spawn.mock.calls[0]
      expect(args).toEqual(expect.arrayContaining(['--workspace', 'E:/examples/workspace']))
      expect(args).not.toContain('--no-workspace')
    } finally {
      process.argv = originalArgv
    }
  })

  it('launches default Desktop windows visibly', async () => {
    const { spawnDesktopWindow } = await import('../trayManager')

    spawnDesktopWindow()

    const [, args, options] = childProcessMocks.spawn.mock.calls[0]
    expect(args).not.toContain('--workspace')
    expect(args).not.toContain('--tray')
    expect(args).toContain('--no-workspace')
    expect(options).toEqual({
      detached: true,
      stdio: 'ignore'
    })
  })

  it('keeps the background tray process hidden', async () => {
    const { ensureTrayProcess } = await import('../trayManager')

    ensureTrayProcess({})

    const [, args, options] = childProcessMocks.spawn.mock.calls[0]
    expect(args).toEqual(expect.arrayContaining(['--tray']))
    expect(options).toEqual({
      detached: true,
      stdio: 'ignore',
      windowsHide: true
    })
  })

  it('stops the tray through its authenticated control endpoint', async () => {
    trayLockMocks.requestTrayShutdown.mockResolvedValue(true)
    const { stopTrayProcess } = await import('../trayManager')

    await expect(stopTrayProcess()).resolves.toBe(true)

    expect(trayLockMocks.requestTrayShutdown).toHaveBeenCalledOnce()
  })

  it('reuses the existing macOS Desktop window before spawning a new process', async () => {
    const originalPlatform = process.platform
    Object.defineProperty(process, 'platform', { value: 'darwin' })
    activationMocks.requestWorkspaceActivation.mockResolvedValue(true)
    desktopActivationLockMocks.getDesktopActivationEndpoint.mockReturnValue({
      host: '127.0.0.1',
      port: 456,
      token: 'desktop-token',
      protocolVersion: 1
    })
    try {
      const { openDesktopWindow } = await import('../trayManager')
      await openDesktopWindow('E:/examples/workspace')

      expect(activationMocks.requestWorkspaceActivation).toHaveBeenCalledWith(
        expect.objectContaining({ port: 456, token: 'desktop-token' }),
        { workspacePath: 'E:/examples/workspace', threadId: null }
      )
      expect(childProcessMocks.spawn).not.toHaveBeenCalled()
    } finally {
      Object.defineProperty(process, 'platform', { value: originalPlatform })
    }
  })

  it('does not launch a macOS tray process when menu bar visibility is disabled', async () => {
    const originalPlatform = process.platform
    Object.defineProperty(process, 'platform', { value: 'darwin' })
    try {
      const { ensureTrayProcess } = await import('../trayManager')

      ensureTrayProcess({ showInMenuBar: false })

      expect(childProcessMocks.spawn).not.toHaveBeenCalled()
    } finally {
      Object.defineProperty(process, 'platform', { value: originalPlatform })
    }
  })
})

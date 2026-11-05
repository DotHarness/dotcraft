import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createDesktopTray, shouldShowDesktopTray } from '../trayManager'
import { showHubNotification } from '../trayNotifications'
import type { DesktopHubClient, HubEvent } from '../desktopHub'

const fake = vi.hoisted(() => ({
  trays: [] as unknown[],
  notifications: [] as unknown[],
  menus: [] as unknown[],
  failTray: false
}))
vi.mock('electron', async () => {
  const { EventEmitter } = await import('node:events')
  return {
    app: { isPackaged: false, getPath: () => '/tmp', getAppPath: () => '/tmp' },
    Menu: {
      buildFromTemplate: (items: unknown[]) => {
        fake.menus.push(items)
        return items
      }
    },
    nativeImage: {
      createEmpty: () => ({ setTemplateImage() {} }),
      createFromPath: () => ({ setTemplateImage() {} })
    },
    shell: { openExternal: vi.fn() },
    Tray: class extends EventEmitter {
      destroy = vi.fn()
      setToolTip = vi.fn()
      setContextMenu = vi.fn()
      constructor() {
        super()
        if (fake.failTray) throw new Error('Tray unavailable')
        fake.trays.push(this)
      }
    },
    Notification: class extends EventEmitter {
      static isSupported() {
        return true
      }
      show = vi.fn()
      constructor(options: unknown) {
        super()
        fake.notifications.push(options)
      }
    }
  }
})
vi.mock('../trayRecentThreads', () => ({
  DIRECT_RECENT_THREAD_COUNT: 5,
  RECENT_THREAD_TITLE_MAX_LENGTH: 40,
  TrayRecentThreadCatalog: class {
    refresh = vi.fn(async () => [])
    dispose = vi.fn()
  }
}))

afterEach(() => {
  fake.trays.length = 0
  fake.menus.length = 0
  fake.notifications.length = 0
  fake.failTray = false
})

describe('application-owned tray', () => {
  it('uses the application window manager and disposes without quitting other windows', async () => {
    let signal: AbortSignal | undefined
    const hub = {
      listProjects: vi.fn(async () => [{ path: '/one' }]),
      listAppServers: vi.fn(async () => []),
      listSatellites: vi.fn(async () => []),
      subscribeEvents: vi.fn((_callback, input: AbortSignal) => {
        signal = input
        return new Promise<void>((resolve) => input.addEventListener('abort', () => resolve()))
      })
    } as unknown as DesktopHubClient
    const openWindow = vi.fn(async () => {})
    const exit = vi.fn(async () => {})
    const tray = createDesktopTray({
      getSettings: () => ({}),
      getHubClient: () => hub,
      openWindow,
      newWindow: vi.fn(),
      isWorkspaceFocused: () => false,
      exit
    })
    try {
      await tray.refresh()
      expect(fake.trays).toHaveLength(1)
      expect(tray.visible).toBe(true)
      ;(fake.trays[0] as EventEmitter).emit('click')
      await Promise.resolve()
      await Promise.resolve()
      expect(openWindow).toHaveBeenCalledWith('/one')
      tray.dispose()
      expect(signal?.aborted).toBe(true)
      expect(exit).not.toHaveBeenCalled()
      expect(tray.visible).toBe(false)
    } finally {
      tray.dispose()
    }
  })

  it('contains a native Tray construction failure instead of rejecting application startup', async () => {
    fake.failTray = true
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const exit = vi.fn(async () => {})
    const tray = createDesktopTray({
      getSettings: () => ({}),
      getHubClient: vi.fn(),
      openWindow: vi.fn(async () => {}),
      newWindow: vi.fn(),
      isWorkspaceFocused: () => false,
      exit
    })
    try {
      await expect(tray.refresh()).resolves.toBeUndefined()
      expect(tray.visible).toBe(false)
      expect(exit).not.toHaveBeenCalled()
      expect(warning).toHaveBeenCalledOnce()
    } finally {
      tray.dispose()
      warning.mockRestore()
    }
  })

  it('respects the native menu-bar preference without changing other platforms', () => {
    expect(shouldShowDesktopTray({ showInMenuBar: false }, 'darwin')).toBe(false)
    expect(shouldShowDesktopTray({ showInMenuBar: false }, 'win32')).toBe(true)
  })

  it('suppresses focused-workspace completion notices', () => {
    const event = {
      kind: 'notification.requested',
      workspacePath: '/one',
      data: { kind: 'turnCompleted', title: 'Completed', body: 'done' }
    } as unknown as HubEvent
    expect(
      showHubNotification(event, {
        locale: 'en',
        settings: { notifications: { taskCompletionMode: 'whenUnfocused' } },
        isWorkspaceFocused: () => true,
        open: vi.fn()
      })
    ).toBe(false)
    expect(fake.notifications).toHaveLength(0)
  })
})

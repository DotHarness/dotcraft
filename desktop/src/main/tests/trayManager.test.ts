import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createDesktopTray, shouldShowDesktopTray } from '../trayManager'
import { showHubNotification } from '../trayNotifications'
import type { DesktopHubClient, HubEvent } from '../desktopHub'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import type { TrayRecentThread } from '../trayRecentThreads'

const fake = vi.hoisted(() => ({
  trays: [] as unknown[],
  notifications: [] as unknown[],
  menus: [] as unknown[],
  threads: [] as TrayRecentThread[],
  updateInlineLabels: vi.fn(),
  nextCommandId: 0,
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
    MenuItem: class {
      commandId = ++fake.nextCommandId
      constructor(options: object) {
        Object.assign(this, options)
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
vi.mock('../trayInlineLabels', () => ({ createTrayInlineLabels: () => fake.updateInlineLabels }))
vi.mock('../trayRecentThreads', async (importOriginal) => ({
  ...await importOriginal<typeof import('../trayRecentThreads')>(),
  TrayRecentThreadCatalog: class {
    refresh = vi.fn(async () => fake.threads)
    dispose = vi.fn()
  }
}))

afterEach(() => {
  fake.trays.length = 0
  fake.menus.length = 0
  fake.notifications.length = 0
  fake.threads = []
  fake.updateInlineLabels.mockClear()
  fake.nextCommandId = 0
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
      isViewingThread: () => false,
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

  it('opens recent chats from the tray and tracks their current menu commands', async () => {
    const chatPath = resolveDefaultChatWorkspacePath()
    fake.threads = Array.from({ length: 4 }, (_, index) => ({
      id: `thread-${index}`,
      displayName: `Conversation ${index}`,
      lastActiveAt: '',
      workspacePath: index === 3 ? chatPath : '/one',
      workspaceName: 'one'
    }))
    const hub = {
      listAppServers: vi.fn(async () => []),
      listSatellites: vi.fn(async () => []),
      subscribeEvents: vi.fn(async () => {})
    } as unknown as DesktopHubClient
    const openWindow = vi.fn(async () => {})
    const tray = createDesktopTray({
      getSettings: () => ({ locale: 'en' }),
      getHubClient: () => hub,
      openWindow,
      newWindow: vi.fn(),
      isWorkspaceFocused: () => false,
      isViewingThread: () => false,
      exit: vi.fn(async () => {})
    })
    type Item = { label: string; sublabel: string; commandId: number; click(): void; submenu?: Item[] }
    try {
      await tray.refresh()
      const menu = fake.menus.at(-1) as Item[]
      const direct = menu.filter((item) => item.sublabel)
      const [overflow] = menu.find((item) => item.label === 'More')!.submenu!
      const commands = [...direct, overflow].map((item) => item.commandId)
      expect(commands).toHaveLength(4)
      expect(fake.updateInlineLabels).toHaveBeenLastCalledWith(commands)
      direct[0].click()
      overflow.click()
      expect(openWindow.mock.calls).toEqual([['/one', 'thread-0'], [chatPath, 'thread-3']])

      fake.threads = fake.threads.slice(-1)
      await tray.refresh()
      const replacement = (fake.menus.at(-1) as Item[]).find((item) => item.sublabel)!
      expect(commands).not.toContain(replacement.commandId)
      expect(fake.updateInlineLabels).toHaveBeenLastCalledWith([replacement.commandId])
    } finally {
      tray.dispose()
    }
    expect(fake.updateInlineLabels).toHaveBeenLastCalledWith([])
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
      isViewingThread: () => false,
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
        isViewingThread: () => true,
        open: vi.fn()
      })
    ).toBe(false)
    expect(fake.notifications).toHaveLength(0)
  })

  it('notifies about an approval in another chat of the focused workspace', () => {
    const approval = (threadId: string) => ({
      kind: 'notification.requested',
      workspacePath: '/one',
      data: { kind: 'approvalRequested', threadId, title: 'Fix login', body: 'Needs your approval' }
    }) as unknown as HubEvent
    const options = {
      locale: 'en' as const,
      settings: {},
      isWorkspaceFocused: () => true,
      isViewingThread: (_workspacePath: string, threadId: string) => threadId === 'thread_a',
      open: vi.fn()
    }

    expect(showHubNotification(approval('thread_a'), options)).toBe(false)
    expect(showHubNotification(approval('thread_b'), options)).toBe(true)
    expect(fake.notifications).toHaveLength(1)
  })
})

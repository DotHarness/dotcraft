import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { HubEvent } from '../HubClient'

const childProcessMocks = vi.hoisted(() => ({
  spawn: vi.fn(() => ({ unref: vi.fn() }))
}))

const workspaceLockMocks = vi.hoisted(() => ({
  checkWorkspaceLock: vi.fn(() => ({ locked: false }))
}))

const activationMocks = vi.hoisted(() => ({
  requestWorkspaceActivation: vi.fn(async () => false)
}))

const electronMocks = vi.hoisted(() => {
  const show = vi.fn()
  let clickHandler: (() => void) | null = null
  const on = vi.fn((event: string, handler: () => void) => {
    if (event === 'click') clickHandler = handler
  })
  const openExternal = vi.fn()
  return {
    show,
    on,
    triggerClick: () => {
      clickHandler?.()
    },
    openExternal,
    Notification: vi.fn().mockImplementation(() => ({ show, on }))
  }
})

vi.mock('electron', () => ({
  app: { isPackaged: false, resourcesPath: 'resources', quit: vi.fn(), on: vi.fn() },
  Menu: { buildFromTemplate: vi.fn((template) => ({ template })) },
  nativeImage: { createFromPath: vi.fn(), createEmpty: vi.fn() },
  Notification: Object.assign(electronMocks.Notification, { isSupported: vi.fn(() => true) }),
  shell: { openExternal: electronMocks.openExternal },
  Tray: vi.fn()
}))

vi.mock('child_process', () => childProcessMocks)

vi.mock('../workspaceLock', () => workspaceLockMocks)

vi.mock('../desktopActivation', () => activationMocks)

vi.mock('fs', () => ({
  existsSync: vi.fn(() => true)
}))

describe('trayManager icon resolution', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('prefers the Windows tray icon asset', async () => {
    const { existsSync } = await import('fs')
    const { resolveTrayIconPath } = await import('../trayManager')

    const path = resolveTrayIconPath('win32')

    expect(path).toContain('tray-icon.png')
    expect(existsSync).toHaveBeenCalledWith(expect.stringContaining('tray-icon.png'))
  })

  it('falls back to the shared PNG when the Windows tray icon is missing', async () => {
    const { existsSync } = await import('fs')
    vi.mocked(existsSync).mockImplementation((path) => String(path).endsWith('icon.png'))
    const { resolveTrayIconPath } = await import('../trayManager')

    const path = resolveTrayIconPath('win32')

    expect(path).toContain('icon.png')
    expect(existsSync).toHaveBeenCalledWith(expect.stringContaining('tray-icon.png'))
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

describe('trayManager process launches', () => {
  beforeEach(() => {
    vi.clearAllMocks()
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

    ensureTrayProcess()

    const [, args, options] = childProcessMocks.spawn.mock.calls[0]
    expect(args).toEqual(expect.arrayContaining(['--tray']))
    expect(options).toEqual({
      detached: true,
      stdio: 'ignore',
      windowsHide: true
    })
  })
})

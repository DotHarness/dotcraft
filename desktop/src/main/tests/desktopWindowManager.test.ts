import { beforeEach, describe, expect, it, vi } from 'vitest'
import { DesktopWindowManager } from '../desktopWindowManager'
import { createDesktopLifecycle } from '../desktopLifecycle'
import type { DesktopApplicationServices } from '../desktopApplicationServices'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import { startDesktopWindow } from '../windows/startDesktopWindow'

const electron = vi.hoisted(() => ({ focused: null as unknown }))
vi.mock('electron', () => ({ BrowserWindow: { getFocusedWindow: () => electron.focused } }))
vi.mock('../windows/startDesktopWindow', () => ({ startDesktopWindow: vi.fn() }))

function fixture() {
  let nextId = 1
  const lifecycle = createDesktopLifecycle()
  const services = { lifecycle } as unknown as DesktopApplicationServices
  const create = vi.mocked(startDesktopWindow)
  create.mockReset()
  create.mockImplementation((_services: DesktopApplicationServices, argv: string[]): DesktopWindowRuntime => {
    const id = nextId++
    const mainWindow = { id, webContents: { id }, isDestroyed: () => false }
    const workspaceIndex = argv.indexOf('--workspace')
    const runtime = {
      mainWindow,
      argv,
      disposed: false,
      activeRemoteProject: null,
      currentWorkspacePath: workspaceIndex === -1 ? '' : argv[workspaceIndex + 1],
      showWindowSafely: vi.fn(),
      openCurrentWorkspaceThread: vi.fn(),
      handleNewThreadDeepLink: vi.fn(async () => {}),
      handleDesktopDeepLink: vi.fn(async () => {}),
      isSameWorkspacePath: (left: string, right: string) => left === right,
      teardownRuntime: vi.fn(async () => {})
    } as unknown as DesktopWindowRuntime
    return runtime
  })
  const manager = new DesktopWindowManager(services)
  services.windows = manager.windows
  return { manager, create, lifecycle }
}

beforeEach(() => {
  electron.focused = null
})

describe('application window ownership', () => {
  it('routes a second launch to the matching window, not whichever window is focused', async () => {
    const { manager, create } = fixture()
    const first = manager.open(['--workspace', '/one'])!
    const second = manager.open(['--workspace', '/two'])!
    electron.focused = first.mainWindow
    await manager.routeLaunch(['dotcraft', '--workspace', '/two'])
    expect(create).toHaveBeenCalledTimes(2)
    expect(second.showWindowSafely).toHaveBeenCalledWith(second.mainWindow)
    expect(first.showWindowSafely).not.toHaveBeenCalled()
    await manager.openWorkspace('/two', 'thread-two')
    expect(second.openCurrentWorkspaceThread).toHaveBeenCalledWith('thread-two')
    expect(first.openCurrentWorkspaceThread).not.toHaveBeenCalled()
  })

  it('preserves explicit remote and no-workspace arguments for newly opened windows', async () => {
    const { manager, create } = fixture()
    manager.open(['--workspace', '/one'])
    await manager.routeLaunch(['dotcraft', '--remote', 'ws://127.0.0.1:9000/ws', '--no-workspace'])
    expect(create.mock.calls[1][1]).toEqual([
      'dotcraft',
      '--remote',
      'ws://127.0.0.1:9000/ws',
      '--no-workspace'
    ])
    await manager.routeLaunch(['dotcraft', '--no-workspace'])
    expect(manager.windows.size).toBe(3)
  })

  it('stops admission and cleans every window once during application exit', async () => {
    const { manager, lifecycle, create } = fixture()
    const first = manager.open(['--workspace', '/one'])!
    const second = manager.open(['--workspace', '/two'])!
    lifecycle.stop()
    await manager.routeLaunch(['--workspace', '/three'])
    expect(create).toHaveBeenCalledTimes(2)
    await manager.dispose()
    expect(first.teardownRuntime).toHaveBeenCalledOnce()
    expect(second.teardownRuntime).toHaveBeenCalledOnce()
  })
})

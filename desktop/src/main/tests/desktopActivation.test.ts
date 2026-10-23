import { tmpdir } from 'os'
import { join } from 'path'
import { describe, expect, it, vi } from 'vitest'
import type { BrowserWindow } from 'electron'
import {
  requestWorkspaceActivation,
  requestWorkspaceWindowState,
  startWorkspaceActivationServer
} from '../desktopActivation'

function createWindowState(overrides?: {
  focused?: () => boolean
  visible?: () => boolean
  minimized?: () => boolean
}): BrowserWindow {
  return {
    isDestroyed: vi.fn(() => false),
    isFocused: vi.fn(overrides?.focused ?? (() => false)),
    isVisible: vi.fn(overrides?.visible ?? (() => true)),
    isMinimized: vi.fn(overrides?.minimized ?? (() => false))
  } as unknown as BrowserWindow
}

function workspacePath(name: string): string {
  return join(tmpdir(), name)
}

describe('desktop activation protocol', () => {
  it('reports window state without activating the workspace', async () => {
    let focused = true
    const onActivate = vi.fn()
    const workspace = workspacePath('dotcraft-activation-state')
    const handle = await startWorkspaceActivationServer({
      workspacePath: workspace,
      getWindow: () => createWindowState({
        focused: () => focused,
        visible: () => true,
        minimized: () => false
      }),
      onActivate
    })

    try {
      await expect(requestWorkspaceWindowState(handle.endpoint, workspace)).resolves.toEqual({
        ok: true,
        focused: true,
        visible: true,
        minimized: false
      })
      focused = false
      await expect(requestWorkspaceWindowState(handle.endpoint, workspace)).resolves.toEqual({
        ok: true,
        focused: false,
        visible: true,
        minimized: false
      })
      expect(onActivate).not.toHaveBeenCalled()

      await expect(requestWorkspaceActivation(handle.endpoint, {
        workspacePath: workspace,
        threadId: 'thread_1'
      })).resolves.toBe(true)
      expect(onActivate).toHaveBeenCalledWith({
        workspacePath: workspace,
        threadId: 'thread_1'
      })
    } finally {
      handle.close()
    }
  })

  it('rejects window state queries with an invalid token or workspace', async () => {
    const onActivate = vi.fn()
    const workspace = workspacePath('dotcraft-activation-invalid')
    const handle = await startWorkspaceActivationServer({
      workspacePath: workspace,
      getWindow: () => createWindowState(),
      onActivate
    })

    try {
      await expect(requestWorkspaceWindowState({
        ...handle.endpoint,
        token: 'wrong-token'
      }, workspace)).resolves.toBeNull()
      await expect(requestWorkspaceWindowState(handle.endpoint, `${workspace}-other`)).resolves.toBeNull()
      expect(onActivate).not.toHaveBeenCalled()
    } finally {
      handle.close()
    }
  })

  it('activates secondary workspaces, reporting them unfocused, even without a foreground workspace', async () => {
    const onActivate = vi.fn()
    const foreground = workspacePath('dotcraft-activation-foreground')
    const secondary = workspacePath('dotcraft-activation-secondary')
    const withForeground = await startWorkspaceActivationServer({
      workspacePath: foreground,
      getWindow: () => createWindowState({ focused: () => true }),
      canActivateWorkspace: (workspace) => workspace === secondary,
      isForegroundWorkspace: (workspace) => workspace === foreground,
      onActivate
    })
    const withoutForeground = await startWorkspaceActivationServer({
      workspacePath: '',
      getWindow: () => createWindowState({ focused: () => true }),
      canActivateWorkspace: () => true,
      isForegroundWorkspace: () => false,
      onActivate
    })

    try {
      await expect(requestWorkspaceWindowState(withForeground.endpoint, secondary)).resolves.toEqual({
        ok: true,
        focused: false,
        visible: true,
        minimized: false
      })
      await expect(requestWorkspaceActivation(withForeground.endpoint, {
        workspacePath: secondary,
        threadId: 'thread_2'
      })).resolves.toBe(true)
      await expect(requestWorkspaceActivation(withoutForeground.endpoint, {
        workspacePath: secondary,
        threadId: 'thread_3'
      })).resolves.toBe(true)
      expect(onActivate.mock.calls).toEqual([
        [{ workspacePath: secondary, threadId: 'thread_2' }],
        [{ workspacePath: secondary, threadId: 'thread_3' }]
      ])
    } finally {
      withForeground.close()
      withoutForeground.close()
    }
  })
})

import { expect, it, vi } from 'vitest'
import type { BrowserWindow } from 'electron'
import { parseWorkspaceOpenDeepLink } from '../../shared/desktopDeepLink'
import { createDesktopNotification } from '../desktopNotification'
import { showJobResultNotification } from '../windows/windowNotifications'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'

vi.mock('../desktopNotification', () => ({ createDesktopNotification: vi.fn() }))
vi.mock('../windows/windowNodeReplMcp', () => ({ isCurrentWorkspaceRequestSource: vi.fn() }))

it('opens a job result in its source project and leaves results without a thread informational', () => {
  const show = vi.fn()
  vi.mocked(createDesktopNotification).mockReturnValue({ show } as unknown as Electron.Notification)
  const openDesktopWindow = vi.fn(async () => {})
  const ctx = { app: { openDesktopWindow }, settings: {}, currentWorkspacePath: '/front' } as unknown as DesktopWindowRuntime
  const entry = { kind: 'local', workspacePath: '/source' } as WorkspaceConnectionEntry
  const win = { isFocused: () => true } as BrowserWindow

  showJobResultNotification(ctx, entry, win, { threadId: 'thread-result' })
  expect(show).not.toHaveBeenCalled()

  ctx.settings.notifications = { taskCompletionMode: 'always' }
  showJobResultNotification(ctx, entry, win, { threadId: 'thread-result' })
  expect(show).toHaveBeenCalledOnce()
  expect(openDesktopWindow).not.toHaveBeenCalled()
  const activation = vi.mocked(createDesktopNotification).mock.calls[0][1]!
  expect(parseWorkspaceOpenDeepLink(activation.url!)).toEqual({ workspacePath: '/source', threadId: 'thread-result' })
  activation.open()
  expect(openDesktopWindow).toHaveBeenCalledWith('/source', 'thread-result')

  showJobResultNotification(ctx, entry, win, {})
  expect(show).toHaveBeenCalledTimes(2)
  expect(vi.mocked(createDesktopNotification).mock.calls[1][1]).toBeUndefined()
})

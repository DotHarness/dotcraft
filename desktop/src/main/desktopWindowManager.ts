import { BrowserWindow } from 'electron'
import type { DesktopApplicationServices } from './desktopApplicationServices'
import type { DesktopWindowRuntime } from './windows/windowRuntime'
import { startDesktopWindow } from './windows/startDesktopWindow'
import { NO_WORKSPACE_ARG, hasRemoteEndpointArg } from './workspaceArgs'
import { findNewThreadDeepLink, findWorkspaceOpenDeepLink } from '../shared/desktopDeepLink'

export class DesktopWindowManager {
  readonly windows = new Map<number, DesktopWindowRuntime>()
  private disposing: Promise<void> | null = null

  constructor(private readonly services: DesktopApplicationServices) {}

  focused(): DesktopWindowRuntime | undefined {
    const focused = BrowserWindow.getFocusedWindow()
    return (
      (focused ? this.windows.get(focused.webContents.id) : undefined) ?? this.windows.values().next().value
    )
  }

  open(argv: string[]): DesktopWindowRuntime | undefined {
    if (this.services.lifecycle.isStopping) return undefined
    const window = startDesktopWindow(this.services, argv)
    if (window.mainWindow) this.windows.set(window.mainWindow.webContents.id, window)
    return window
  }

  async openWorkspace(workspacePath?: string | null, threadId?: string | null): Promise<void> {
    if (this.services.lifecycle.isStopping) return
    const selected = workspacePath
      ? [...this.windows.values()].find(
          (window) =>
            !window.disposed &&
            !window.activeRemoteProject &&
            window.isSameWorkspacePath(window.currentWorkspacePath, workspacePath)
        )
      : this.focused()
    if (selected?.mainWindow) {
      selected.showWindowSafely(selected.mainWindow)
      if (threadId) selected.openCurrentWorkspaceThread(threadId)
      return
    }
    const args = workspacePath ? ['--workspace', workspacePath] : [NO_WORKSPACE_ARG]
    const created = this.open(args)
    if (threadId && created) created.pendingWorkspaceOpenThreadId = threadId
  }

  async routeLaunch(argv: string[], initial = false): Promise<void> {
    if (this.services.lifecycle.isStopping) return
    const workspace = findWorkspaceOpenDeepLink(argv)
    if (workspace) {
      await this.openWorkspace(workspace.workspacePath, workspace.threadId)
      return
    }
    const newThread = findNewThreadDeepLink(argv)
    if (newThread) {
      const target = this.focused() ?? this.open([NO_WORKSPACE_ARG])
      if (target) await target.handleNewThreadDeepLink(newThread)
      return
    }
    const deepLink = argv.find((arg) => arg.startsWith('dotcraft://'))
    if (deepLink) {
      const target = this.focused() ?? this.open([NO_WORKSPACE_ARG])
      if (target) await target.handleDesktopDeepLink(deepLink)
      return
    }
    const workspaceIndex = argv.indexOf('--workspace')
    if (!initial && workspaceIndex !== -1 && argv[workspaceIndex + 1] && !hasRemoteEndpointArg(argv)) {
      await this.openWorkspace(argv[workspaceIndex + 1])
      return
    }
    if (initial || hasRemoteEndpointArg(argv) || argv.includes(NO_WORKSPACE_ARG) || !this.focused())
      this.open(argv)
    else {
      const target = this.focused()
      if (target?.mainWindow) target.showWindowSafely(target.mainWindow)
    }
  }

  dispose(): Promise<void> {
    return (this.disposing ??= Promise.allSettled(
      [...this.windows.values()].map(async (window) => {
        window.disposed = true
        await window.teardownRuntime('application shutdown', {
          cleanupIpcHandlers: true,
          clearWorkspace: true
        })
      })
    ).then(() => undefined))
  }
}

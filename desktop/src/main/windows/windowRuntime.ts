import { BrowserWindow } from 'electron'
import type { ConnectionStatusPayload } from '../../shared/connectionStatus'
import { findNewThreadDeepLink, findWorkspaceOpenDeepLink } from '../../shared/desktopDeepLink'
import type { WorkspaceStatusPayload } from '../../shared/workspaceSetup'
import type { DesktopApplicationServices } from '../desktopApplicationServices'
import { DesktopAppServerClient } from '../DesktopAppServerClient'
import { type DesktopHubClient } from '../desktopHub'
import { resolveServerRequestBridge } from '../ipcBridge'
import { type AppSettings } from '../settings'
import type { ProjectRestoreState } from '../recentProjectRestore'
import { SshMachinesManager } from '../sshMachines/sshMachinesManager'
import { UserInputAutoResolutionCoordinator } from '../userInputAutoResolution'
import * as windowConnections from './windowConnections'
import * as windowForegroundConnection from './windowForegroundConnection'
import * as windowFrame from './windowFrame'
import * as windowLifecycle from './windowLifecycle'
import * as windowLinks from './windowLinks'
import * as windowProjects from './windowProjects'
import * as windowDeviceConnections from './windowDeviceConnections'
import * as windowRemoteActions from './windowRemoteActions'
import * as windowRemoteConnections from './windowRemoteConnections'
import * as windowRemoteProjects from './windowRemoteProjects'
import * as windowRequests from './windowRequests'
import * as windowSecondaryConnections from './windowSecondaryConnections'
import * as windowStatus from './windowStatus'
import type { ActiveRemoteProject, WorkspaceConnectionEntry } from './windowTypes'
import * as windowWorkspaceActions from './windowWorkspaceActions'

export interface DesktopWindowState {
  app: DesktopApplicationServices
  settings: AppSettings
  argv: string[]
  disposed: boolean
  disposers: Array<() => void>
  oratorio: { contextChanged(): void; dispose(): void } | null
  mainWindow: BrowserWindow | null
  wireClient: DesktopAppServerClient | null
  userInputAutoResolution: UserInputAutoResolutionCoordinator
  currentWorkspacePath: string
  lastDashboardUrl: string | null
  lastAppServerWsUrl: string | null
  lastConnectionStatus: ConnectionStatusPayload
  activeRemoteWorkspace: WorkspaceStatusPayload['remote'] | null
  activeRemoteProject: ActiveRemoteProject | null
  previousLocalForegroundWorkspacePath: string | null
  lastRemoteStackLocalPort: number | null
  deviceOpenGeneration: number
  sshMachinesManager: SshMachinesManager | null
  workspaceConnections: Map<string, WorkspaceConnectionEntry>
  pendingDeviceOpenTimer: ReturnType<typeof setTimeout> | null
  pendingDeviceOpenAttempt: number
  hubEventAbortController: AbortController | null
  secondaryRefreshTimer: ReturnType<typeof setTimeout> | null
  startupProjectStates: Map<string, ProjectRestoreState>
  pendingChromeSettingsDeepLink: boolean
  pendingPairingLink: string | null
  pendingWorkspaceOpenThreadId: string | null
  pendingNewThreadLink: ReturnType<typeof findNewThreadDeepLink>
  prewarmedAppServer: {
    workspacePath: string
    promise: ReturnType<DesktopHubClient['ensureAppServer']>
  } | null
}

type BoundActions<T> = {
  [K in keyof T]: T[K] extends (ctx: DesktopWindowRuntime, ...args: infer A) => infer R
    ? (...args: A) => R
    : never
}

export type DesktopWindowRuntime = DesktopWindowState &
  BoundActions<typeof windowProjects> &
  BoundActions<typeof windowRequests> &
  BoundActions<typeof windowRemoteProjects> &
  BoundActions<typeof windowRemoteConnections> &
  BoundActions<typeof windowLifecycle> &
  BoundActions<typeof windowLinks> &
  BoundActions<typeof windowFrame> &
  BoundActions<typeof windowRemoteActions> &
  BoundActions<typeof windowConnections> &
  BoundActions<typeof windowDeviceConnections> &
  BoundActions<typeof windowSecondaryConnections> &
  BoundActions<typeof windowWorkspaceActions> &
  BoundActions<typeof windowForegroundConnection> &
  BoundActions<typeof windowStatus> & {
    runtimeIsStopping(): boolean
    updateSettings(partial: Partial<AppSettings>): Promise<void>
  }

function bindActions<T extends Record<string, (ctx: DesktopWindowRuntime, ...args: never[]) => unknown>>(
  getContext: () => DesktopWindowRuntime,
  actions: T
): BoundActions<T> {
  return Object.fromEntries(
    Object.entries(actions).map(([name, fn]) => [name, (...args: never[]) => fn(getContext(), ...args)])
  ) as BoundActions<T>
}

export function createWindowRuntime(app: DesktopApplicationServices, argv: string[]): DesktopWindowRuntime {
  const ctx: DesktopWindowRuntime = {
    app,
    settings: { ...app.settings },
    argv,
    disposed: false,
    disposers: [],
    oratorio: null,
    mainWindow: null,
    wireClient: null,
    userInputAutoResolution: new UserInputAutoResolutionCoordinator({
      onResolve: (bridgeId) => {
        if (ctx.mainWindow)
          resolveServerRequestBridge(bridgeId, { answers: {} }, ctx.mainWindow.webContents.id)
      },
      onChanged: (states) => {
        const win = ctx.mainWindow
        if (win && !win.isDestroyed()) {
          win.webContents.send('appserver:user-input-auto-resolution-changed', states)
        }
      }
    }),
    currentWorkspacePath: '',
    lastDashboardUrl: null,
    lastAppServerWsUrl: null,
    lastConnectionStatus: { status: 'disconnected' },
    activeRemoteWorkspace: null,
    activeRemoteProject: null,
    previousLocalForegroundWorkspacePath: null,
    lastRemoteStackLocalPort: null,
    deviceOpenGeneration: 0,
    sshMachinesManager: null,
    workspaceConnections: new Map<string, WorkspaceConnectionEntry>(),
    pendingDeviceOpenTimer: null,
    pendingDeviceOpenAttempt: 0,
    hubEventAbortController: null,
    secondaryRefreshTimer: null,
    startupProjectStates: new Map(),
    pendingChromeSettingsDeepLink: false,
    pendingPairingLink: null,
    pendingWorkspaceOpenThreadId: findWorkspaceOpenDeepLink(argv)?.threadId ?? null,
    pendingNewThreadLink: findNewThreadDeepLink(argv),
    prewarmedAppServer: null,
    runtimeIsStopping: () => ctx.disposed || app.lifecycle.isStopping,
    updateSettings: (partial: Partial<AppSettings>) => app.updateSharedSettings(partial, ctx),
    ...bindActions(() => ctx, windowProjects),
    ...bindActions(() => ctx, windowRequests),
    ...bindActions(() => ctx, windowRemoteProjects),
    ...bindActions(() => ctx, windowRemoteConnections),
    ...bindActions(() => ctx, windowLifecycle),
    ...bindActions(() => ctx, windowLinks),
    ...bindActions(() => ctx, windowFrame),
    ...bindActions(() => ctx, windowRemoteActions),
    ...bindActions(() => ctx, windowConnections),
    ...bindActions(() => ctx, windowDeviceConnections),
    ...bindActions(() => ctx, windowSecondaryConnections),
    ...bindActions(() => ctx, windowWorkspaceActions),
    ...bindActions(() => ctx, windowForegroundConnection),
    ...bindActions(() => ctx, windowStatus)
  }
  ctx.pendingChromeSettingsDeepLink = argv.some(ctx.isChromeSettingsDeepLink)
  return ctx
}

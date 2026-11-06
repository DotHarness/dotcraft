import { BrowserWindow } from 'electron'
import type { ConnectionStatusPayload, RetryConnectionRequest } from '../shared/connectionStatus'
import type { RemoteStack } from '../shared/dockerDeployments'
import { normalizeLocale, type AppLocale } from '../shared/locales'
import { type ConnectionSettingsDraft } from '../shared/remoteConnection'
import type { SshMachine } from '../shared/sshMachines'
import type { UserInputAutoResolutionState } from '../shared/userInputAutoResolution'
import type { WorkspaceProjectsPayload } from '../shared/workspaceProjects'
import type {
  WorkspaceSetupModelListRequest,
  WorkspaceSetupModelListResult,
  WorkspaceSetupRequest,
  WorkspaceSetupSignInResult,
  WorkspaceStatusPayload
} from '../shared/workspaceSetup'
import type { DesktopAppServerClient } from './DesktopAppServerClient'
import type { DesktopHubClient } from './desktopHub'
import type { AppSettings, BinarySource } from './settings'
import type { SshMachinesManager } from './sshMachines/sshMachinesManager'
import type { WindowIpcMain } from './windowIpc'

export interface ResolvedBinaryRequest {
  binarySource?: BinarySource
  binaryPath?: string
}
export interface IpcHandlerCallbacks {
  onSwitchWorkspace: (newPath: string) => Promise<void>
  onOpenDesktopLink: (url: string) => Promise<void>
  onClearWorkspaceSelection: () => Promise<void>
  onRunWorkspaceSetup: (request: WorkspaceSetupRequest) => Promise<void>
  onListSetupModels: (request: WorkspaceSetupModelListRequest) => Promise<WorkspaceSetupModelListResult>
  onLoginSetupProvider?: (request: WorkspaceSetupModelListRequest) => Promise<WorkspaceSetupSignInResult>
  onOpenNewWindow: () => void
  onRestartManagedAppServer: () => Promise<void>
  onRetryAppServerConnection?: (request?: RetryConnectionRequest) => Promise<void>
  onApplyConnectionSettings?: (draft: ConnectionSettingsDraft) => Promise<void>
  getSshMachinesManager?: () => SshMachinesManager
  onConnectDockerDeployment?: (machine: SshMachine, stack: RemoteStack) => Promise<{ localPort?: number }>
  onDisconnectDockerDeployment?: (machineId: string, stackId: string) => Promise<void>
  onOpenRemoteProject?: (machineId: string, projectId: string) => Promise<void>
  onLeaveRemoteProject?: (machineId: string, projectId: string) => Promise<void>
  onDisconnectRemoteProject?: () => Promise<void>
  getSettings: () => AppSettings
  /** Absent leaves the satellite channels unregistered. */
  getHubClient?: () => DesktopHubClient
  /** Null unless the AppServer is a Hub-managed local one. */
  getAppServerWsConfig?: () => { wsUrl: string; token?: string } | null
  updateSettings: (partial: Partial<AppSettings>) => void | Promise<void>
  getLocalProjectPaths: () => string[]
  getWorkspaceProjects?: () => WorkspaceProjectsPayload
  removeProject?: (workspacePath: string) => Promise<void>
  saveLocalProject?: (params: {
    projectPath?: string
    primaryFolder: string
    secondaryFolders: string[]
    name?: string
  }) => void | Promise<void>
  restartWorkspace?: (workspacePath: string) => void | Promise<void>
  stopWorkspace?: (workspacePath: string) => void | Promise<void>
  archiveThreadInWorkspace?: (workspacePath: string, threadId: string) => void | Promise<void>
  getConnectionKey?: (client: DesktopAppServerClient) => string | null
  getConnectionClient?: (key: string) => DesktopAppServerClient | null
  getConnectionStatus: () => ConnectionStatusPayload
  getWorkspaceStatus: () => WorkspaceStatusPayload
  isWorkspaceOpen?: (workspacePath: string) => boolean
  onAppServerRequestStarted?: (client: DesktopAppServerClient, method: string, params: unknown) => number | undefined
  /** Observes only successful renderer AppServer requests, feeding Desktop-local routing state. */
  onAppServerRequestCompleted?: (
    client: DesktopAppServerClient,
    method: string,
    params: unknown,
    result: unknown,
    requestGeneration?: number
  ) => void
  getUserInputAutoResolutionSnapshot?: () => UserInputAutoResolutionState[]
  onUserInputConversationPresented?: (threadId: string | null) => void
  onUserInputConversationActivity?: (threadId: string) => void
  onUserInputAutoResolutionSnoozed?: (threadId: string, requestId: string) => void
}

export function mainLocale(callbacks: IpcHandlerCallbacks): AppLocale {
  return normalizeLocale(callbacks.getSettings().locale)
}
export interface WindowIpcContext {
  owner: BrowserWindow
  ipc: WindowIpcMain
  handleSafe: WindowIpcMain['handle']
  workspacePath: string
  getWireClient: () => DesktopAppServerClient | null
  callbacks: IpcHandlerCallbacks
}

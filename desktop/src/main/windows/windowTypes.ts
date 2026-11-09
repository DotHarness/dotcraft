import type {
  WorkspaceProjectKind,
  WorkspaceRemoteProjectMetadata,
  WorkspaceRemoteProjectSource
} from '../../shared/workspaceProjects'
import type { WorkspaceStatusPayload } from '../../shared/workspaceSetup'
import { DesktopAppServerClient, type InitializeResult } from '../DesktopAppServerClient'
import { type WorkspaceConnectionRole } from '../workspaceConnectionRouting'
import type { AppServerBridge } from '../pairedComputers/appServerBridge'

export interface DeviceProjectConnection {
  bridge: AppServerBridge | null
  retryTimer: ReturnType<typeof setTimeout> | null
  attempt: number
  generation: number
  project: ActiveRemoteProject
}

export interface WorkspaceConnectionEntry {
  key: string
  projectId: string
  kind: WorkspaceProjectKind
  workspacePath: string
  localWorkspacePath?: string
  displayPath?: string
  remote?: WorkspaceRemoteProjectMetadata
  wsUrl: string
  client: DesktopAppServerClient
  role: WorkspaceConnectionRole
  connected: boolean
  connecting: boolean
  lastUsedAt: number
  initializeResult?: InitializeResult
  threads: unknown[]
  errorMessage?: string
  device?: DeviceProjectConnection
  remoteProject?: ActiveRemoteProject
  reconnectTimer?: ReturnType<typeof setTimeout> | null
  reconnectAttempt?: number
}

export interface ActiveRemoteProject {
  projectId: string
  source: WorkspaceRemoteProjectSource
  name: string
  identityWorkspacePath: string
  displayPath: string
  endpoint?: string
  localWorkspacePath: string
  remote: WorkspaceRemoteProjectMetadata
  status: NonNullable<WorkspaceStatusPayload['remote']>
}

export interface RemoteConnectionDiagnostic {
  stage?: string
  hostName?: string
  stackName?: string
  localPort?: number
  targetPort?: number
  tokenPresent?: boolean
}

export interface ConnectViaWebSocketOptions {
  role?: WorkspaceConnectionRole
  existingEntry?: WorkspaceConnectionEntry
  device?: DeviceProjectConnection
  onReady?: () => void
  autoReconnect?: boolean
  initializeTimeoutMs?: number | null
  initialDisconnectIsError?: boolean
  remoteDiagnostic?: RemoteConnectionDiagnostic
  projectId?: string
  projectKind?: WorkspaceProjectKind
  identityWorkspacePath?: string
  displayPath?: string
  remote?: WorkspaceRemoteProjectMetadata
  remoteProject?: ActiveRemoteProject
}

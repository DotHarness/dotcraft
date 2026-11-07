import type {
  WorkspaceProjectKind,
  WorkspaceRemoteProjectMetadata,
  WorkspaceRemoteProjectSource
} from '../../shared/workspaceProjects'
import type { WorkspaceStatusPayload } from '../../shared/workspaceSetup'
import { DesktopAppServerClient, type InitializeResult } from '../DesktopAppServerClient'
import { type WorkspaceConnectionRole } from '../workspaceConnectionRouting'
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
  onReady?: () => void
  onInitialFailure?: (error: unknown) => void
  autoReconnect?: boolean
  initializeTimeoutMs?: number | null
  initialDisconnectIsError?: boolean
  remoteDiagnostic?: RemoteConnectionDiagnostic
  projectId?: string
  projectKind?: WorkspaceProjectKind
  identityWorkspacePath?: string
  displayPath?: string
  remote?: WorkspaceRemoteProjectMetadata
}

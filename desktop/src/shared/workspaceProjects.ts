export type WorkspaceProjectState = 'foreground' | 'secondary' | 'cold' | 'connecting' | 'error'
export type WorkspaceProjectKind = 'local' | 'remote' | 'chat'
export type WorkspaceRemoteProjectSource = 'servers' | 'ssh' | 'device' | 'manual' | 'cli'

export interface WorkspaceRemoteProjectMetadata {
  source: WorkspaceRemoteProjectSource
  displayPath?: string
  endpoint?: string
  hostId?: string
  stackId?: string
  serverName?: string
  stackName?: string
  workspaceDir?: string
  appServerWorkspacePath?: string
  composeDir?: string
  projectName?: string
  remoteProjectId?: string
}

export interface WorkspaceProjectSummary {
  projectId?: string
  connectionKey?: string
  connectionId?: string
  capabilities?: Record<string, unknown>
  kind?: WorkspaceProjectKind
  path: string
  identityWorkspacePath?: string
  primaryFolder?: string
  name: string
  lastOpenedAt?: string
  state: WorkspaceProjectState
  running: boolean
  loaded: boolean
  threadCount: number
  threads: unknown[]
  pinnedThreadIds?: string[]
  /** Desktop-local project pin state. */
  pinned: boolean
  secondaryFolders?: string[]
  remote?: WorkspaceRemoteProjectMetadata
  errorMessage?: string
}

export interface WorkspaceProjectsPayload {
  foregroundWorkspacePath: string
  foregroundProjectId?: string
  secondaryLimit: number
  projects: WorkspaceProjectSummary[]
  /** This computer's default Chat workspace, independent of the foreground project; omitted in transient remote-only launches. */
  chat?: WorkspaceProjectSummary
}

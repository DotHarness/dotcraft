export type WorkspaceProjectState = 'foreground' | 'secondary' | 'cold' | 'connecting' | 'error'
export type WorkspaceProjectKind = 'local' | 'remote' | 'chat'
export type WorkspaceRemoteProjectSource = 'servers' | 'manual' | 'cli'

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
}

export interface WorkspaceProjectSummary {
  projectId?: string
  kind?: WorkspaceProjectKind
  path: string
  identityWorkspacePath?: string
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
  /**
   * Local multi-folder Projects only: additional runtime roots beyond the
   * primary folder, as absolute normalized paths. The primary folder (`path`)
   * is the identity and is never included here. Omitted/empty for single-folder,
   * remote, and Chat projects.
   */
  secondaryFolders?: string[]
  remote?: WorkspaceRemoteProjectMetadata
  errorMessage?: string
}

export interface WorkspaceProjectsPayload {
  foregroundWorkspacePath: string
  foregroundProjectId?: string
  secondaryLimit: number
  projects: WorkspaceProjectSummary[]
  /**
   * Default Chat workspace (`~/.craft/workspaces/chats`), surfaced as a dedicated
   * `Chats` group rather than a Project row. Its physical path is diagnostic only.
   * Present only in local connection mode; omitted while a remote project is active.
   */
  chat?: WorkspaceProjectSummary
}

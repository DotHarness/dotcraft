export const PROJECT_ACTION_ICONS = ['tool', 'run', 'debug', 'test'] as const
export const PROJECT_ACTION_PLATFORMS = ['win32', 'darwin', 'linux'] as const

export type ProjectActionIcon = (typeof PROJECT_ACTION_ICONS)[number]
export type ProjectActionPlatform = (typeof PROJECT_ACTION_PLATFORMS)[number]

export interface ProjectAction {
  id: string
  name: string
  icon: ProjectActionIcon
  command: string
}

export interface ProjectEnvironment {
  id: string
  name: string
  path: string
  isDefault: boolean
  error?: string
  actions: ProjectAction[]
  revision: string
}

export interface ProjectEnvironmentsResult {
  environments: ProjectEnvironment[]
  selectedEnvironmentId: string | null
}

export interface WorktreeEnvironmentOption {
  id: string
  name: string
  path: string
  isDefault: boolean
  error?: string
}

export interface WorktreeEnvironmentsResult {
  environments: WorktreeEnvironmentOption[]
  selectedEnvironmentId: string | null
  unavailable: boolean
}

export interface ProjectActionDraft {
  name: string
  icon: ProjectActionIcon
  command: string
  platform?: ProjectActionPlatform
}

export interface AddProjectActionRequest {
  workspacePath: string
  environmentId?: string
  /** Content revision captured when the dialog opened; `null` when no environment existed. */
  revision: string | null
  action: ProjectActionDraft
}

export type AddProjectActionResult =
  | { ok: true; environmentId: string; action: ProjectAction | null }
  | { ok: false; reason: 'conflict' | 'failed' }

export interface ProjectActionRunRequest {
  workspacePath: string
  environmentId: string
  actionId: string
  worktreePath?: string
}

export type EnvironmentScript = { default: string } & Partial<Record<ProjectActionPlatform, string>>

export interface EnvironmentDocument {
  name: string
  setup: EnvironmentScript
  cleanup: EnvironmentScript
  actions: ProjectActionDraft[]
}

export interface ProjectEnvironmentEntry {
  id: string
  name: string
  fileName: string
  path: string
  folder: string
  isDefault: boolean
  inherited: boolean
  error?: string
}

export interface ProjectEnvironmentList {
  projectPath: string
  environments: ProjectEnvironmentEntry[]
  newEnvironmentPath: string
}

export type ReadEnvironmentResult =
  | { ok: true; path: string; revision: string | null; document: EnvironmentDocument | null; error?: string }
  | { ok: false; reason: 'failed' }

export interface WriteEnvironmentRequest {
  projectPath: string
  path: string
  revision: string | null
  document: EnvironmentDocument
}

export type WriteEnvironmentResult =
  | { ok: true; path: string; revision: string }
  | { ok: false; reason: 'conflict' | 'failed' }

export interface ProjectActionRun {
  cwd: string
  command: string
  title: string
}

export interface ProjectSetupRunRequest {
  workspacePath: string
  environmentId: string
  worktreePath?: string
}

export interface ProjectSetupRun {
  cwd: string
  command: string
  env: Record<string, string>
}

export interface ProjectActionPreferences {
  selectedEnvironmentId?: string | null
  worktreeEnvironmentId?: string | null
  recentActions?: Record<string, string[]>
}

export function isProjectActionIcon(value: unknown): value is ProjectActionIcon {
  return typeof value === 'string' && (PROJECT_ACTION_ICONS as readonly string[]).includes(value)
}

export function isProjectActionPlatform(value: unknown): value is ProjectActionPlatform {
  return typeof value === 'string' && (PROJECT_ACTION_PLATFORMS as readonly string[]).includes(value)
}

export function projectActionTerminalTabId(threadId: string, environmentId: string, actionId: string): string {
  return `project-action:${threadId}:${environmentId}:${actionId}`
}

export function projectSetupTerminalTabId(threadId: string): string {
  return `project-setup:${threadId}`
}

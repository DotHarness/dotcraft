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
}

export interface ProjectActionRun {
  cwd: string
  command: string
  title: string
}

export interface ProjectActionPreferences {
  selectedEnvironmentId?: string
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

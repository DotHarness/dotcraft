export type OpenTargetIconKey = 'fileManager' | 'terminal' | 'editor'

export interface OpenTargetInfo {
  id: string
  label: string
  labelKey?: string
  iconKey: OpenTargetIconKey
  iconDataUrl?: string
  hidden?: boolean
}

export interface OpenTargetLocation {
  line: number
  column: number
}

export interface OpenInTargetRequest {
  targetId: string
  path: string
  line?: number
  column?: number
}

export interface OpenTargetPreferences {
  global?: string
  perProject?: Record<string, string>
}

export const FILE_MANAGER_TARGET_ID = 'fileManager'
export const SYSTEM_DEFAULT_TARGET_ID = 'systemDefault'
export const CUSTOM_TARGET_PREFIX = 'custom:'

export function resolveEffectiveOpenTarget<T extends { id: string; hidden?: boolean }>(
  targets: readonly T[],
  preferences: OpenTargetPreferences | undefined,
  projectRoot: string | null | undefined
): T | undefined {
  const visible = targets.filter((target) => !target.hidden)
  const find = (id: string | undefined): T | undefined =>
    id ? visible.find((target) => target.id === id) : undefined
  return (projectRoot ? find(preferences?.perProject?.[projectRoot]) : undefined)
    ?? find(preferences?.global)
    ?? visible[0]
}

export function rememberOpenTarget(targetId: string, projectRoot: string | null | undefined): OpenTargetPreferences {
  return projectRoot
    ? { global: targetId, perProject: { [projectRoot]: targetId } }
    : { global: targetId }
}

export function normalizeOpenTargetPreferences(value: unknown): OpenTargetPreferences | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const raw = value as Record<string, unknown>
  const global = typeof raw.global === 'string' && raw.global.trim() ? raw.global.trim() : undefined
  const perProject: Record<string, string> = {}
  if (raw.perProject && typeof raw.perProject === 'object' && !Array.isArray(raw.perProject)) {
    for (const [root, targetId] of Object.entries(raw.perProject as Record<string, unknown>)) {
      if (root.trim() && typeof targetId === 'string' && targetId.trim()) perProject[root] = targetId.trim()
    }
  }
  const hasPerProject = Object.keys(perProject).length > 0
  if (!global && !hasPerProject) return undefined
  return {
    ...(global ? { global } : {}),
    ...(hasPerProject ? { perProject } : {})
  }
}

export function mergeOpenTargetPreferences(
  current: OpenTargetPreferences | undefined,
  update: OpenTargetPreferences
): OpenTargetPreferences {
  return {
    global: update.global ?? current?.global,
    perProject: { ...(current?.perProject ?? {}), ...(update.perProject ?? {}) }
  }
}

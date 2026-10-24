import type { ProjectActionPreferences, ProjectEnvironment, WorktreeEnvironmentsResult } from '../../shared/projectActions'

const MAX_RECENT_ACTIONS = 50

function stringList(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((entry): entry is string => typeof entry === 'string' && entry !== '') : []
}

export function normalizeProjectActionPreferences(
  value: unknown
): Record<string, ProjectActionPreferences> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const result: Record<string, ProjectActionPreferences> = {}
  for (const [project, raw] of Object.entries(value as Record<string, unknown>)) {
    if (!project || !raw || typeof raw !== 'object' || Array.isArray(raw)) continue
    const entry = raw as Record<string, unknown>
    const preferences: ProjectActionPreferences = {}
    if (entry.selectedEnvironmentId === null || (typeof entry.selectedEnvironmentId === 'string' && entry.selectedEnvironmentId)) {
      preferences.selectedEnvironmentId = entry.selectedEnvironmentId
    }
    if (entry.worktreeEnvironmentId === null || (typeof entry.worktreeEnvironmentId === 'string' && entry.worktreeEnvironmentId)) {
      preferences.worktreeEnvironmentId = entry.worktreeEnvironmentId
    }
    if (entry.recentActions && typeof entry.recentActions === 'object' && !Array.isArray(entry.recentActions)) {
      const recentActions: Record<string, string[]> = {}
      for (const [environmentId, ids] of Object.entries(entry.recentActions as Record<string, unknown>)) {
        const list = stringList(ids).slice(0, MAX_RECENT_ACTIONS)
        if (list.length > 0) recentActions[environmentId] = list
      }
      if (Object.keys(recentActions).length > 0) preferences.recentActions = recentActions
    }
    if (Object.keys(preferences).length > 0) result[project] = preferences
  }
  return Object.keys(result).length > 0 ? result : undefined
}

export function applyProjectActionPreferences<T extends ProjectEnvironment>(
  environments: readonly T[],
  preferences: ProjectActionPreferences | undefined
): { environments: T[]; selectedEnvironmentId: string | null } {
  const ordered = environments.map((environment) => {
    const recent = preferences?.recentActions?.[environment.id] ?? []
    const rank = (id: string): number => {
      const index = recent.indexOf(id)
      return index === -1 ? recent.length : index
    }
    const actions = environment.actions
      .map((action, index) => ({ action, index }))
      .sort((a, b) => rank(a.action.id) - rank(b.action.id) || a.index - b.index)
      .map(({ action }) => action)
    return { ...environment, actions }
  })
  if (preferences?.selectedEnvironmentId === null) return { environments: ordered, selectedEnvironmentId: null }
  const selected = ordered.find((environment) => environment.id === preferences?.selectedEnvironmentId)
    ?? ordered[0]
  return { environments: ordered, selectedEnvironmentId: selected?.id ?? null }
}

export function withRecentAction(
  preferences: ProjectActionPreferences | undefined,
  environmentId: string,
  actionId: string
): ProjectActionPreferences {
  const recent = preferences?.recentActions?.[environmentId] ?? []
  return {
    ...preferences,
    recentActions: {
      ...preferences?.recentActions,
      [environmentId]: [actionId, ...recent.filter((id) => id !== actionId)].slice(0, MAX_RECENT_ACTIONS)
    }
  }
}

export function withSelectedEnvironment(
  preferences: ProjectActionPreferences | undefined,
  environmentId: string | null
): ProjectActionPreferences {
  return { ...preferences, selectedEnvironmentId: environmentId }
}

export function withWorktreeEnvironment(
  preferences: ProjectActionPreferences | undefined,
  environmentId: string | null
): ProjectActionPreferences {
  return { ...preferences, worktreeEnvironmentId: environmentId }
}

export function applyWorktreeEnvironmentPreference(
  environments: readonly ProjectEnvironment[],
  preferences: ProjectActionPreferences | undefined
): WorktreeEnvironmentsResult {
  const stored = preferences?.worktreeEnvironmentId ?? null
  const selected = stored === null ? undefined : environments.find((environment) => environment.id === stored)
  return {
    environments: environments.map(({ id, name, path, isDefault, error }) => ({
      id,
      name,
      path,
      isDefault,
      ...(error === undefined ? {} : { error })
    })),
    selectedEnvironmentId: selected?.id ?? null,
    unavailable: stored !== null && selected === undefined
  }
}

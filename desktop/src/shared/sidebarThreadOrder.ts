import { normalizeWorkspaceProjectKey } from './workspaceProjectKey'

export type SidebarThreadSortMode = 'updated' | 'manual'

export const MAX_MANUAL_THREAD_ORDER_LENGTH = 500

export interface SidebarThreadOrderSettings {
  recentsThreadSort?: SidebarThreadSortMode
  projectsThreadSort?: SidebarThreadSortMode
  pinnedThreadSort?: SidebarThreadSortMode
  recentsShowProjects?: boolean
  recentsThreadOrder?: string[]
  pinnedThreadOrder?: string[]
  threadOrderByProject?: Record<string, string[]>
  projectSort?: SidebarThreadSortMode
  projectOrder?: string[]
  collapsedProjectIds?: string[]
}

export function normalizeSidebarThreadSortMode(
  value: unknown,
  defaultMode: SidebarThreadSortMode
): SidebarThreadSortMode | undefined {
  return (value === 'updated' || value === 'manual') && value !== defaultMode ? value : undefined
}

export function normalizeManualThreadOrder(value: unknown): string[] | undefined {
  if (!Array.isArray(value)) return undefined
  const seen = new Set<string>()
  const ids: string[] = []
  for (const entry of value) {
    if (typeof entry !== 'string') continue
    const id = entry.trim()
    if (!id || /[\u0000-\u001f]/.test(id) || seen.has(id)) continue
    seen.add(id)
    ids.push(id)
    if (ids.length >= MAX_MANUAL_THREAD_ORDER_LENGTH) break
  }
  return ids.length > 0 ? ids : undefined
}

export function normalizeThreadOrderByProject(value: unknown): Record<string, string[]> | undefined {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return undefined
  const normalized: Record<string, string[]> = {}
  for (const [projectKey, order] of Object.entries(value)) {
    const key = normalizeWorkspaceProjectKey(projectKey)
    if (!key) continue
    const ids = normalizeManualThreadOrder(order)
    if (ids) normalized[key] = ids
    else delete normalized[key]
  }
  return Object.keys(normalized).length > 0 ? normalized : undefined
}

export function normalizeProjectKeyList(value: unknown): string[] | undefined {
  if (!Array.isArray(value)) return undefined
  const keys = new Set<string>()
  for (const entry of value) {
    const key = typeof entry === 'string' ? normalizeWorkspaceProjectKey(entry) : ''
    if (key) keys.add(key)
    if (keys.size >= MAX_MANUAL_THREAD_ORDER_LENGTH) break
  }
  return keys.size > 0 ? [...keys] : undefined
}

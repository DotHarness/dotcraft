import { normalizeWorkspaceProjectKey, sameWorkspaceProjectKey } from './workspaceProjectKey'

export const MAX_SIDEBAR_SECTIONS = 50
export const MAX_SIDEBAR_SECTION_ITEMS = 500
export const MAX_SIDEBAR_SECTION_NAME_LENGTH = 80

export type SidebarSectionItem =
  | { kind: 'project'; projectKey: string }
  | { kind: 'thread'; projectKey: string; threadId: string }

export interface SidebarSection {
  id: string
  name: string
  collapsed?: boolean
  items: SidebarSectionItem[]
}

export function sidebarSectionItemKey(item: SidebarSectionItem): string {
  return item.kind === 'project'
    ? `project:${normalizeWorkspaceProjectKey(item.projectKey)}`
    : `thread:${normalizeWorkspaceProjectKey(item.projectKey)}:${item.threadId}`
}

function cleanText(value: unknown): string {
  return typeof value === 'string' ? value.replace(/[\u0000-\u001f]/g, '').trim() : ''
}

function normalizeItem(value: unknown): SidebarSectionItem | null {
  if (value == null || typeof value !== 'object') return null
  const raw = value as Record<string, unknown>
  const projectKey = typeof raw.projectKey === 'string' ? normalizeWorkspaceProjectKey(raw.projectKey) : ''
  if (!projectKey) return null
  if (raw.kind === 'project') return { kind: 'project', projectKey }
  const threadId = cleanText(raw.threadId)
  return raw.kind === 'thread' && threadId ? { kind: 'thread', projectKey, threadId } : null
}

/** Keeps the first placement of each item so an item belongs to at most one section. */
export function normalizeSidebarSections(value: unknown): SidebarSection[] | undefined {
  if (!Array.isArray(value)) return undefined
  const sectionIds = new Set<string>()
  const placed = new Set<string>()
  const sections: SidebarSection[] = []
  for (const entry of value) {
    if (sections.length >= MAX_SIDEBAR_SECTIONS) break
    if (entry == null || typeof entry !== 'object') continue
    const raw = entry as Record<string, unknown>
    const id = cleanText(raw.id)
    const name = cleanText(raw.name).slice(0, MAX_SIDEBAR_SECTION_NAME_LENGTH)
    if (!id || !name || sectionIds.has(id)) continue
    sectionIds.add(id)
    const items: SidebarSectionItem[] = []
    for (const candidate of Array.isArray(raw.items) ? raw.items : []) {
      const item = normalizeItem(candidate)
      if (!item) continue
      const key = sidebarSectionItemKey(item)
      if (placed.has(key)) continue
      placed.add(key)
      items.push(item)
      if (items.length >= MAX_SIDEBAR_SECTION_ITEMS) break
    }
    sections.push({ id, name, ...(raw.collapsed === true ? { collapsed: true } : {}), items })
  }
  return sections.length > 0 ? sections : undefined
}

export function forgetProjectInSidebarSections(
  sections: SidebarSection[] | undefined,
  projectPath: string
): SidebarSection[] | undefined {
  return sections?.map((section) => ({
    ...section,
    items: section.items.filter((item) => !sameWorkspaceProjectKey(item.projectKey, projectPath))
  }))
}

import { normalizeWorkspaceProjectKey } from '../../../../shared/workspaceProjectKey'
import {
  sidebarSectionItemKey,
  type SidebarSection,
  type SidebarSectionItem
} from '../../../../shared/sidebarSections'

export interface SectionPlacement {
  sectionIdByItemKey: Map<string, string>
  hasProject(projectKey: string): boolean
  threadIds(projectKey: string): string[]
}

export function buildSectionPlacement(sections: readonly SidebarSection[]): SectionPlacement {
  const sectionIdByItemKey = new Map<string, string>()
  const projects = new Set<string>()
  const threadsByProject = new Map<string, string[]>()
  for (const section of sections) {
    for (const item of section.items) {
      sectionIdByItemKey.set(sidebarSectionItemKey(item), section.id)
      const projectKey = normalizeWorkspaceProjectKey(item.projectKey)
      if (item.kind === 'project') {
        projects.add(projectKey)
      } else {
        threadsByProject.set(projectKey, [...(threadsByProject.get(projectKey) ?? []), item.threadId])
      }
    }
  }
  return {
    sectionIdByItemKey,
    hasProject: (projectKey) => projects.has(normalizeWorkspaceProjectKey(projectKey)),
    threadIds: (projectKey) => threadsByProject.get(normalizeWorkspaceProjectKey(projectKey)) ?? []
  }
}

export function sectionOfItem(sections: readonly SidebarSection[], item: SidebarSectionItem): SidebarSection | undefined {
  const key = sidebarSectionItemKey(item)
  return sections.find((section) => section.items.some((candidate) => sidebarSectionItemKey(candidate) === key))
}

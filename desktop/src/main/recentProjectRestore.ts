import { normalizeWorkspaceProjectKey } from '../shared/workspaceProjectKey'
import type { HubProject } from './desktopHub'
import type { LocalProjectList } from './localProjects'

const PROJECT_RESTORE_LIMIT = 4
const PROJECT_RESTORE_CONCURRENCY = 2

export type ProjectRestoreState = { state: 'connecting' } | { state: 'error'; errorMessage: string }

export interface RecentProjectRestoreHost {
  foregroundPath: string
  isChatWorkspace(path: string): boolean
  isActive(): boolean
  projects: Pick<LocalProjectList, 'refresh' | 'list' | 'has'>
  isEligible(path: string): boolean
  startProject(path: string): Promise<void>
  setProjectState(path: string, state: ProjectRestoreState | undefined): void
}

export async function restoreRecentProjects(host: RecentProjectRestoreHost): Promise<void> {
  if (!host.isActive() || !await host.projects.refresh() || !host.isActive()) return
  const foregroundKey = normalizeWorkspaceProjectKey(host.foregroundPath)
  const slots = PROJECT_RESTORE_LIMIT - (host.isChatWorkspace(host.foregroundPath) ? 0 : 1)
  const candidates: HubProject[] = []
  const ranked = [...host.projects.list()]
    .sort((left, right) => Date.parse(right.lastOpenedAt) - Date.parse(left.lastOpenedAt))
  for (const project of ranked) {
    if (normalizeWorkspaceProjectKey(project.path) === foregroundKey ||
      host.isChatWorkspace(project.path) || !host.isEligible(project.path)) continue
    candidates.push(project)
    if (candidates.length === slots) break
  }
  let next = 0
  const worker = async (): Promise<void> => {
    while (host.isActive()) {
      const project = candidates[next++]
      if (!project) return
      if (!host.projects.has(project.path) || !host.isEligible(project.path)) continue
      host.setProjectState(project.path, { state: 'connecting' })
      try {
        await host.startProject(project.path)
        host.setProjectState(project.path, undefined)
      } catch (error) {
        const errorMessage = error instanceof Error ? error.message : String(error)
        if (host.isActive()) {
          host.setProjectState(project.path, { state: 'error', errorMessage })
          console.warn('[desktop] failed to restore recent project', { workspacePath: project.path, errorMessage })
        } else {
          host.setProjectState(project.path, undefined)
        }
      }
    }
  }
  await Promise.all(Array.from({ length: PROJECT_RESTORE_CONCURRENCY }, worker))
}

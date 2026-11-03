import { buildOratorioProjectDisplayOptions } from './settings/oratorio-project-display'
import type { OratorioProjectConfig } from './settings/oratorio-settings-model'

export interface LocalTaskRepositoryOption {
  value: string
  label: string
}

export function localTaskRepositoryOptions(projects: OratorioProjectConfig[]): LocalTaskRepositoryOption[] {
  const runnable = projects.filter((project) => project.enabled && project.workspacePath.trim())
  const labels = new Map(buildOratorioProjectDisplayOptions(runnable).map((option) => [option.projectId, option.label]))
  return runnable.map((project) => ({
    value: project.provider === 'github' ? project.projectKey : project.routeProjectKey ?? project.projectKey,
    label: labels.get(project.id) ?? project.projectKey,
  }))
}

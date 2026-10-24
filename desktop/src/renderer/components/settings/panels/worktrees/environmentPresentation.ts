import type { MessageKey } from '../../../../../shared/locales'
import type { ProjectActionPlatform } from '../../../../../shared/projectActions'
import type { SegmentedOption } from '../../ui/SegmentedControl'
import type { ScriptTab } from './environmentDraft'

type Translate = (key: MessageKey, vars?: Record<string, string | number>) => string

export const PLATFORM_LABELS: Record<ProjectActionPlatform, string> = {
  win32: 'Windows',
  darwin: 'macOS',
  linux: 'Linux'
}

export function scriptTabOptions(t: Translate, tabs: readonly ScriptTab[]): SegmentedOption<ScriptTab>[] {
  return tabs.map((tab) => ({
    value: tab,
    label: tab === 'default' ? t('settings.environments.platform.default') : PLATFORM_LABELS[tab]
  }))
}

export const ENVIRONMENT_VARIABLES: ReadonlyArray<{ name: string; descriptionKey: MessageKey }> = [
  { name: 'DOTCRAFT_SOURCE_TREE_PATH', descriptionKey: 'settings.environments.variables.source' },
  { name: 'DOTCRAFT_WORKTREE_PATH', descriptionKey: 'settings.environments.variables.worktree' }
]

export function projectFolderName(projectPath: string): string {
  return projectPath.replace(/[\\/]+$/, '').split(/[\\/]/).pop() || projectPath
}

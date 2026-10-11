import type { DesktopPluginOpenPathOptions, DesktopPluginOpenTarget } from '@dotcraft/plugin'

import { translate } from '../../shared/locales'
import { resolveEffectiveOpenTarget, type OpenTargetInfo } from '../../shared/openTargets'
import { useConversationStore } from '../stores/conversationStore'
import { toAbsoluteWorkspacePath } from '../utils/workspacePaths'
import { readDesktopPluginEnvironment } from './desktopPluginEnvironment'

async function listLocalTargets(): Promise<OpenTargetInfo[]> {
  if (useConversationStore.getState().remoteWorkspaceActive) {
    throw new Error('Open targets are not available in a remote workspace')
  }
  return (await window.api.shell.listOpenTargets()).filter((target) => !target.hidden)
}

export async function listDesktopPluginOpenTargets(): Promise<DesktopPluginOpenTarget[]> {
  const { locale } = readDesktopPluginEnvironment()
  return (await listLocalTargets()).map((target) => ({
    id: target.id,
    label: target.labelKey ? translate(locale, target.labelKey) : target.label,
    ...(target.iconDataUrl ? { icon: target.iconDataUrl } : {})
  }))
}

export async function openDesktopPluginPath(path: string, options: DesktopPluginOpenPathOptions = {}): Promise<void> {
  const targets = await listLocalTargets()
  const projectRoot = useConversationStore.getState().workspacePath
  const target = options.target
    ? targets.find((candidate) => candidate.id === options.target)
    : resolveEffectiveOpenTarget(targets, (await window.api.settings.get()).openTargetPreferences, projectRoot)
  if (!target) throw new Error(`Open target is not available: ${options.target ?? 'default'}`)
  await window.api.shell.openInTarget({
    targetId: target.id,
    path: projectRoot ? toAbsoluteWorkspacePath(projectRoot, path) : path,
    ...(options.line ? { line: options.line } : {}),
    ...(options.column ? { column: options.column } : {})
  })
}

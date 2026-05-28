import type { PluginEntry } from '../stores/pluginStore'
import { AGENT_TEAMS_PLUGIN_ID } from './agentTeamsPlugin'

interface PluginDesktopExtensionContent {
  key: string
  title: string
  kind: string
  description: string
}

type Translate = (key: string, vars?: Record<string, string | number>) => string

export function getPluginDesktopExtensionContents(
  plugin: PluginEntry,
  t: Translate
): PluginDesktopExtensionContent[] {
  if (plugin.id !== AGENT_TEAMS_PLUGIN_ID) return []

  return [
    {
      key: 'desktop-extension:team-card-board',
      title: t('plugins.content.agentTeams.teamCardBoard.title'),
      kind: t('plugins.content.desktopExtension'),
      description: t('plugins.content.agentTeams.teamCardBoard.description')
    }
  ]
}

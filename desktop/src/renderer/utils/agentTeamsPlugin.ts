import type { PluginEntry } from '../stores/pluginStore'

export const AGENT_TEAMS_PLUGIN_ID = 'agent-teams'

export function isAgentTeamsPluginEnabled(plugins: PluginEntry[]): boolean {
  return plugins.some((plugin) =>
    plugin.id === AGENT_TEAMS_PLUGIN_ID &&
    plugin.installed === true &&
    plugin.enabled === true
  )
}

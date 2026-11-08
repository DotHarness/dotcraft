import type { PluginEntry } from '../../stores/pluginStore'
import { stageChatDraft } from '../conversation/skillRequestDraft'
import { pluginTitle } from './PluginCatalogItem'

export const PLUGIN_CREATOR_SKILL = 'plugin-creator'

export function stagePluginTryInChat(plugin: PluginEntry): void {
  const prompt = plugin.interface?.defaultPrompt || ''
  const enabledSkills = plugin.skills.filter((skill) => skill.enabled)
  const skillName = enabledSkills.find((skill) => skill.name === plugin.id)?.name
    ?? (enabledSkills.length === 1 ? enabledSkills[0]!.name : null)
  const text = skillName ? `$${skillName}${prompt ? ` ${prompt}` : ''}` : (prompt || pluginTitle(plugin))
  stageChatDraft(text, skillName ? [{ type: 'skill', skillName }] : [])
}

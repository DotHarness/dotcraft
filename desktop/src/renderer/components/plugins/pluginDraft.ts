import type { PluginEntry } from '../../stores/pluginStore'
import { useUIStore } from '../../stores/uiStore'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import { stringifyComposerDraftSegments } from '../conversation/richInputSerialization'
import { pluginTitle } from './PluginCatalogItem'

export const PLUGIN_CREATOR_SKILL = 'plugin-creator'

export function stagePluginCreationInChat(prompt: string, includeCreatorSkill: boolean): void {
  // The segment list is the composer's content model and the plain text is only its
  // serialization, so the prompt has to be a segment of its own — text passed beside a
  // lone skill segment is dropped. Deriving the text from the segments keeps the two
  // from drifting apart.
  const segments: ComposerDraftSegment[] = includeCreatorSkill
    ? [{ type: 'skill', skillName: PLUGIN_CREATOR_SKILL }, { type: 'text', value: ` ${prompt}` }]
    : [{ type: 'text', value: prompt }]
  stagePluginDraft(stringifyComposerDraftSegments(segments), segments)
}

export function stagePluginTryInChat(plugin: PluginEntry): void {
  const prompt = plugin.interface?.defaultPrompt || ''
  const enabledSkills = plugin.skills.filter((skill) => skill.enabled)
  const skillName = enabledSkills.find((skill) => skill.name === plugin.id)?.name
    ?? (enabledSkills.length === 1 ? enabledSkills[0]!.name : null)
  const text = skillName ? `$${skillName}${prompt ? ` ${prompt}` : ''}` : (prompt || pluginTitle(plugin))
  stagePluginDraft(text, skillName ? [{ type: 'skill', skillName }] : [])
}

function stagePluginDraft(text: string, segments: ComposerDraftSegment[]): void {
  const ui = useUIStore.getState()
  const existing = ui.welcomeDraft
  ui.setWelcomeDraft({
    text,
    segments,
    selectionStart: text.length,
    selectionEnd: text.length,
    images: [],
    files: [],
    mode: existing?.mode ?? 'agent',
    model: existing?.model || 'Default',
    approvalPolicy: existing?.approvalPolicy ?? 'default'
  })
  ui.goToNewChat()
}

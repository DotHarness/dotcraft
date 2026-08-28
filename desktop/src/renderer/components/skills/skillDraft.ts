import { useUIStore } from '../../stores/uiStore'
import type { SkillEntry } from '../../stores/skillsStore'
import { stringifyComposerDraftSegments } from '../conversation/richInputSerialization'
import type { ComposerDraftSegment } from '../../types/composerDraft'

/**
 * Shared so the skills catalog and a plugin's contents list cannot drift apart on
 * how the mention is built: segments are the composer's content, text only their
 * serialization.
 */
export function stageSkillTryInChat(skill: SkillEntry): void {
  const segments: ComposerDraftSegment[] = [{ type: 'skill', skillName: skill.name }]
  const text = stringifyComposerDraftSegments(segments)
  const ui = useUIStore.getState()
  const existing = ui.welcomeDraft
  ui.setWelcomeDraft({
    text,
    segments,
    selectionStart: 1,
    selectionEnd: 1,
    images: [],
    files: [],
    mode: existing?.mode ?? 'agent',
    model: existing?.model || 'Default',
    approvalPolicy: existing?.approvalPolicy ?? 'default'
  })
  ui.goToNewChat()
}

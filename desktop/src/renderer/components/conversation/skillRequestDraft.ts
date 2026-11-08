import { useSkillsStore } from '../../stores/skillsStore'
import { useUIStore } from '../../stores/uiStore'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import { stringifyComposerDraftSegments } from './richInputSerialization'

export async function stageSkillRequestInChat(skillName: string, prompt: string): Promise<void> {
  let available = useSkillsStore.getState().skills
  if (available.length === 0) {
    try {
      await useSkillsStore.getState().fetchSkills()
      available = useSkillsStore.getState().skills
    } catch {
      available = []
    }
  }

  // Text beside a lone skill segment is dropped, so the prompt needs its own segment.
  const segments: ComposerDraftSegment[] = available.some((skill) => skill.name === skillName)
    ? [{ type: 'skill', skillName }, { type: 'text', value: ` ${prompt}` }]
    : [{ type: 'text', value: prompt }]
  stageChatDraft(stringifyComposerDraftSegments(segments), segments)
}

export function stageChatDraft(text: string, segments: ComposerDraftSegment[]): void {
  const ui = useUIStore.getState()
  const existing = ui.welcomeDraft
  ui.stageWelcomeDraft({
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
}

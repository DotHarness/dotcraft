import { useUIStore } from '../../stores/uiStore'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import { stringifyComposerDraftSegments } from '../conversation/richInputSerialization'

export const AUTOMATIONS_SKILL = 'automations'

export function stageAutomationCreationInWelcome(prompt: string): void {
  const instruction = prompt.trim() || 'Help me create an automation.'
  const segments: ComposerDraftSegment[] = [
    { type: 'skill', skillName: AUTOMATIONS_SKILL },
    { type: 'text', value: ` ${instruction}` }
  ]
  const text = stringifyComposerDraftSegments(segments)
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

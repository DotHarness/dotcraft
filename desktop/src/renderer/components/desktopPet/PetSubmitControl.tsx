import { Mic, RotateCcw, Square } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ComposerSubmitButton } from '../conversation/ComposerSubmitButton'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { PetFollowUpMode, PetStatusInfo, PetVoice, PetVoiceAction } from '../../../shared/desktopPet'

/** Pet mode's only submit control: it sends, steers or queues a draft, stops the running turn when the draft is empty, and otherwise dictates into the source composer. */
export function PetSubmitControl({ status, followUpMode, hasDraft, busy, voice, onSubmit, onStop, onVoice }: {
  status: PetStatusInfo | undefined
  followUpMode: PetFollowUpMode
  hasDraft: boolean
  busy: boolean
  voice?: PetVoice
  onSubmit: () => void
  onStop: () => void
  onVoice?: (action: PetVoiceAction) => void
}): JSX.Element {
  const running = status?.status === 'running'
  if (voice === 'recording') return <PetVoiceButton kind="recording" onClick={() => onVoice?.('stop')} />
  if (voice === 'processing') return <PetVoiceButton kind="processing" disabled />
  if (running && hasDraft) return <ComposerSubmitButton mode={followUpMode} disabled={busy} onClick={onSubmit} />
  if (running && status.stopping) return <ComposerSubmitButton mode="stopping" tone="enabled" disabled onClick={() => {}} />
  if (running && status.canStop) return <ComposerSubmitButton mode="stop" tone="enabled" onClick={onStop} />
  if (!hasDraft && voice === 'retryable') return <PetVoiceButton kind="retry" disabled={busy} onClick={() => onVoice?.('retry')} />
  if (!hasDraft && voice === 'idle') return <PetVoiceButton kind="start" disabled={busy} onClick={() => onVoice?.('start')} />
  return <ComposerSubmitButton mode="send" disabled={busy || !hasDraft} onClick={onSubmit} />
}

const VOICE_LABEL = {
  start: 'voice.control.dictate',
  recording: 'voice.control.stop',
  processing: 'voice.control.processing',
  retry: 'voice.control.retry'
} as const

function PetVoiceButton({ kind, disabled = false, onClick }: {
  kind: keyof typeof VOICE_LABEL
  disabled?: boolean
  onClick?: () => void
}): JSX.Element {
  const t = useT()
  const label = t(VOICE_LABEL[kind])
  return <ActionTooltip label={label} placement="top">
    <button type="button" className="dc-composer-icon-control" style={{ width: 32, height: 32 }}
      aria-label={label} aria-pressed={kind === 'recording' ? true : undefined} disabled={disabled} onClick={onClick}>
      {kind === 'start'
        ? <Mic size={16} aria-hidden />
        : kind === 'retry'
          ? <RotateCcw size={16} aria-hidden />
          : <Square size={11} fill="currentColor" strokeWidth={0} aria-hidden style={{ display: 'block' }} />}
    </button>
  </ActionTooltip>
}

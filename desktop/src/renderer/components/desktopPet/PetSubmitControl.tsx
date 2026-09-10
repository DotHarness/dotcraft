import { ComposerSubmitButton } from '../conversation/ComposerSubmitButton'
import type { PetFollowUpMode, PetStatusInfo } from '../../../shared/desktopPet'

/** Pet mode's only submit control: it sends, steers or queues a draft, and stops the running turn when the draft is empty. */
export function PetSubmitControl({ status, followUpMode, hasDraft, busy, onSubmit, onStop }: {
  status: PetStatusInfo | undefined
  followUpMode: PetFollowUpMode
  hasDraft: boolean
  busy: boolean
  onSubmit: () => void
  onStop: () => void
}): JSX.Element {
  const running = status?.status === 'running'
  if (running && hasDraft) return <ComposerSubmitButton mode={followUpMode} disabled={busy} onClick={onSubmit} />
  if (running && status.stopping) return <ComposerSubmitButton mode="stopping" tone="enabled" disabled onClick={() => {}} />
  if (running && status.canStop) return <ComposerSubmitButton mode="stop" tone="enabled" onClick={onStop} />
  return <ComposerSubmitButton mode="send" disabled={busy || !hasDraft} onClick={onSubmit} />
}

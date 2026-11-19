import { memo, useState, type JSX } from 'react'
import { X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { addToast } from '../../stores/toastStore'
import { useThreadTasks } from '../../stores/threadTasksStore'
import { sendTextHere } from '../../utils/sendTextHere'
import { turnNextStep } from '../../utils/threadToolDisplay'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import css from './TurnNextStep.module.css'

export const TurnNextStep = memo(function TurnNextStep({ turnId }: { turnId: string }): JSX.Element | null {
  const t = useT()
  const turn = useConversationStore((s) => s.turns.find((candidate) => candidate.id === turnId))
  const latest = useConversationStore((s) => s.turns.at(-1)?.id === turnId)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const [busy, setBusy] = useState(false)
  const [replied, setReplied] = useState(false)
  const step = turn?.status === 'completed' && latest ? turnNextStep(turn.items) : null
  const snapshot = useThreadTasks(step ? turn?.threadId ?? '' : '')
  if (!turn || !step || replied || snapshot?.dismissedQuestionCallIds?.includes(step.callId)) return null

  const dismiss = async (): Promise<void> => {
    setBusy(true)
    try {
      const result = await window.api.threadTasks.dismissQuestion({ threadId: turn.threadId, callId: step.callId })
      if (!result.ok) throw new Error(result.error)
    } catch (error) {
      addToast(error instanceof Error ? error.message : String(error), 'error')
    } finally {
      setBusy(false)
    }
  }

  const reply = async (label: string): Promise<void> => {
    setReplied(true)
    try {
      await sendTextHere(turn.threadId, workspacePath, label, t('threadActionCard.untitled'))
    } catch (error) {
      setReplied(false)
      addToast(error instanceof Error ? error.message : String(error), 'error')
    }
  }

  const [primary, ...others] = step.actions
  return (
    <div className={css.card} role="group" aria-label={step.ask}>
      <div className={css.header}>
        <div className={css.text}>
          {step.recap ? <p className={css.recap}>{step.recap}</p> : null}
          <p className={css.ask}>{step.ask}</p>
        </div>
        {step.blocked ? null : (
          <IconButton icon={<X size={16} />} label={t('common.close')} className={css.close}
            disabled={busy} onClick={() => { void dismiss() }} />
        )}
      </div>
      <div className={css.buttons}>
        {others.map((label) => (
          <Button key={label} variant="ghost" size="sm" disabled={busy} onClick={() => { void reply(label) }}>{label}</Button>
        ))}
        <Button variant="primary" size="sm" disabled={busy} onClick={() => { void reply(primary) }}>{primary}</Button>
      </div>
    </div>
  )
})

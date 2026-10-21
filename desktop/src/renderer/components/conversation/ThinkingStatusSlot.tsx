import { useT } from '../../contexts/LocaleContext'
import { ThinkingIndicator } from './ThinkingIndicator'
import type { TurnThinkingStatus } from './turnThinkingStatus'
import styles from './ThinkingStatusSlot.module.css'

/** The running turn's one Thinking status: it keeps its height and fades rather than mounting, so content below never jumps. */
export function ThinkingStatusSlot({ status }: { status: TurnThinkingStatus }): JSX.Element {
  const t = useT()
  const label = status.waitingForAnswer ? t('conversation.thinking.waitingForAnswer') : status.heading
  return (
    <div
      className={styles.slot}
      data-visible={status.visible ? 'true' : 'false'}
      aria-hidden={!status.visible}
      data-testid="thinking-status-slot"
    >
      <ThinkingIndicator streaming statusText={label} />
    </div>
  )
}

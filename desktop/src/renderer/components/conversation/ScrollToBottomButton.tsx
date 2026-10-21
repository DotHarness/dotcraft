import { ArrowDown } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import styles from './ScrollToBottomButton.module.css'

interface ScrollToBottomButtonProps {
  onClick: () => void
  bottomOffsetPx?: number
  working?: boolean
}

export function ScrollToBottomButton({ onClick, bottomOffsetPx = 10, working = false }: ScrollToBottomButtonProps): JSX.Element {
  return (
    <ActionTooltip
      label="Scroll to bottom"
      placement="top"
      wrapperStyle={{
        position: 'absolute',
        bottom: `${bottomOffsetPx}px`,
        left: '50%',
        transform: 'translateX(-50%)',
        zIndex: 10
      }}
    >
      <button type="button" className={styles.button} onClick={onClick} aria-label="Scroll to bottom">
        {working ? (
          <span className={styles.dots} aria-hidden="true">
            <span className={styles.dot} />
            <span className={styles.dot} />
            <span className={styles.dot} />
          </span>
        ) : (
          <ArrowDown size={18} strokeWidth={1.9} aria-hidden="true" />
        )}
      </button>
    </ActionTooltip>
  )
}

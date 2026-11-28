import type { ReactNode } from 'react'
import { Pause } from 'lucide-react'
import type { OwnedThreadRecord } from '../../../shared/ownedThreads'
import { useT } from '../../contexts/LocaleContext'
import { toggleOwnedThreadPaused } from '../../utils/ownedThreadLifecycle'
import { ActionTooltip } from '../ui/ActionTooltip'
import styles from './OwnedThreadPausedBar.module.css'

export function OwnedThreadPausedBar({
  record,
  children
}: {
  record: OwnedThreadRecord
  children?: ReactNode
}): JSX.Element {
  const t = useT()

  return (
    <div className={styles.container} data-testid="conductor-paused-bar">
      {children && (
        <div className={styles.composerHidden} aria-hidden="true">
          {children}
        </div>
      )}
      <div className={styles.overlay}>
        <ActionTooltip label={t('conductor.resume')} placement="top">
          <button
            type="button"
            className={styles.resumeButton}
            aria-label={t('conductor.resume')}
            data-testid="conductor-paused-bar-resume"
            onClick={() => { void toggleOwnedThreadPaused(record, t) }}
          >
            <Pause size={14} strokeWidth={2.2} className={styles.icon} aria-hidden />
            <span>{t('conductor.paused')}</span>
          </button>
        </ActionTooltip>
      </div>
    </div>
  )
}

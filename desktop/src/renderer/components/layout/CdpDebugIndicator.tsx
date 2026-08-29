import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import styles from './CdpDebugIndicator.module.css'

export function CdpDebugIndicator({ enabled }: { enabled: boolean }): JSX.Element | null {
  if (!enabled) return null

  return (
    <div className={styles.anchor}>
      <CdpDebugSignal />
    </div>
  )
}

export function CdpDebugSignal(): JSX.Element {
  const t = useT()
  const label = t('debug.cdpEnabledTooltip')
  return (
    <ActionTooltip label={label} placement="top">
      <span className={styles.indicator} role="status" aria-label={label} tabIndex={0}>
        <span className={styles.signal} aria-hidden="true" />
      </span>
    </ActionTooltip>
  )
}

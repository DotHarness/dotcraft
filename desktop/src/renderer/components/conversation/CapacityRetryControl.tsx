import { useEffect, useRef, useState } from 'react'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import styles from './CapacityRetryControl.module.css'

interface CapacityRetryControlProps {
  delaySeconds: number | null
  busy?: boolean
  onRetry: () => void
}

export function CapacityRetryControl({
  delaySeconds,
  busy = false,
  onRetry
}: CapacityRetryControlProps): JSX.Element {
  const locale = useLocale()
  const [deadline] = useState(() => (delaySeconds == null ? null : Date.now() + delaySeconds * 1000))
  const [remaining, setRemaining] = useState(delaySeconds ?? 0)
  const firedRef = useRef(false)

  useEffect(() => {
    if (deadline == null || busy) return undefined

    const tick = (): void => {
      const left = Math.max(0, Math.ceil((deadline - Date.now()) / 1000))
      setRemaining(left)
      if (left === 0 && !firedRef.current) {
        firedRef.current = true
        onRetry()
      }
    }

    tick()
    const timer = window.setInterval(tick, 1000)
    return () => window.clearInterval(timer)
  }, [deadline, busy, onRetry])

  const counting = deadline != null && remaining > 0
  const label = counting
    ? translate(locale, 'conversation.providerRetry.retryCountdown', { seconds: remaining })
    : translate(locale, 'conversation.providerRetry.retry')
  const progress = counting && delaySeconds ? (delaySeconds - remaining) / delaySeconds : 0

  return (
    <div className={styles.control}>
      <button
        type="button"
        className={styles.button}
        disabled={busy}
        onClick={() => {
          firedRef.current = true
          onRetry()
        }}
      >
        {counting && (
          <span className={styles.fill} style={{ transform: `scaleX(${progress})` }} aria-hidden />
        )}
        <span className={styles.label}>{label}</span>
      </button>
    </div>
  )
}

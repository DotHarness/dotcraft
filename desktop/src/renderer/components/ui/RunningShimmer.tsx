import { useEffect, useRef, type HTMLAttributes, type ReactNode } from 'react'
import { prefersReducedMotion } from '../../utils/appearance'
import styles from './RunningShimmer.module.css'

const FIRST_SWEEP_DELAY_MS = 600
const SWEEP_INTERVAL_MS = 4_000
const SWEEP_DURATION_MS = 1_000

interface RunningShimmerProps extends HTMLAttributes<HTMLElement> {
  as?: 'span' | 'div'
  active?: boolean
  reducedMotion?: boolean
  children: ReactNode
}

/** Children are copied into an aria-hidden overlay, so pass text to a block-level root without padding. */
export function RunningShimmer({
  as: Tag = 'span',
  active = true,
  reducedMotion = false,
  className,
  children,
  ...rest
}: RunningShimmerProps): JSX.Element {
  const rootRef = useRef<HTMLDivElement & HTMLSpanElement>(null)
  // Without matchMedia the OS motion preference is unknowable.
  const sweeps = active && !reducedMotion && typeof window.matchMedia === 'function'

  useEffect(() => {
    const root = rootRef.current
    if (!sweeps || !root) return
    let interval: number | undefined
    let end: number | undefined
    const sweep = (): void => {
      if (prefersReducedMotion()) return
      root.setAttribute('data-sweep', '')
      end = window.setTimeout(() => root.removeAttribute('data-sweep'), SWEEP_DURATION_MS)
    }
    const start = window.setTimeout(() => {
      sweep()
      interval = window.setInterval(sweep, SWEEP_INTERVAL_MS)
    }, FIRST_SWEEP_DELAY_MS)
    return () => {
      window.clearTimeout(start)
      window.clearInterval(interval)
      window.clearTimeout(end)
      root.removeAttribute('data-sweep')
    }
  }, [sweeps])

  return (
    <Tag
      {...rest}
      ref={rootRef}
      className={active ? (className ? `${styles.root} ${className}` : styles.root) : className}
      data-running-shimmer={active ? '' : undefined}
    >
      {children}
      {sweeps && (
        <span className={styles.sweep} aria-hidden="true">
          <span className={styles.highlight}>{children}</span>
        </span>
      )}
    </Tag>
  )
}

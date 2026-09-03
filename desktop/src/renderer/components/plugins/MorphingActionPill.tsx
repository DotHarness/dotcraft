import { useLayoutEffect, useRef, useState } from 'react'
import type { JSX, ReactNode } from 'react'
import { PluginInstallButton } from './PluginInstallButton'
import styles from './MorphingActionPill.module.css'

/** The off-screen copy measures the next label so the visible slot can transition to that width. */
export function MorphingActionPill({
  label,
  iconLeft,
  loading = false,
  disabled = false,
  onClick
}: {
  label: string
  iconLeft?: ReactNode
  loading?: boolean
  disabled?: boolean
  onClick: () => void
}): JSX.Element {
  const measureRef = useRef<HTMLSpanElement>(null)
  const [width, setWidth] = useState<number | null>(null)
  const [transitionReady, setTransitionReady] = useState(false)

  useLayoutEffect(() => {
    const nextWidth = measureRef.current?.getBoundingClientRect().width
    if (nextWidth == null || nextWidth === 0) return
    setWidth(nextWidth)
    if (!transitionReady) {
      const frame = window.requestAnimationFrame(() => setTransitionReady(true))
      return () => window.cancelAnimationFrame(frame)
    }
  }, [label, loading, transitionReady])

  return (
    <>
      <span ref={measureRef} className={styles.measure} aria-hidden="true">
        <PluginInstallButton variant="primary" loading={loading} disabled tabIndex={-1} iconLeft={iconLeft}>
          {label}
        </PluginInstallButton>
      </span>
      <span
        className={styles.slot}
        data-transition-ready={transitionReady ? 'true' : 'false'}
        style={width == null ? undefined : { width }}
      >
        <PluginInstallButton
          variant="primary"
          loading={loading}
          aria-busy={loading}
          disabled={disabled}
          onClick={onClick}
          iconLeft={iconLeft}
          className={styles.button}
        >
          {label}
        </PluginInstallButton>
      </span>
    </>
  )
}

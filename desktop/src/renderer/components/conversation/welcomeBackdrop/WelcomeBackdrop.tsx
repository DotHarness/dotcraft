import { useEffect, useRef, useState, type JSX, type RefObject } from 'react'
import { prefersReducedMotion } from '../../../utils/appearance'
import { startWelcomeBackdrop } from './backdropEngine'
import styles from './WelcomeBackdrop.module.css'

interface BackdropPreference {
  on: boolean
  still: boolean
}

const drawable = (): boolean =>
  typeof window.matchMedia === 'function' && typeof window.WebGL2RenderingContext === 'function'

function readPreference(): BackdropPreference {
  if (!drawable()) return { on: false, still: true }
  return {
    on: document.documentElement.dataset.welcomeBackdrop !== 'false',
    still: prefersReducedMotion()
  }
}

function useBackdropPreference(): BackdropPreference {
  const [preference, setPreference] = useState(readPreference)
  useEffect(() => {
    if (!drawable()) return
    const sync = (): void => {
      const next = readPreference()
      setPreference((prev) => (prev.on === next.on && prev.still === next.still ? prev : next))
    }
    const observer = new MutationObserver(sync)
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-welcome-backdrop', 'data-reduce-motion']
    })
    const motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)')
    motionQuery.addEventListener('change', sync)
    return () => {
      observer.disconnect()
      motionQuery.removeEventListener('change', sync)
    }
  }, [])
  return preference
}

export function WelcomeBackdrop({ calmRef }: { calmRef: RefObject<HTMLElement | null> }): JSX.Element | null {
  const { on, still } = useBackdropPreference()
  const hostRef = useRef<HTMLDivElement>(null)
  const [shown, setShown] = useState(false)

  useEffect(() => {
    const host = hostRef.current
    if (!host || !on) return
    const backdrop = startWelcomeBackdrop(host, {
      still,
      canvasClassName: styles.canvas ?? '',
      calm: () => calmRef.current,
      onShown: () => setShown(true)
    })
    return () => {
      backdrop?.destroy()
      setShown(false)
    }
  }, [calmRef, on, still])

  if (!on) return null
  return <div ref={hostRef} aria-hidden className={styles.layer} data-shown={shown ? 'true' : 'false'} />
}

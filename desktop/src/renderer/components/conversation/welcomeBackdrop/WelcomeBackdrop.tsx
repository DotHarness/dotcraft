import { useEffect, useLayoutEffect, useRef, useState, type JSX, type ReactNode, type RefObject } from 'react'
import { prefersReducedMotion } from '../../../utils/appearance'
import { startWelcomeBackdrop, type WelcomeBackdropHandle } from './backdropEngine'
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

interface WelcomeBackdropProps {
  active: boolean
  calmRef: RefObject<HTMLElement | null>
}

function WelcomeBackdrop({ active, calmRef }: WelcomeBackdropProps): JSX.Element | null {
  const { on, still } = useBackdropPreference()
  const hostRef = useRef<HTMLDivElement>(null)
  const backdropRef = useRef<WelcomeBackdropHandle | null>(null)
  const [shown, setShown] = useState(false)

  useEffect(() => {
    const host = hostRef.current
    if (!host || !on || !active || backdropRef.current) return
    backdropRef.current = startWelcomeBackdrop(host, {
      active,
      still,
      canvasClassName: styles.canvas ?? '',
      calm: () => calmRef.current,
      onShown: () => setShown(true)
    })
  }, [active, calmRef, on, still])

  useEffect(() => () => {
    backdropRef.current?.destroy()
    backdropRef.current = null
    setShown(false)
  }, [calmRef, on, still])

  useLayoutEffect(() => {
    backdropRef.current?.setActive(active)
  }, [active])

  if (!on) return null
  return <div ref={hostRef} aria-hidden className={styles.layer} data-active={active} data-shown={shown} />
}

export function ConversationBackdrop({ children, ...props }: WelcomeBackdropProps & { children: ReactNode }): JSX.Element {
  return <div className={styles.surface}>
    <WelcomeBackdrop {...props} />
    <div className={styles.content}>{children}</div>
  </div>
}

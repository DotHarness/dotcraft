import type { ReactNode, RefObject } from 'react'
import { Pause, SatelliteDish } from 'lucide-react'
import type { ScreenViewStatus } from '../../../../shared/screenView'
import { screenViewSurfaceKind } from './screenViewLabels'
import styles from './ScreenViewStateSurface.module.css'

interface ScreenViewStateSurfaceProps {
  status: ScreenViewStatus
  className: string
  surfaceRef?: RefObject<HTMLDivElement | null>
  children: ReactNode
}

export function ScreenViewStateSurface({
  status,
  className,
  surfaceRef,
  children
}: ScreenViewStateSurfaceProps): JSX.Element {
  const kind = screenViewSurfaceKind(status)

  return (
    <div ref={surfaceRef} className={`${styles.surface} ${className}`} data-state={kind}>
      {children}
      {kind === 'connecting' && <SatelliteDish className={styles.glyph} strokeWidth={1.5} aria-hidden />}
      {kind === 'idle' && (
        <span className={styles.mark} aria-hidden>
          <SatelliteDish className={styles.glyph} strokeWidth={1.5} />
          {status.kind === 'paused' && <Pause className={styles.pause} strokeWidth={2} />}
        </span>
      )}
      {kind === 'waiting' && <span className={styles.dot} aria-hidden />}
    </div>
  )
}

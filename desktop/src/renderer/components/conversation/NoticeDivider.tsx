import type { ReactNode } from 'react'
import styles from './NoticeDivider.module.css'

interface NoticeDividerProps {
  ariaLabel: string
  icon: ReactNode
  title: string
  detail?: string | null
  active?: boolean
}

export function NoticeDivider({ ariaLabel, icon, title, detail, active = false }: NoticeDividerProps): JSX.Element {
  return <div className={styles.divider} role={active ? 'status' : 'separator'} aria-live={active ? 'polite' : undefined} aria-label={ariaLabel}>
    <span className={styles.line} aria-hidden />
    <span className={styles.label}>
      <span className={styles.icon} aria-hidden>{icon}</span>
      <span className={active ? 'tool-running-gradient-text' : undefined}>{title}</span>
      {detail && <span className={styles.detail}>· {detail}</span>}
    </span>
    <span className={styles.line} aria-hidden />
  </div>
}

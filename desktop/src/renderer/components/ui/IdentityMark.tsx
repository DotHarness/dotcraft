import type { CSSProperties, ReactNode } from 'react'
import styles from './IdentityMark.module.css'

export type IdentityMarkRole = 'compact' | 'list' | 'hero'

interface IdentityMarkProps {
  role: IdentityMarkRole
  src?: string | null
  fallback: ReactNode
  size?: number
  backgroundColor?: CSSProperties['backgroundColor']
  framed?: boolean
  className?: string
  style?: CSSProperties
}

const defaultSize: Record<IdentityMarkRole, number> = {
  compact: 24,
  list: 40,
  hero: 60,
}

export function IdentityMark({
  role,
  src,
  fallback,
  size = defaultSize[role],
  backgroundColor,
  framed = role === 'hero',
  className,
  style,
}: IdentityMarkProps): JSX.Element {
  const rootStyle = {
    '--identity-mark-size': `${size}px`,
    backgroundColor,
    ...style,
  } as CSSProperties

  return (
    <span
      className={[styles.root, className].filter(Boolean).join(' ')}
      data-role={role}
      data-framed={framed || undefined}
      data-fallback={!src || undefined}
      style={rootStyle}
      aria-hidden
    >
      {src ? <img className={styles.image} src={src} alt="" /> : <span className={styles.fallback}>{fallback}</span>}
    </span>
  )
}

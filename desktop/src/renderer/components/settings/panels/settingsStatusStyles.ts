import type { CSSProperties } from 'react'

export type StatusTone = 'success' | 'warning' | 'error' | 'info' | 'neutral'

export function dotStyle(tone: StatusTone): CSSProperties {
  if (tone === 'neutral') {
    return {
      width: 8,
      height: 8,
      borderRadius: '50%',
      flexShrink: 0,
      border: '1.5px solid var(--text-dimmed)',
      boxSizing: 'border-box'
    }
  }
  return {
    width: 8,
    height: 8,
    borderRadius: '50%',
    flexShrink: 0,
    marginTop: 1,
    background: `var(--${tone})`
  }
}

export function statusTextStyle(tone: StatusTone): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: 7,
    fontSize: 12.5,
    lineHeight: 1,
    color: tone === 'neutral' ? 'var(--text-secondary)' : `var(--${tone})`
  }
}

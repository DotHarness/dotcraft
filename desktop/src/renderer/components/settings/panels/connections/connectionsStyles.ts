import type { CSSProperties } from 'react'

export const listRow: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 12,
  padding: '13px 14px',
  cursor: 'pointer',
  background: 'transparent',
  border: 'none',
  width: '100%',
  textAlign: 'left',
  color: 'var(--text-primary)'
}

export const listRowIcon: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: 34,
  height: 34,
  borderRadius: 8,
  background: 'var(--bg-tertiary)',
  color: 'var(--text-secondary)',
  flexShrink: 0
}

export const banner: CSSProperties = {
  display: 'flex',
  gap: 12,
  padding: 14,
  border: '1px solid color-mix(in srgb, var(--error) 38%, var(--border-default))',
  borderRadius: 10,
  background: 'color-mix(in srgb, var(--error) 8%, transparent)'
}

export const emptyBox: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  textAlign: 'center',
  gap: 6,
  padding: '46px 24px',
  border: '1px dashed var(--border-active)',
  borderRadius: 12,
  background: 'var(--bg-secondary)'
}

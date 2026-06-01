import type { CSSProperties } from 'react'

/**
 * Token-based styles for the Servers surface. Mirrors the approved design
 * prototype (dotcraft-design/desktop/remote-servers). Neutral-first; semantic
 * color only on status dots/badges per the Desktop Visual Design spec.
 */

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

export const card: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: 12,
  background: 'var(--bg-secondary)',
  overflow: 'hidden'
}

export const groupHead: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
  padding: '13px 14px',
  borderBottom: '1px solid var(--border-default)'
}

export const serverRow: CSSProperties = {
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

export const serverRowIcon: CSSProperties = {
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

export const btnPrimary: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 6,
  height: 32,
  padding: '0 12px',
  borderRadius: 8,
  fontSize: 13,
  fontWeight: 600,
  cursor: 'pointer',
  whiteSpace: 'nowrap',
  border: '1px solid var(--text-primary)',
  background: 'var(--text-primary)',
  color: 'var(--bg-primary)'
}

export const btn: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 6,
  height: 32,
  padding: '0 12px',
  borderRadius: 8,
  fontSize: 13,
  fontWeight: 600,
  cursor: 'pointer',
  whiteSpace: 'nowrap',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-primary)'
}

export const btnSm: CSSProperties = { height: 28, padding: '0 10px', fontSize: 12 }

export const btnDanger: CSSProperties = {
  ...btn,
  border: '1px solid color-mix(in srgb, var(--error) 55%, var(--border-default))',
  background: 'transparent',
  color: 'var(--error)'
}

export const iconBtn: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: 30,
  height: 30,
  borderRadius: 8,
  border: '1px solid var(--border-default)',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-secondary)',
  cursor: 'pointer'
}

export const iconBtnGhost: CSSProperties = {
  ...iconBtn,
  border: '1px solid transparent',
  background: 'transparent'
}

export const stackCard: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: 10,
  background: 'var(--bg-secondary)',
  overflow: 'hidden'
}

export const stackHead: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  minHeight: 26,
  padding: '13px 14px 0'
}

export const stackMeta: CSSProperties = {
  padding: '7px 14px 0',
  color: 'var(--text-dimmed)',
  fontSize: 12
}

export const stackActions: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  padding: '12px 14px 14px',
  flexWrap: 'wrap'
}

export const logsBox: CSSProperties = {
  margin: '0 14px 14px',
  border: '1px solid var(--border-default)',
  borderRadius: 8,
  background: 'var(--code-block-bg)',
  overflow: 'hidden'
}

export const logsBar: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  padding: '7px 10px',
  borderBottom: '1px solid var(--border-default)',
  fontSize: 11.5,
  color: 'var(--text-secondary)'
}

export const logsBody: CSSProperties = {
  height: 180,
  overflow: 'auto',
  padding: '10px 12px',
  fontFamily: 'var(--font-mono)',
  fontSize: 11.5,
  lineHeight: 1.7,
  color: 'var(--text-secondary)',
  whiteSpace: 'pre-wrap'
}

export const pillInfo: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  height: 20,
  padding: '0 8px',
  borderRadius: 999,
  fontSize: 11,
  fontWeight: 600,
  border: '1px solid color-mix(in srgb, var(--info) 35%, transparent)',
  background: 'color-mix(in srgb, var(--info) 14%, transparent)',
  color: 'var(--info)'
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

// ── Modal ────────────────────────────────────────────────────────────────────

export const modalScrim: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 10000,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'var(--overlay-scrim)'
}

export const modal: CSSProperties = {
  width: 460,
  maxWidth: 'calc(100vw - 48px)',
  maxHeight: 'calc(100vh - 80px)',
  overflowY: 'auto',
  border: '1px solid var(--border-active)',
  borderRadius: 14,
  background: 'var(--bg-elevated)',
  boxShadow: 'var(--shadow-level-3)'
}

export const modalHead: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  padding: '16px 18px 12px'
}

export const modalBody: CSSProperties = { padding: '4px 18px 6px' }

export const modalFoot: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'flex-end',
  gap: 8,
  padding: '14px 18px 18px'
}

export const fieldLabel: CSSProperties = {
  display: 'block',
  fontSize: 12.5,
  fontWeight: 600,
  marginBottom: 6,
  color: 'var(--text-primary)'
}

export const input: CSSProperties = {
  width: '100%',
  height: 34,
  padding: '0 11px',
  border: '1px solid var(--border-default)',
  borderRadius: 8,
  background: 'var(--bg-secondary)',
  color: 'var(--text-primary)',
  fontSize: 13,
  boxSizing: 'border-box'
}

export const fieldHint: CSSProperties = {
  marginTop: 5,
  color: 'var(--text-dimmed)',
  fontSize: 11.5
}

export const switchRow: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 16,
  padding: '12px 14px',
  border: '1px solid var(--border-default)',
  borderRadius: 9,
  background: 'var(--bg-secondary)'
}

export const callout: CSSProperties = {
  margin: '12px 0',
  padding: '12px 14px',
  border: '1px solid var(--border-default)',
  borderLeft: '3px solid var(--accent)',
  borderRadius: 8,
  background: 'color-mix(in srgb, var(--accent) 6%, transparent)',
  fontSize: 12.5,
  color: 'var(--text-secondary)'
}

export const overflowMenu: CSSProperties = {
  position: 'absolute',
  right: 0,
  top: 'calc(100% + 4px)',
  width: 184,
  zIndex: 20,
  border: '1px solid var(--border-active)',
  borderRadius: 10,
  background: 'var(--bg-elevated)',
  boxShadow: 'var(--shadow-level-3)',
  padding: 5,
  fontSize: 13
}

export const overflowItem: CSSProperties = {
  display: 'block',
  width: '100%',
  textAlign: 'left',
  padding: '8px 10px',
  borderRadius: 7,
  border: 'none',
  background: 'transparent',
  color: 'var(--text-primary)',
  fontSize: 13,
  cursor: 'pointer'
}

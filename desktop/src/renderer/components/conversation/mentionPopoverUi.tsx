import type { CSSProperties, JSX, ReactNode } from 'react'

/**
 * Shared visual language for the composer mention popovers (`@` files and `/`
 * commands·skills·system). Both render the same row shape — a standalone
 * type-colored icon, a bold name, and dimmed secondary text — under uppercase
 * section headers, inside one glass overlay with inset rounded highlight rows.
 * Keeping the styles here (rather than per popover) is what makes the two
 * surfaces read as one control.
 *
 * Per DESIGN.md: the overlay is the shared opaque elevated surface, rows are
 * borderless at rest, and the highlighted row uses neutral background elevation
 * (`--bg-tertiary`) — semantic color lives only in the small leading icon.
 */

interface ContainerOptions {
  constrainToAnchor: boolean
  /** Min width when free-floating (ignored when constrained to the anchor). */
  minWidth: string
  /** Max width when free-floating. */
  maxWidth: string
  maxHeight: string
}

export function mentionPopoverContainerStyle({
  constrainToAnchor,
  minWidth,
  maxWidth,
  maxHeight
}: ContainerOptions): CSSProperties {
  return {
    position: 'absolute',
    bottom: '100%',
    left: 0,
    marginBottom: '4px',
    width: constrainToAnchor ? '100%' : undefined,
    minWidth: constrainToAnchor ? 'min(280px, 100%)' : minWidth,
    maxWidth: constrainToAnchor ? '100%' : maxWidth,
    maxHeight,
    overflowY: 'auto',
    zIndex: 50,
    boxShadow: 'var(--glass-shadow-soft)',
    background: 'var(--glass-surface-strong)',
    border: 'none',
    borderRadius: '12px',
    backdropFilter: 'var(--glass-blur)',
    WebkitBackdropFilter: 'var(--glass-blur)',
    // Inset padding so the rounded row highlight floats inside the surface.
    padding: '6px'
  }
}

export function MentionSectionHeader({ label }: { label: string }): JSX.Element {
  return (
    <div
      style={{
        padding: '6px 8px 3px',
        fontSize: '11px',
        color: 'var(--text-dimmed)',
        fontWeight: 600,
        textTransform: 'uppercase',
        letterSpacing: '0.04em'
      }}
    >
      {label}
    </div>
  )
}

export function mentionRowStyle(active: boolean): CSSProperties {
  return {
    display: 'flex',
    width: '100%',
    alignItems: 'center',
    gap: '9px',
    padding: '7px 8px',
    border: 'none',
    background: active ? 'var(--bg-tertiary)' : 'transparent',
    borderRadius: '8px',
    color: 'var(--text-primary)',
    cursor: 'pointer',
    textAlign: 'left',
    font: 'inherit'
  }
}

/** Fixed-width leading icon slot so every name aligns regardless of glyph width. */
export const mentionRowIconStyle: CSSProperties = {
  width: '18px',
  height: '18px',
  flexShrink: 0,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center'
}

export const mentionRowNameStyle: CSSProperties = {
  fontWeight: 600,
  fontSize: '13px',
  color: 'var(--text-primary)',
  whiteSpace: 'nowrap',
  flexShrink: 0,
  maxWidth: '60%',
  overflow: 'hidden',
  textOverflow: 'ellipsis'
}

export const mentionRowDescStyle: CSSProperties = {
  fontSize: '12px',
  color: 'var(--text-dimmed)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  minWidth: 0,
  flex: 1
}

/** Wraps a leading icon and tints it (lucide glyphs inherit via currentColor). */
export function MentionRowIcon({ tint, children }: { tint?: string; children: ReactNode }): JSX.Element {
  return <span style={{ ...mentionRowIconStyle, color: tint }}>{children}</span>
}

/** Loading / empty / hint message rows. */
export const mentionEmptyStyle: CSSProperties = {
  padding: '8px 10px',
  fontSize: '12px',
  color: 'var(--text-dimmed)'
}

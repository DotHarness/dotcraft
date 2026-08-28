// Product tokens around the code, not the code's own colors: syntax coloring comes
// from the highlighter's token variables and is not decided here.
import type { CSSProperties } from 'react'
import type { RowType } from './diffRows'

export function diffLineBackground(type: RowType): string {
  if (type === 'add') return 'var(--diff-add-bg)'
  if (type === 'remove') return 'var(--diff-remove-bg)'
  return 'transparent'
}

/** Color mode marks changed lines with an accent bar; +/- mode uses the gutter sign instead. */
export function diffLineBar(type: RowType, signMode: boolean): string | undefined {
  if (signMode) return undefined
  if (type === 'add') return 'inset 2px 0 0 var(--success)'
  if (type === 'remove') return 'inset 2px 0 0 var(--error)'
  return undefined
}

/** Neutral in both modes: add/remove already read from the tinted fill. */
export function diffLineColor(type: RowType): string {
  if (type === 'blank') return 'transparent'
  return type === 'remove' ? 'var(--text-secondary)' : 'var(--text-primary)'
}

export function markerStyle(type: RowType): CSSProperties {
  return {
    width: '16px',
    flexShrink: 0,
    color: type === 'add'
      ? 'var(--success)'
      : type === 'remove'
        ? 'var(--error)'
        : 'var(--text-dimmed)',
    textAlign: 'center',
    userSelect: 'none'
  }
}

export function markerText(type: RowType): string {
  if (type === 'add') return '+'
  if (type === 'remove') return '-'
  return ' '
}

export const DIFF_GUTTER_WIDTH = 40

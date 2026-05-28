import type { CSSProperties, JSX } from 'react'
import type { ShortcutSpec } from './shortcutKeys'
import { formatShortcutParts } from './shortcutKeys'

interface ShortcutBadgeProps {
  shortcut: ShortcutSpec
  platform?: string
  tone?: 'default' | 'onAccent'
  style?: CSSProperties
}

export function ShortcutBadge({ shortcut, platform, tone = 'default', style }: ShortcutBadgeProps): JSX.Element {
  const parts = formatShortcutParts(shortcut, platform)
  const toneVars: CSSProperties = tone === 'onAccent'
    ? {
        ['--shortcut-bg' as string]: 'color-mix(in srgb, var(--on-accent) 16%, transparent)',
        ['--shortcut-border' as string]: 'color-mix(in srgb, var(--on-accent) 48%, transparent)',
        ['--shortcut-text' as string]: 'var(--on-accent)'
      }
    : {}

  return (
    <span
      aria-hidden="true"
      className="dc-shortcut-badge"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '3px',
        flexShrink: 0,
        ...toneVars,
        ...style
      }}
    >
      {parts.map((part, index) => (
        <kbd key={`${part}-${index}`} className="dc-shortcut-key">
          {part}
        </kbd>
      ))}
    </span>
  )
}

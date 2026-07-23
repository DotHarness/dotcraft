import type { CSSProperties } from 'react'
import {
  settingsDescriptionStyle,
  settingsPageTitleStyle
} from '../../settingsTypography'

export function pageStyle(): CSSProperties {
  return {
    display: 'flex',
    flexDirection: 'column',
    gap: '16px'
  }
}

export function pageHeadingStyle(): CSSProperties {
  return settingsPageTitleStyle()
}

export function pageDescriptionStyle(): CSSProperties {
  return settingsDescriptionStyle()
}

export function pillBadgeStyle(tone: 'neutral' | 'accent' | 'warning' | 'success'): CSSProperties {
  const { bg, fg } = pillPalette(tone)
  return {
    display: 'inline-flex',
    alignItems: 'center',
    padding: '2px 8px',
    borderRadius: '999px',
    fontSize: '11px',
    fontWeight: 600,
    backgroundColor: bg,
    color: fg
  }
}

export function noticeStyle(tone: 'error' | 'info' | 'warning'): CSSProperties {
  const palette =
    tone === 'error'
      ? { bg: 'rgba(255, 69, 58, 0.12)', fg: 'var(--error, #ff453a)' }
      : tone === 'warning'
        ? { bg: 'rgba(255, 149, 0, 0.12)', fg: 'var(--warning, #ff9500)' }
        : { bg: 'var(--bg-tertiary)', fg: 'var(--text-secondary)' }
  return {
    padding: '10px 12px',
    borderRadius: '10px',
    fontSize: 'var(--type-secondary-size)',
    lineHeight: 'var(--type-secondary-prose-line-height)',
    background: palette.bg,
    color: palette.fg
  }
}

export function actionBarStyle(): CSSProperties {
  return {
    display: 'flex',
    justifyContent: 'flex-end',
    alignItems: 'center',
    gap: '8px',
    flexWrap: 'wrap'
  }
}

function pillPalette(tone: 'neutral' | 'accent' | 'warning' | 'success'): { bg: string; fg: string } {
  switch (tone) {
    case 'accent':
      return { bg: 'color-mix(in srgb, var(--accent) 18%, transparent)', fg: 'var(--accent)' }
    case 'warning':
      return { bg: 'rgba(255, 149, 0, 0.15)', fg: 'var(--warning, #ff9500)' }
    case 'success':
      return { bg: 'rgba(52, 199, 89, 0.15)', fg: 'var(--success, #34c759)' }
    default:
      return { bg: 'var(--bg-tertiary)', fg: 'var(--text-secondary)' }
  }
}

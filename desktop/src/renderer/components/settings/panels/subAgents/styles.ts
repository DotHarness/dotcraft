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

export function noticeStyle(tone: 'error' | 'info' | 'warning'): CSSProperties {
  const palette =
    tone === 'error'
      ? { bg: 'var(--error-bg)', fg: 'var(--error-text)' }
      : tone === 'warning'
        ? { bg: 'var(--warning-bg)', fg: 'var(--warning-text)' }
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

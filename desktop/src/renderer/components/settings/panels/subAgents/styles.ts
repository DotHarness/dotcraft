import type { CSSProperties } from 'react'
import {
  settingsPageDescriptionStyle,
  settingsPageTitleStyle
} from '../../SettingsPageHeader'

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
  return settingsPageDescriptionStyle()
}

export function inputStyle(mono = false): CSSProperties {
  return {
    width: '100%',
    boxSizing: 'border-box',
    padding: '8px 10px',
    fontSize: '13px',
    borderRadius: '8px',
    border: '1px solid var(--border-default)',
    background: 'var(--bg-primary)',
    color: 'var(--text-primary)',
    outline: 'none',
    fontFamily: mono ? 'var(--font-mono)' : undefined
  }
}

export function monoTextAreaStyle(): CSSProperties {
  return {
    ...inputStyle(true),
    fontSize: '12px',
    lineHeight: 1.45,
    minHeight: '84px',
    resize: 'vertical'
  }
}

export function primaryButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '8px 14px',
    border: 'none',
    borderRadius: '8px',
    background: disabled ? 'color-mix(in srgb, var(--accent) 45%, var(--bg-tertiary))' : 'var(--accent)',
    color: 'var(--accent-foreground, white)',
    fontSize: '13px',
    fontWeight: 600,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.85 : 1
  }
}

export function secondaryButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '8px 14px',
    border: '1px solid var(--border-default)',
    borderRadius: '8px',
    background: 'transparent',
    color: 'var(--text-primary)',
    fontSize: '13px',
    fontWeight: 500,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.7 : 1
  }
}

export function dangerButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '8px 14px',
    border: '1px solid color-mix(in srgb, var(--error, #ff453a) 45%, var(--border-default))',
    borderRadius: '8px',
    background: 'transparent',
    color: 'var(--error, #ff453a)',
    fontSize: '13px',
    fontWeight: 500,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.6 : 1
  }
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
    fontSize: '12px',
    background: palette.bg,
    color: palette.fg,
    lineHeight: 1.5
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

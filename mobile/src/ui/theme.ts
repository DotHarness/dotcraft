import { createContext, useContext } from 'react'
import { Platform, type TextStyle } from 'react-native'

const dark = {
  bgPrimary: '#141515',
  bgSecondary: '#202020',
  bgTertiary: '#2a2a2a',
  bgElevated: '#242524',
  textPrimary: '#eeeeec',
  textSecondary: '#a4a4a3',
  textDimmed: '#70706f',
  composerPlaceholder: '#9b9c9a',
  borderSubtle: 'rgba(238, 238, 236, 0.05)',
  borderDefault: 'rgba(238, 238, 236, 0.1)',
  borderActive: 'rgba(238, 238, 236, 0.18)',
  accent: '#4566cc',
  success: '#22c55e',
  warning: '#eab308',
  error: '#ef4444',
  successText: '#22c55e',
  warningText: '#eab308',
  errorText: '#f87171',
  warningBg: 'rgba(234, 179, 8, 0.18)',
  userMessageBg: '#2a2a2a',
  composerInputBackground: '#2b2b2b',
  composerInputBorder: 'rgba(238, 238, 236, 0.1)',
  composerFocusBorder: 'rgba(95, 130, 247, 0.34)',
  overlayScrim: 'rgba(0, 0, 0, 0.5)',
  roundFill: 'rgba(238, 238, 236, 0.06)',
  roundFillPressed: 'rgba(238, 238, 236, 0.11)',
  primaryPressed: '#d4d4d2',
  dangerFill: 'rgba(239, 68, 68, 0.1)',
  dangerFillPressed: 'rgba(239, 68, 68, 0.18)',
  sendDisabled: 'rgba(238, 238, 236, 0.14)',
  failureBorder: 'rgba(239, 92, 92, 0.442)',
  failureFill: 'rgba(239, 68, 68, 0.08)',
  shadow1: '0px 1px 3px rgba(0, 0, 0, 0.22)',
  shadow3: '0px 18px 48px rgba(0, 0, 0, 0.32)',
}

const light: typeof dark = {
  bgPrimary: '#f9f9f9',
  bgSecondary: '#ffffff',
  bgTertiary: '#ededed',
  bgElevated: '#ffffff',
  textPrimary: '#1a1c1f',
  textSecondary: '#5d5e60',
  textDimmed: '#898a8c',
  composerPlaceholder: '#6f7072',
  borderSubtle: 'rgba(26, 28, 31, 0.04)',
  borderDefault: 'rgba(26, 28, 31, 0.08)',
  borderActive: 'rgba(26, 28, 31, 0.12)',
  accent: '#4f6cce',
  success: '#16a34a',
  warning: '#c9a227',
  error: '#dc2626',
  successText: '#15803d',
  warningText: '#8a6a10',
  errorText: '#b91c1c',
  warningBg: 'rgba(201, 162, 39, 0.14)',
  userMessageBg: '#f0f0f0',
  composerInputBackground: '#ffffff',
  composerInputBorder: 'rgba(26, 28, 31, 0.08)',
  composerFocusBorder: 'rgba(95, 130, 247, 0.34)',
  overlayScrim: 'rgba(0, 0, 0, 0.35)',
  roundFill: 'rgba(26, 28, 31, 0.06)',
  roundFillPressed: 'rgba(26, 28, 31, 0.11)',
  primaryPressed: '#353739',
  dangerFill: 'rgba(220, 38, 38, 0.1)',
  dangerFillPressed: 'rgba(220, 38, 38, 0.18)',
  sendDisabled: 'rgba(26, 28, 31, 0.14)',
  failureBorder: 'rgba(198, 37, 37, 0.4296)',
  failureFill: 'rgba(220, 38, 38, 0.08)',
  shadow1: '0px 1px 2px rgba(0, 0, 0, 0.06)',
  shadow3: '0px 8px 16px -4px rgba(0, 0, 0, 0.12)',
}

export const themes = {
  dark: { colors: dark },
  light: { colors: light },
}

export const camera = {
  background: '#0b0c0d',
  text: '#ffffff',
  textMuted: 'rgba(255, 255, 255, 0.72)',
  textHint: 'rgba(255, 255, 255, 0.6)',
  roundFill: 'rgba(255, 255, 255, 0.14)',
  noticeBorder: 'rgba(255, 255, 255, 0.14)',
  noticeFill: 'rgba(255, 255, 255, 0.08)',
  noticeText: 'rgba(255, 255, 255, 0.86)',
}

const mono = Platform.select({
  android: 'monospace',
  ios: 'Menlo',
  default: 'ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace',
})

export const type = {
  text: { fontSize: 15, lineHeight: 20 },
  meta: { fontSize: 13, lineHeight: 18 },
  caption: { fontSize: 12, lineHeight: 16 },
  body: { fontSize: 14, lineHeight: 21 },
  title: { fontSize: 28, lineHeight: 34, fontWeight: '700' },
  detailTitle: { fontSize: 20, lineHeight: 27, fontWeight: '600' },
  sheetTitle: { fontSize: 18, lineHeight: 23, fontWeight: '600' },
  code: { fontSize: 13, fontFamily: mono },
} satisfies Record<string, TextStyle>

export const metrics = {
  gutter: 16,
  touch: 44,
  rowRadius: 10,
  listRadius: 8,
  heroRadius: 16,
  noticeRadius: 12,
  pill: 999,
}

export const ThemeContext = createContext(themes.dark)

export function useTheme() {
  return useContext(ThemeContext)
}

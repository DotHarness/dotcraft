import type { AppLocale } from '../../../../../shared/locales'

export function formatDay(value: string | undefined | null, locale: AppLocale): string | null {
  if (!value) return null
  const parsed = Date.parse(value)
  if (!Number.isFinite(parsed)) return null
  return new Date(parsed).toLocaleDateString(locale, { year: 'numeric', month: 'short', day: 'numeric' })
}

export function formatClock(value: string, locale: AppLocale): string {
  const parsed = Date.parse(value)
  if (!Number.isFinite(parsed)) return ''
  return new Date(parsed).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })
}

/** The leaf of a folder path, so a row can lead with a name instead of a full path. */
export function folderLabel(path: string, fallback: string): string {
  const trimmed = path.replace(/[\\/]+$/, '')
  const leaf = trimmed.split(/[\\/]/).pop()
  return leaf && leaf !== '' ? leaf : fallback
}

import { validateTheme, type Theme, type ThemeInput } from '../../../shared/themes'

export function themeImportInput(value: unknown): ThemeInput {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('A theme object is required.')
  const {
    revision: _revision,
    updatedAt: _updatedAt,
    expectedRevision: _expectedRevision,
    builtin: _builtin,
    authorThreadId: _authorThreadId,
    pair: _pair,
    ...theme
  } = value as Record<string, unknown>
  return validateTheme({ ...theme, id: `theme-${crypto.randomUUID()}` })
}

export function exportTheme(theme: Theme): void {
  const {
    revision: _revision,
    updatedAt: _updatedAt,
    authorThreadId: _authorThreadId,
    builtin: _builtin,
    pair: _pair,
    ...input
  } = theme
  const value = { ...input, id: input.id.replace(/^builtin-/, '') }
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `${value.id}.theme.json`
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export function readImageDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error ?? new Error('The image could not be read.'))
    reader.readAsDataURL(file)
  })
}

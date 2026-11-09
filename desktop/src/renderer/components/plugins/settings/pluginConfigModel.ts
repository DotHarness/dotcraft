import type { DesktopPluginSettingField, DesktopPluginSettingsSnapshot } from '@dotcraft/plugin'

export type PluginConfigSnapshot = DesktopPluginSettingsSnapshot<Record<string, unknown>>

export type PluginConfigInputError = 'number' | 'range' | 'json'

export function configFieldLabel(field: DesktopPluginSettingField): string {
  return field.title || field.key
}

export function storedKey(record: Partial<Record<string, unknown>>, key: string): string | undefined {
  const lower = key.toLowerCase()
  return Object.keys(record).find((candidate) => candidate.toLowerCase() === lower)
}

export function formatConfigInput(field: DesktopPluginSettingField, value: unknown): string {
  if (field.type === 'json') return value === undefined ? '' : JSON.stringify(value, null, 2)
  return value == null ? '' : String(value)
}

export function parseConfigInput(
  field: DesktopPluginSettingField,
  raw: string
): { value: unknown } | { error: PluginConfigInputError } {
  if (field.type === 'number') {
    const value = raw.trim() === '' ? Number.NaN : Number(raw)
    if (!Number.isFinite(value)) return { error: 'number' }
    if ((field.min !== undefined && value < field.min) || (field.max !== undefined && value > field.max)) {
      return { error: 'range' }
    }
    return { value }
  }
  if (field.type === 'json') {
    try {
      return { value: JSON.parse(raw) as unknown }
    } catch {
      return { error: 'json' }
    }
  }
  return { value: raw }
}

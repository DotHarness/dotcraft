import type { JsonValue } from '@dotcraft/sdk/contracts'

export function parseBackendSettings(text: string): Record<string, JsonValue> {
  const value: unknown = JSON.parse(text)
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Backend settings must be a JSON object.')
  return value as Record<string, JsonValue>
}

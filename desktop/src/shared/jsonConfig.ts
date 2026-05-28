export function stripUtf8Bom(input: string): string {
  return input.charCodeAt(0) === 0xfeff ? input.slice(1) : input
}

function ensureParsedObjectConfig(config: unknown): Record<string, unknown> {
  if (config == null || typeof config !== 'object' || Array.isArray(config)) {
    throw new Error('Config payload must be a JSON object')
  }
  return config as Record<string, unknown>
}

export function parseJsonConfig<T>(raw: string, fallback: T): T {
  const trimmed = stripUtf8Bom(raw).trim()
  if (!trimmed) return fallback
  try {
    const parsed = JSON.parse(trimmed) as unknown
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return parsed as T
    }
    return fallback
  } catch {
    return fallback
  }
}

export function parseJsonObjectConfig(raw: string): Record<string, unknown> {
  const trimmed = stripUtf8Bom(raw).trim()
  if (!trimmed) return {}
  return ensureParsedObjectConfig(JSON.parse(trimmed) as unknown)
}

export function parseJsonRecordConfig(raw: string): Record<string, unknown> {
  const trimmed = stripUtf8Bom(raw).trim()
  if (!trimmed) return {}
  const parsed = JSON.parse(trimmed) as unknown
  if (parsed == null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return {}
  }
  return parsed as Record<string, unknown>
}

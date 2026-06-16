interface ParsedJson {
  ok: true
  value: unknown
}

interface FailedParse {
  ok: false
}

type ParseResult = ParsedJson | FailedParse

export function formatDefaultToolResultForDisplay(result: string | undefined): string {
  if (!result) return ''

  const parsed = tryParseJson(result)
  if (!parsed.ok) return result

  return formatParsedValue(parsed.value)
}

function formatParsedValue(value: unknown): string {
  const mcpContent = tryFormatMcpEnvelope(value)
  if (mcpContent != null) return mcpContent

  return stringifyDisplayValue(value)
}

function tryFormatMcpEnvelope(value: unknown): string | null {
  if (!isRecord(value)) return null

  if (Object.prototype.hasOwnProperty.call(value, 'structuredContent')) {
    const structuredContent = value.structuredContent
    if (structuredContent !== undefined && structuredContent !== null) {
      return stringifyDisplayValue(structuredContent)
    }
  }

  if (!Array.isArray(value.content)) return null

  const textItems = value.content
    .map((entry) => {
      if (!isRecord(entry)) return null
      if (entry.type !== 'text') return null
      return typeof entry.text === 'string' ? entry.text : null
    })
    .filter((entry): entry is string => entry != null && entry.length > 0)

  if (textItems.length === 0) return null

  return textItems.map(formatJsonTextIfPossible).join('\n')
}

function stringifyDisplayValue(value: unknown): string {
  if (typeof value === 'string') {
    return formatJsonTextIfPossible(value)
  }

  try {
    return JSON.stringify(value, null, 2)
  } catch {
    return String(value)
  }
}

function formatJsonTextIfPossible(text: string): string {
  const parsed = tryParseJson(text)
  if (!parsed.ok) return text

  return stringifyDisplayValue(parsed.value)
}

function tryParseJson(text: string): ParseResult {
  const trimmed = text.trim()
  if (!trimmed) return { ok: false }

  try {
    return { ok: true, value: JSON.parse(trimmed) as unknown }
  } catch {
    return { ok: false }
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value != null && !Array.isArray(value)
}

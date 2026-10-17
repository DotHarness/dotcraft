import { translate, type AppLocale } from '../../shared/locales'
import type { ToolKind } from './toolKind'

export type WebToolKind = Extract<ToolKind, 'webSearch' | 'webFetch' | 'toolSearch'>

export interface WebSearchResultRow {
  title: string
  url: string
  domain: string
}

export type WebSearchResultDisplay =
  | { kind: 'results'; rows: WebSearchResultRow[] }
  | { kind: 'empty'; message: string }
  | { kind: 'error'; message: string }

export function isWebToolKind(kind: ToolKind | null): kind is WebToolKind {
  return kind === 'webSearch' || kind === 'webFetch' || kind === 'toolSearch'
}

/** Accept string fields only so mistyped values use the generic invocation fallback. */
function getJsonStringField(args: Record<string, unknown> | undefined, key: string): string | undefined {
  if (!args) return undefined
  const v = args[key]
  return typeof v === 'string' ? v : undefined
}

function getToolSearchQuery(args: Record<string, unknown> | undefined): string | undefined {
  return getJsonStringField(args, 'query') ?? getJsonStringField(args, 'q')
}

/**
 * Returns null when required string fields are missing or not JSON strings so
 * the caller can use the generic "Called …" label.
 */
export function formatInvocationDisplay(
  kind: WebToolKind,
  args: Record<string, unknown> | undefined,
  locale: AppLocale
): string | null {
  if (kind === 'webSearch') {
    const query = getJsonStringField(args, 'query')
    if (query === undefined) return null
    return translate(locale, 'toolCall.webSearch.invocation', { query })
  }
  if (kind === 'webFetch') {
    const url = getJsonStringField(args, 'url')
    if (url === undefined) return null
    return translate(locale, 'toolCall.webFetch.invocation', { url })
  }
  const query = getToolSearchQuery(args)
  if (query === undefined) return null
  return translate(locale, 'toolCall.searchTools.invocation', { query })
}

/** When true, ToolCallCard uses "Calling …" + toolName; otherwise it shows the standalone sentence. */
export function invocationNeedsCallingPrefix(
  kind: WebToolKind,
  args: Record<string, unknown> | undefined
): boolean {
  return formatInvocationDisplay(kind, args, 'en') === null
}

function peelJsonStringWrapper(parsed: unknown): unknown {
  if (typeof parsed === 'string') {
    try {
      return JSON.parse(parsed) as unknown
    } catch {
      return parsed
    }
  }
  return parsed
}

function hostFromUrl(url: string): string {
  try {
    const u = new URL(url)
    return u.hostname
  } catch {
    const rest = url
      .replace(/^https:\/\//i, '')
      .replace(/^http:\/\//i, '')
      .replace(/^ftp:\/\//i, '')
    const hostPort = rest.split(/[/?#]/)[0] ?? ''
    const host = hostPort.includes('@') ? hostPort.split('@').pop() ?? hostPort : hostPort
    return host
  }
}

export function formatToolSearchCompletedLabel(
  result: string | undefined,
  locale: AppLocale
): string | null {
  const count = extractToolSearchResultCount(result)
  if (count === null) return null
  return count === 1
    ? translate(locale, 'toolCall.searchTools.completed.single')
    : translate(locale, 'toolCall.searchTools.completed.multiple', { count })
}

export function parseWebSearchResultDisplay(result: string | undefined): WebSearchResultDisplay | null {
  const trimmed = result?.trim() ?? ''
  if (trimmed === '') return null

  let root: unknown
  try {
    root = JSON.parse(trimmed) as unknown
  } catch {
    return null
  }

  return parseWebSearchResult(peelJsonStringWrapper(root))
}

function parseWebSearchResult(root: unknown): WebSearchResultDisplay | null {
  if (root === null || typeof root !== 'object' || Array.isArray(root)) return null
  const obj = root as Record<string, unknown>

  if ('error' in obj && obj.error != null) {
    const msg = String(obj.error).trim()
    return msg ? { kind: 'error', message: msg } : null
  }

  const resultsProp = obj.results
  if (!Array.isArray(resultsProp)) {
    const msg = typeof obj.message === 'string' ? obj.message.trim() : ''
    return msg ? { kind: 'empty', message: msg } : null
  }

  const count = resultsProp.length
  if (count === 0) {
    return { kind: 'empty', message: 'No results found.' }
  }

  const rows = toWebSearchResultRows(resultsProp)
  if (rows.length === 0) {
    return { kind: 'empty', message: 'No results found.' }
  }

  return { kind: 'results', rows }
}

export function toWebSearchResultRows(entries: readonly unknown[]): WebSearchResultRow[] {
  const rows: WebSearchResultRow[] = []
  for (const item of entries) {
    if (item === null || typeof item !== 'object' || Array.isArray(item)) {
      continue
    }
    const row = item as Record<string, unknown>
    const url = row.url != null ? String(row.url).trim() : ''
    if (!url) continue

    const title = row.title != null ? String(row.title).trim() : ''
    const domain = hostFromUrl(url).replace(/^www\./iu, '')
    rows.push({ title: title || domain || url, url, domain: domain || url })
  }
  return rows
}

interface ToolSearchDisplayTool {
  name: string
  description?: string
}

function collectToolSearchTools(
  value: unknown[],
  namespaceName?: string
): ToolSearchDisplayTool[] {
  const tools: ToolSearchDisplayTool[] = []
  for (const item of value) {
    if (item === null || typeof item !== 'object' || Array.isArray(item)) continue
    const obj = item as Record<string, unknown>
    const type = typeof obj.type === 'string' ? obj.type.trim().toLowerCase() : ''
    const name = typeof obj.name === 'string' ? obj.name.trim() : ''
    const childTools = Array.isArray(obj.tools) ? obj.tools : null

    if (type === 'namespace' && childTools) {
      const childNamespace = name || namespaceName
      tools.push(...collectToolSearchTools(childTools, childNamespace))
      continue
    }

    if (!name) continue
    const displayName = namespaceName ? `${namespaceName}.${name}` : name
    const description = typeof obj.description === 'string' ? obj.description.trim() : ''
    tools.push(description ? { name: displayName, description } : { name: displayName })
  }
  return tools
}

function extractToolSearchResultCount(result: string | undefined): number | null {
  const trimmed = result?.trim() ?? ''
  if (!trimmed) return null

  let root: unknown
  try {
    root = JSON.parse(trimmed) as unknown
    root = peelJsonStringWrapper(root)
    if (root && typeof root === 'object' && !Array.isArray(root)) {
      const toolsValue = (root as Record<string, unknown>).tools
      if (Array.isArray(toolsValue)) {
        return collectToolSearchTools(toolsValue).length
      }
    }
  } catch {
    // Fall back to text parsing below.
  }

  const lines = trimmed
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
  const first = lines[0] ?? ''
  if (/^No matching tools found\.?$/i.test(first)) return 0
  const found = /^Found\s+(\d+)\s+matching tool/i.exec(first)
  if (found) return Number.parseInt(found[1]!, 10)

  const bulletCount = lines.filter((line) => line.startsWith('- ')).length
  return bulletCount > 0 ? bulletCount : null
}

/**
 * WebSearch / WebFetch / SearchTools / tool_search display — aligned with DotCraft.Core
 * CoreToolDisplays and TUI `tool_format.rs`.
 */

import { translate, type AppLocale } from '../../shared/locales'

const TOOL_SEARCH_TOOLS = new Set(['SearchTools', 'tool_search'])
const WEB_TOOLS = new Set(['WebSearch', 'WebFetch', 'SearchTools', 'tool_search'])

export interface WebSearchResultRow {
  title: string
  url: string
  snippet?: string
  author?: string
  publishedDate?: string
  domain: string
  linkLabel: string
}

export type WebSearchResultDisplay =
  | { kind: 'results'; query?: string; provider?: string; rows: WebSearchResultRow[] }
  | { kind: 'empty'; message: string }
  | { kind: 'error'; message: string }

export function isWebToolName(toolName: string): boolean {
  return WEB_TOOLS.has(toolName)
}

function isToolSearchTool(toolName: string): boolean {
  return TOOL_SEARCH_TOOLS.has(toolName)
}

export function truncate(s: string, max: number): string {
  if (max <= 0) return ''
  const chars = [...s]
  if (chars.length <= max) return s
  return chars.slice(0, max).join('') + '…'
}

/** JSON string field only — matches TUI `parse_string_field` / standalone invocation detection. */
function getJsonStringField(args: Record<string, unknown> | undefined, key: string): string | undefined {
  if (!args) return undefined
  const v = args[key]
  return typeof v === 'string' ? v : undefined
}

function getToolSearchQuery(args: Record<string, unknown> | undefined): string | undefined {
  return getJsonStringField(args, 'query') ?? getJsonStringField(args, 'q')
}

/**
 * Human-readable invocation line (matches CoreToolDisplays / format_invocation_display).
 * Returns null when required string fields are missing or not JSON strings (TUI: fall back to generic "Called …").
 */
export function formatInvocationDisplay(
  toolName: string,
  args: Record<string, unknown> | undefined,
  locale: AppLocale
): string | null {
  if (toolName === 'WebSearch') {
    const qRaw = getJsonStringField(args, 'query')
    if (qRaw === undefined) return null
    const q = truncate(qRaw, 80)
    return translate(locale, 'toolCall.webSearch.invocation', { query: q })
  }
  if (toolName === 'WebFetch') {
    const uRaw = getJsonStringField(args, 'url')
    if (uRaw === undefined) return null
    const u = truncate(uRaw, 80)
    return translate(locale, 'toolCall.webFetch.invocation', { url: u })
  }
  if (isToolSearchTool(toolName)) {
    const qRaw = getToolSearchQuery(args)
    if (qRaw === undefined) return null
    const q = truncate(qRaw, 60)
    return translate(locale, 'toolCall.searchTools.invocation', { query: q })
  }
  return null
}

/** When true, ToolCallCard should use "Calling …" + toolName; when false, show standalone sentence only (TUI parity). */
export function invocationNeedsCallingPrefix(
  toolName: string,
  args: Record<string, unknown> | undefined
): boolean {
  if (!isWebToolName(toolName)) return true
  return formatInvocationDisplay(toolName, args, 'en') === null
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

function displayUrl(url: string): string {
  const domain = hostFromUrl(url)
  if (domain) return domain
  return truncate(url, 80)
}

function formatIntGrouped(n: number): string {
  return n.toLocaleString('en-US', { maximumFractionDigits: 0 })
}

function jsonNumberToInt(v: unknown): number | null {
  if (typeof v === 'number' && Number.isFinite(v)) return Math.trunc(v)
  return null
}

/**
 * Structured result lines (matches ToolRegistry.FormatToolResult for web tools).
 * Returns null to fall back to generic raw preview.
 */
export function formatResultSummary(toolName: string, result: string | undefined): string[] | null {
  const trimmed = result?.trim() ?? ''
  if (trimmed === '') return null

  if (isToolSearchTool(toolName)) {
    return parseToolSearchResultSummary(trimmed)
  }

  if (toolName === 'WebSearch') {
    const parsed = parseWebSearchResultDisplay(result)
    if (!parsed) return null
    if (parsed.kind === 'error') return [`Error: ${parsed.message}`]
    if (parsed.kind === 'empty') return [parsed.message]

    const count = parsed.rows.length
    const lines: string[] = []
    lines.push(`${count} result${count === 1 ? '' : 's'}:`)
    for (let i = 0; i < parsed.rows.length; i++) {
      const row = parsed.rows[i]!
      const titleText = truncate(row.title || row.url || '?', 70)
      lines.push(row.domain ? `${i + 1}. ${titleText} — ${row.domain}` : `${i + 1}. ${titleText}`)
    }
    return lines
  }

  if (toolName === 'WebFetch') {
    let root: unknown
    try {
      root = JSON.parse(trimmed) as unknown
    } catch {
      return null
    }
    root = peelJsonStringWrapper(root)
    return parseWebFetchResult(root)
  }

  return null
}

export function formatToolSearchCompletedLabel(
  toolName: string,
  result: string | undefined,
  locale: AppLocale
): string | null {
  if (!isToolSearchTool(toolName)) return null
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

  const rows: WebSearchResultRow[] = []
  for (const item of resultsProp) {
    if (item === null || typeof item !== 'object' || Array.isArray(item)) {
      continue
    }
    const row = item as Record<string, unknown>
    const url = row.url != null ? String(row.url).trim() : ''
    if (!url) continue

    const title = row.title != null ? String(row.title).trim() : ''
    const snippet = row.snippet != null ? String(row.snippet).trim() : ''
    const author = row.author != null ? String(row.author).trim() : ''
    const publishedDate = row.publishedDate != null ? String(row.publishedDate).trim() : ''
    const domain = hostFromUrl(url)
    rows.push({
      title: title || domain || url,
      url,
      ...(snippet ? { snippet } : {}),
      ...(author ? { author } : {}),
      ...(publishedDate ? { publishedDate } : {}),
      domain,
      linkLabel: displayUrl(url)
    })
  }

  if (rows.length === 0) {
    return { kind: 'empty', message: 'No results found.' }
  }

  return {
    kind: 'results',
    query: typeof obj.query === 'string' ? obj.query : undefined,
    provider: typeof obj.provider === 'string' ? obj.provider : undefined,
    rows
  }
}

function parseWebFetchResult(root: unknown): string[] | null {
  if (root === null || typeof root !== 'object' || Array.isArray(root)) return null
  const obj = root as Record<string, unknown>

  if ('error' in obj && obj.error != null) {
    const msg = String(obj.error).trim()
    return msg ? [`Error: ${msg}`] : null
  }

  const parts: string[] = []

  const status = jsonNumberToInt(obj.status)
  if (status !== null) parts.push(String(status))

  const len = obj.length
  if (typeof len === 'number' && Number.isFinite(len)) {
    parts.push(`${formatIntGrouped(Math.trunc(len))} chars`)
  }

  if (obj.extractor != null) {
    const ext = String(obj.extractor).trim()
    if (ext) parts.push(ext)
  }

  if (obj.truncated === true) {
    parts.push('truncated')
  }

  if (parts.length === 0) return null
  return [parts.join(' · ')]
}

interface ToolSearchDisplayTool {
  name: string
  description?: string
}

function parseToolSearchResultSummary(result: string): string[] | null {
  const fromJson = parseToolSearchJsonResult(result)
  if (fromJson) return fromJson
  return parseToolSearchTextResult(result)
}

function parseToolSearchJsonResult(result: string): string[] | null {
  let root: unknown
  try {
    root = JSON.parse(result) as unknown
  } catch {
    return null
  }
  root = peelJsonStringWrapper(root)

  if (root === null || typeof root !== 'object' || Array.isArray(root)) return null
  const toolsValue = (root as Record<string, unknown>).tools
  if (!Array.isArray(toolsValue)) return null

  const tools = collectToolSearchTools(toolsValue)
  if (tools.length === 0) return ['No matching tools found.']

  return [
    `Found ${tools.length} matching tool(s):`,
    ...tools.map((tool) =>
      tool.description ? `- ${tool.name}: ${tool.description}` : `- ${tool.name}`
    )
  ]
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

function parseToolSearchTextResult(result: string): string[] | null {
  const lines = result
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .filter((line) => !line.startsWith('You can call these tools directly'))

  if (lines.length === 0) return null
  const first = lines[0]!
  if (/^(Found \d+ matching tool\(s\):?|No matching tools found\.?)$/i.test(first)) {
    return [first, ...lines.slice(1)]
  }
  return lines
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

export function getWebToolSectionLabel(toolName: string, locale: AppLocale): string | null {
  if (toolName === 'WebSearch') {
    return translate(locale, 'toolCall.webSearch.section')
  }
  if (toolName === 'WebFetch') {
    return translate(locale, 'toolCall.webFetch.section')
  }
  if (isToolSearchTool(toolName)) {
    return translate(locale, 'toolCall.searchTools.section')
  }
  return null
}

/** Icon from ToolRegistry / server (🔍 WebSearch, 🌐 WebFetch); SearchTools uses wrench-style search. */
export function getWebToolIcon(toolName: string): string {
  if (toolName === 'WebSearch') return '🔍'
  if (toolName === 'WebFetch') return '🌐'
  if (isToolSearchTool(toolName)) return '🔧'
  return '🔧'
}

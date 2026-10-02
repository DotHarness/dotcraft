export interface SummaryItem {
  readonly id: string
  readonly turnId: string
  readonly type: string
  readonly createdAt: string
  readonly payload: Readonly<Record<string, unknown>>
}

export type SourceKind = 'web' | 'file' | 'skill' | 'mcp'

export interface Source {
  readonly kind: SourceKind
  readonly id: string
  readonly title: string
  readonly target: string | null
}

const SOURCE_ITEM_TYPES = new Set(['toolCall', 'toolResult', 'mcpToolCall'])

export function isSourceItemType(type: string): boolean {
  return SOURCE_ITEM_TYPES.has(type)
}

export function toSummaryItem(value: unknown, turnId?: string | null): SummaryItem | null {
  if (!isRecord(value)) return null
  const type = stringOf(value.type)
  const id = stringOf(value.id)
  const turn = stringOf(turnId) ?? stringOf(value.turnId)
  if (!type || !id || !turn || !isSourceItemType(type)) return null
  return {
    id,
    turnId: turn,
    type,
    createdAt: stringOf(value.completedAt) ?? stringOf(value.createdAt) ?? '',
    payload: isRecord(value.payload) ? value.payload : {}
  }
}

export function hasFileChanges(items: readonly SummaryItem[]): boolean {
  return items.some((item) => {
    if (item.type !== 'toolResult') return false
    const content = item.payload.structuredContent ?? item.payload.structuredResult
    return isRecord(content) && content.kind === 'fileChange' && Array.isArray(content.changes) && content.changes.length > 0
  })
}

export function extractSources(items: readonly SummaryItem[], workspacePath: string | null): Source[] {
  const calls = new Map<string, { toolName: string; args: Record<string, unknown> }>()
  const sources: Source[] = []
  const positions = new Map<string, number>()
  const untitled = new Set<string>()

  const add = (source: Source, provisional = false): void => {
    const key = `${source.kind}:${source.id}`
    const position = positions.get(key)
    if (position === undefined) {
      positions.set(key, sources.length)
      sources.push(source)
      if (provisional) untitled.add(key)
    } else if (!provisional && untitled.delete(key)) {
      sources[position] = source
    }
  }

  const addWeb = (rawUrl: string, title: string | null): void => {
    const url = normalizeUrl(rawUrl)
    if (url) add({ kind: 'web', id: url, title: title ?? domainOf(url), target: url }, title === null)
  }

  for (const item of items) {
    const payload = item.payload
    const callId = stringOf(payload.callId) ?? item.id

    if (item.type === 'mcpToolCall') {
      const server = stringOf(payload.server)
      if (server) add({ kind: 'mcp', id: server, title: server, target: null })
      continue
    }

    if (item.type === 'toolCall') {
      const toolName = stringOf(payload.toolName) ?? ''
      calls.set(callId, { toolName, args: isRecord(payload.arguments) ? payload.arguments : {} })
      const source = isRecord(payload.source) ? payload.source : null
      const server = source && stringOf(source.kind)?.toLowerCase() === 'mcp' ? stringOf(source.sourceId) : null
      if (server) add({ kind: 'mcp', id: server, title: server, target: null })
      continue
    }

    const call = calls.get(callId)
    const toolName = stringOf(payload.toolName) ?? call?.toolName ?? ''
    const args = call?.args ?? {}
    const result = typeof payload.result === 'string' ? payload.result : ''
    const succeeded = payload.success !== false

    if (toolName === 'WebSearch') {
      for (const row of webSearchRows(result)) addWeb(row.url, row.title)
    } else if (toolName === 'WebFetch') {
      const url = stringOf(args.url)
      if (succeeded && url) addWeb(url, null)
    } else if (toolName === 'ReadFile') {
      const path = stringOf(args.path)
      if (succeeded && path) {
        const relative = relativePath(path, workspacePath)
        const slash = relative.lastIndexOf('/')
        add({ kind: 'file', id: relative, title: relative.slice(slash + 1), target: path })
      }
    } else if (toolName === 'SkillView') {
      const name = stringOf(args.name)?.trim()
      if (succeeded && name && skillLoaded(result)) add({ kind: 'skill', id: name, title: name, target: null })
    }
  }

  return sources
}

export interface TurnMark {
  readonly id: string
  readonly startedAt: string
}

export function rolledBackTurns(
  items: readonly SummaryItem[],
  recentDescending: readonly TurnMark[],
  complete: boolean
): Set<string> {
  const surviving = new Set(recentDescending.map((turn) => turn.id))
  const oldest = recentDescending[recentDescending.length - 1]
  const boundary = complete ? Number.NEGATIVE_INFINITY : Date.parse(oldest?.startedAt ?? '')
  const removed = new Set<string>()
  for (const item of items) {
    if (surviving.has(item.turnId)) continue
    if (complete || Date.parse(item.createdAt) > boundary) removed.add(item.turnId)
  }
  return removed
}

function webSearchRows(result: string): Array<{ url: string; title: string | null }> {
  let root: unknown
  try {
    root = JSON.parse(result.trim())
    if (typeof root === 'string') root = JSON.parse(root)
  } catch {
    return []
  }
  if (!isRecord(root) || root.error != null || !Array.isArray(root.results)) return []
  const rows: Array<{ url: string; title: string | null }> = []
  for (const entry of root.results) {
    if (!isRecord(entry)) continue
    const url = entry.url != null ? String(entry.url).trim() : ''
    if (!url) continue
    const title = entry.title != null ? String(entry.title).trim() : ''
    rows.push({ url, title: title || null })
  }
  return rows
}

function skillLoaded(result: string): boolean {
  const text = result.trim()
  return text !== '' && text !== 'Skill name is required.' && !/^Skill '.+' not found\.$/.test(text)
}

function normalizeUrl(raw: string): string | null {
  try {
    const url = new URL(raw.trim())
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return null
    url.hash = ''
    return url.href
  } catch {
    return null
  }
}

function domainOf(url: string): string {
  try {
    return new URL(url).hostname.replace(/^www\./i, '')
  } catch {
    return url
  }
}

function relativePath(path: string, workspacePath: string | null): string {
  const normalized = path.trim().replace(/\\/g, '/')
  const root = workspacePath?.trim().replace(/\\/g, '/').replace(/\/+$/, '') ?? ''
  return root && normalized.toLowerCase().startsWith(`${root.toLowerCase()}/`)
    ? normalized.slice(root.length + 1)
    : normalized.replace(/^\.\//, '')
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function stringOf(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null
}

export interface SummaryItem {
  readonly id: string
  readonly turnId: string
  readonly type: string
  readonly createdAt: string
  readonly payload: Readonly<Record<string, unknown>>
}

export type SourceKind = 'file' | 'skill' | 'mcp' | 'web'

export interface Source {
  readonly kind: SourceKind
  readonly id: string
  readonly title: string
  readonly target: string | null
}

const SOURCE_ITEM_TYPES = new Set(['toolCall', 'toolResult', 'mcpToolCall', 'webSearch'])

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

export interface ChangeTotals {
  readonly additions: number
  readonly deletions: number
}

export function changeTotals(
  items: readonly SummaryItem[],
  turnDiffs: ReadonlyMap<string, ChangeTotals> = new Map()
): ChangeTotals | null {
  const turns = new Map<string, ChangeTotals>()
  for (const item of items) {
    const content = item.type === 'toolResult' ? item.payload.structuredContent : null
    if (!isRecord(content) || content.kind !== 'fileChange' || !Array.isArray(content.changes)) continue
    for (const change of content.changes) {
      if (!isRecord(change)) continue
      const current = turns.get(item.turnId) ?? { additions: 0, deletions: 0 }
      turns.set(item.turnId, {
        additions: current.additions + countOf(change.additions),
        deletions: current.deletions + countOf(change.deletions)
      })
    }
  }
  if (turns.size === 0) return null
  let additions = 0
  let deletions = 0
  for (const [turnId, calls] of turns) {
    const totals = turnDiffs.get(turnId) ?? calls
    additions += totals.additions
    deletions += totals.deletions
  }
  return { additions, deletions }
}

export function diffTotals(diff: string): ChangeTotals | null {
  let additions = 0
  let deletions = 0
  let inHunk = false
  for (const line of diff.split('\n')) {
    if (line.startsWith('diff --git ')) inHunk = false
    else if (line.startsWith('@@')) inHunk = true
    else if (inHunk && line.startsWith('+')) additions += 1
    else if (inHunk && line.startsWith('-')) deletions += 1
  }
  return additions > 0 || deletions > 0 ? { additions, deletions } : null
}

export function extractSources(items: readonly SummaryItem[], workspacePath: string | null): Source[] {
  const calls = new Map<string, { kind: SourceToolKind | null; args: Record<string, unknown> }>()
  const sources: Source[] = []
  const keys = new Set<string>()

  const add = (source: Source): void => {
    const key = `${source.kind}:${source.id}`
    if (keys.has(key)) return
    keys.add(key)
    sources.push(source)
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
      calls.set(callId, { kind: sourceKindOf(payload), args: isRecord(payload.arguments) ? payload.arguments : {} })
      const source = isRecord(payload.source) ? payload.source : null
      const server = source && stringOf(source.kind)?.toLowerCase() === 'mcp' ? stringOf(source.sourceId) : null
      if (server) add({ kind: 'mcp', id: server, title: server, target: null })
      continue
    }

    const call = calls.get(callId)
    const kind = sourceKindOf(payload) ?? call?.kind ?? null
    const args = call?.args ?? {}
    const result = typeof payload.result === 'string' ? payload.result : ''
    const succeeded = payload.success !== false

    if (kind === 'webFetch') {
      const url = stringOf(args.url)
      const normalized = url ? normalizeUrl(url) : null
      if (succeeded && normalized) add({ kind: 'web', id: normalized, title: domainOf(normalized), target: normalized })
    } else if (kind === 'read') {
      const path = stringOf(args.path)
      if (succeeded && path) {
        const relative = relativePath(path, workspacePath)
        const slash = relative.lastIndexOf('/')
        add({ kind: 'file', id: relative, title: relative.slice(slash + 1), target: path })
      }
    } else if (kind === 'skillView') {
      const name = stringOf(args.name)?.trim()
      if (succeeded && name && skillLoaded(result)) add({ kind: 'skill', id: name, title: name, target: null })
    }
  }

  return sources
}

export function hasWebSearch(items: readonly SummaryItem[]): boolean {
  return items.some((item) => item.type === 'webSearch'
    || (item.type === 'toolCall' && sourceKindOf(item.payload) === 'webSearch'))
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

type SourceToolKind = 'webSearch' | 'webFetch' | 'read' | 'skillView'

function sourceKindOf(payload: Record<string, unknown>): SourceToolKind | null {
  const source = isRecord(payload.source) ? payload.source : null
  const presentation = isRecord(payload.presentation) ? payload.presentation : null
  if (stringOf(source?.kind) !== 'CoreNative' || !presentation) return null
  const operation = isRecord(presentation.options) ? stringOf(presentation.options.operation) : null
  switch (stringOf(presentation.presentationId)) {
    case 'core.web':
      return operation === 'search' ? 'webSearch' : operation === 'fetch' ? 'webFetch' : null
    case 'core.read-file':
      return operation === 'read' ? 'read' : null
    case 'core.skill-view':
      return 'skillView'
    default:
      return null
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function countOf(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0
}

function stringOf(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null
}

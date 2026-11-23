import { collectMcpCalls, type SourceToolGroup } from './mcpSourceCalls'

export type { SourceToolCall, SourceToolGroup } from './mcpSourceCalls'

export interface SummaryItem {
  readonly id: string
  readonly turnId: string
  readonly type: string
  readonly createdAt: string
  readonly status?: string
  readonly payload: Readonly<Record<string, unknown>>
}

export type SourceKind = 'file' | 'skill' | 'mcp' | 'web'

export interface Source {
  readonly kind: SourceKind
  readonly id: string
  readonly title: string
  readonly target: string | null
  readonly image?: boolean
  readonly pluginId?: string | null
  readonly usage?: readonly SourceUsage[]
  readonly toolGroups?: readonly SourceToolGroup[]
}

export type SourceUsage = 'attached' | 'provided' | 'read' | 'loaded'

export interface WebSearchPage {
  readonly url: string
  readonly title: string
}

export interface WebSearchActivity {
  readonly searches: number
  readonly queries: readonly string[]
  readonly pages: readonly WebSearchPage[]
}

const SOURCE_ITEM_TYPES = new Set(['userMessage', 'toolCall', 'toolResult', 'mcpToolCall', 'webSearch'])

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
    ...(typeof value.status === 'string' ? { status: value.status } : {}),
    payload: isRecord(value.payload) ? value.payload : {}
  }
}

export interface ChangeTotals {
  readonly additions: number
  readonly deletions: number
}

export type FileChanges = ReadonlyMap<string, ChangeTotals>

function addTo(target: Map<string, ChangeTotals>, path: string, additions: number, deletions: number): void {
  const current = target.get(path) ?? { additions: 0, deletions: 0 }
  target.set(path, { additions: current.additions + additions, deletions: current.deletions + deletions })
}

export function changeTotals(
  items: readonly SummaryItem[],
  turnDiffs: ReadonlyMap<string, FileChanges> = new Map()
): FileChanges | null {
  const turns = new Map<string, Map<string, ChangeTotals>>()
  for (const item of items) {
    const content = item.type === 'toolResult' ? item.payload.structuredContent : null
    if (!isRecord(content) || content.kind !== 'fileChange' || !Array.isArray(content.changes)) continue
    for (const change of content.changes) {
      if (!isRecord(change)) continue
      const files = turns.get(item.turnId) ?? new Map<string, ChangeTotals>()
      turns.set(item.turnId, files)
      addTo(files, stringOf(change.path) ?? '', countOf(change.additions), countOf(change.deletions))
    }
  }
  if (turns.size === 0) return null
  const totals = new Map<string, ChangeTotals>()
  for (const [turnId, calls] of turns) {
    for (const [path, change] of turnDiffs.get(turnId) ?? calls) addTo(totals, path, change.additions, change.deletions)
  }
  return totals
}

export function sumChanges(changes: Iterable<ChangeTotals>): ChangeTotals {
  let additions = 0
  let deletions = 0
  for (const change of changes) {
    additions += change.additions
    deletions += change.deletions
  }
  return { additions, deletions }
}

export function diffTotals(diff: string): FileChanges {
  const files = new Map<string, ChangeTotals>()
  let path: string | null = null
  let inHunk = false
  for (const line of diff.split('\n')) {
    if (line.startsWith('diff --git ')) {
      const marker = line.lastIndexOf(' b/')
      path = marker >= 0 ? line.slice(marker + 3) : null
      inHunk = false
    } else if (line.startsWith('@@')) inHunk = true
    else if (inHunk && path != null && line.startsWith('+')) addTo(files, path, 1, 0)
    else if (inHunk && path != null && line.startsWith('-')) addTo(files, path, 0, 1)
  }
  return files
}

export function extractSources(items: readonly SummaryItem[], workspacePath: string | null): Source[] {
  const calls = new Map<string, { kind: SourceToolKind | null; args: Record<string, unknown> }>()
  const sources: Source[] = []
  const usages = new Map<string, Set<SourceUsage>>()
  const mcp = collectMcpCalls()

  const add = (source: Source, usage?: SourceUsage): void => {
    const key = `${source.kind}:${source.id}`
    let used = usages.get(key)
    if (!used) {
      used = new Set()
      usages.set(key, used)
      sources.push(source)
    }
    if (usage) used.add(usage)
  }

  for (const item of items) {
    const payload = item.payload
    const callId = stringOf(payload.callId) ?? item.id

    if (item.type === 'userMessage') {
      for (const source of userSources(payload, workspacePath)) add(source, source.kind === 'file' ? 'attached' : 'provided')
      continue
    }

    if (item.type === 'mcpToolCall' || item.type === 'toolCall') {
      if (item.type === 'toolCall') {
        calls.set(callId, { kind: sourceKindOf(payload), args: isRecord(payload.arguments) ? payload.arguments : {} })
      }
      const source = mcp.observeCall(item, callId)
      if (source) add(source)
      continue
    }

    if (item.type === 'toolResult') mcp.observeResult(item, callId)

    const call = calls.get(callId)
    const kind = sourceKindOf(payload) ?? call?.kind ?? null
    const args = call?.args ?? {}
    const result = typeof payload.result === 'string' ? payload.result : ''
    const succeeded = payload.success !== false

    if (kind === 'webFetch') {
      const url = stringOf(args.url)
      const normalized = url ? normalizeUrl(url) : null
      if (succeeded && normalized) add({ kind: 'web', id: normalized, title: domainOf(normalized), target: normalized }, 'read')
    } else if (kind === 'skillView') {
      const name = stringOf(args.name)?.trim()
      if (succeeded && name && skillLoaded(result)) add({ kind: 'skill', id: name, title: name, target: name }, 'loaded')
    }
  }

  return sources.map((source) => {
    const usage = [...usages.get(`${source.kind}:${source.id}`) ?? []]
    const toolGroups = source.kind === 'mcp' ? mcp.groups(source.id) : undefined
    return {
      ...source,
      ...(usage.length > 0 ? { usage } : {}),
      ...(toolGroups ? { toolGroups } : {})
    }
  })
}

export function webSearchActivity(items: readonly SummaryItem[]): WebSearchActivity | null {
  let found = false
  let searches = 0
  const queries = new Set<string>()
  const pages = new Map<string, string>()
  const addQuery = (value: unknown): void => {
    const query = stringOf(value)?.trim()
    if (query) queries.add(query)
  }

  for (const { type, payload } of items) {
    if (type === 'toolCall' && sourceKindOf(payload) === 'webSearch') {
      found = true
      searches += 1
      addQuery(isRecord(payload.arguments) ? payload.arguments.query : null)
      continue
    }
    if (type !== 'webSearch') continue
    found = true
    const action = isRecord(payload.action) ? payload.action : null
    const actionType = stringOf(action?.type)
    if (actionType === 'openPage' || actionType === 'findInPage') {
      const url = normalizeUrl(stringOf(action?.url) ?? '')
      if (url && !pages.has(url)) pages.set(url, resultTitle(payload.results, url) ?? domainOf(url))
    } else if (actionType !== 'other') {
      searches += 1
      addQuery(action?.query)
      if (Array.isArray(action?.queries)) action.queries.forEach(addQuery)
      addQuery(payload.query)
    }
  }

  return found ? { searches, queries: [...queries], pages: Array.from(pages, ([url, title]) => ({ url, title })) } : null
}

function resultTitle(results: unknown, url: string): string | null {
  if (!Array.isArray(results)) return null
  const match = results.find((entry) => isRecord(entry) && normalizeUrl(stringOf(entry.url) ?? '') === url)
  return isRecord(match) ? stringOf(match.title) : null
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

export function workflowCallIds(items: readonly SummaryItem[]): Map<string, string> {
  const workflowCalls = new Set<string>()
  const runs = new Map<string, string>()
  for (const item of items) {
    const callId = stringOf(item.payload.callId)
    if (!callId) continue
    if (item.type === 'toolCall' && stringOf(item.payload.toolName)?.toLowerCase() === 'workflow') workflowCalls.add(callId)
    else if (item.type === 'toolResult' && workflowCalls.has(callId) && typeof item.payload.result === 'string') {
      try {
        const runId = stringOf((JSON.parse(item.payload.result) as { runId?: unknown }).runId)
        if (runId) runs.set(runId, callId)
      } catch {
        continue
      }
    }
  }
  return runs
}

// Task notifications and mailbox deliveries are model-facing, so they carry no user-provided sources.
function userSources(payload: Record<string, unknown>, workspacePath: string | null): Source[] {
  if (stringOf(payload.triggerKind) || payload.deliveryMode === 'subagentMailbox') return []
  const sources: Source[] = []
  const file = (path: string | null, name?: string | null, image = false): void => {
    if (!path) return
    const relative = relativePath(path, workspacePath)
    sources.push({ kind: 'file', id: relative, title: name || relative.slice(relative.lastIndexOf('/') + 1), target: path, image })
  }
  const parts = Array.isArray(payload.nativeInputParts) ? payload.nativeInputParts : []
  for (const part of parts) {
    if (!isRecord(part)) continue
    if (part.type === 'fileRef') file(stringOf(part.path))
    else if (part.type === 'localImage') file(stringOf(part.path), stringOf(part.fileName), true)
    else if (part.type === 'skillRef') {
      const name = stringOf(part.name)?.trim()
      if (name) sources.push({ kind: 'skill', id: name, title: name, target: name })
    }
  }
  const images = Array.isArray(payload.images) ? payload.images : []
  for (const image of images) if (isRecord(image)) file(stringOf(image.path), stringOf(image.fileName), true)
  return sources
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

type SourceToolKind = 'webSearch' | 'webFetch' | 'skillView'

function sourceKindOf(payload: Record<string, unknown>): SourceToolKind | null {
  const source = isRecord(payload.source) ? payload.source : null
  const presentation = isRecord(payload.presentation) ? payload.presentation : null
  if (stringOf(source?.kind) !== 'CoreNative' || !presentation) return null
  const operation = isRecord(presentation.options) ? stringOf(presentation.options.operation) : null
  switch (stringOf(presentation.presentationId)) {
    case 'core.web':
      return operation === 'search' ? 'webSearch' : operation === 'fetch' ? 'webFetch' : null
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

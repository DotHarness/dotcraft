import type { ToolEntry, ToolVerb, TranscriptEntry } from './transcriptTypes'

export type ToolGroupPart = 'calledTool' | 'created' | 'edited' | 'explored' | 'ran' | 'searchedWeb' | 'fetched'

export type ToolGroupLabel = { part: ToolGroupPart; many: boolean }[]

type GroupKind = 'tool' | 'write' | 'explore' | 'shell' | 'search' | 'fetch'

const GROUP_ORDER: readonly GroupKind[] = ['tool', 'write', 'explore', 'shell', 'search', 'fetch']

const OWN_ROW_VERBS = new Set<ToolVerb>([
  'spawned',
  'messaged',
  'listedAgents',
  'stoppedShell',
  'stoppedAgent',
  'stoppedWorkflow',
  'stoppingShell',
  'stoppingAgent',
  'stoppingWorkflow',
  'agentFinished',
  'agentInterrupted',
])

function groupKind(item: ToolEntry): GroupKind {
  if (item.category === 'web') return item.web === 'fetch' ? 'fetch' : 'search'
  return item.category ?? 'tool'
}

function byGroupKind(items: ToolEntry[]): Map<GroupKind, ToolEntry[]> {
  const kinds = new Map<GroupKind, ToolEntry[]>()
  for (const item of items) {
    const kind = groupKind(item)
    const entries = kinds.get(kind)
    if (entries) entries.push(item)
    else kinds.set(kind, [item])
  }
  return new Map(GROUP_ORDER.filter((kind) => kinds.has(kind)).map((kind) => [kind, kinds.get(kind)!]))
}

function writeLabel(items: ToolEntry[]): { part: ToolGroupPart; many: boolean } {
  const paths = new Map<string, boolean>()
  for (const item of items) paths.set(item.path || item.id, (paths.get(item.path || item.id) ?? false) || item.created)
  const created = [...paths.values()].filter(Boolean).length
  return created > 0 && created === paths.size ? { part: 'created', many: created > 1 } : { part: 'edited', many: paths.size > 1 }
}

function groupLabel(kinds: Map<GroupKind, ToolEntry[]>): ToolGroupLabel {
  return [...kinds].map(([kind, entries]) => {
    const many = entries.length > 1
    switch (kind) {
      case 'tool':
        return { part: 'calledTool', many }
      case 'write':
        return writeLabel(entries)
      case 'explore':
        return { part: 'explored', many }
      case 'shell':
        return { part: 'ran', many }
      case 'search':
        return { part: 'searchedWeb', many }
      case 'fetch':
        return { part: 'fetched', many }
    }
  })
}

function toolGroup(items: ToolEntry[], active: boolean): Extract<TranscriptEntry, { kind: 'toolGroup' }> {
  const kinds = byGroupKind(items)
  return {
    kind: 'toolGroup',
    id: `group-${items[0].id}`,
    label: groupLabel(kinds),
    icon: kinds.values().next().value![0].icon,
    failed: items.some((item) => item.failed && item.category !== 'shell'),
    active,
    children: items,
  }
}

function settledRun(items: ToolEntry[]): TranscriptEntry[] {
  const out: TranscriptEntry[] = []
  let bucket: ToolEntry[] = []
  const flush = () => {
    if (bucket.length > 1) out.push(toolGroup(bucket, false))
    else out.push(...bucket)
    bucket = []
  }
  for (const item of items) {
    if (item.live) {
      flush()
      out.push(item)
    } else {
      bucket.push(item)
    }
  }
  flush()
  return out
}

function aggregate(run: ToolEntry[], active: boolean): TranscriptEntry[] {
  const out: TranscriptEntry[] = []
  let segment: ToolEntry[] = []
  for (const item of run) {
    if (!OWN_ROW_VERBS.has(item.verb) && item.images.length === 0) {
      segment.push(item)
      continue
    }
    out.push(...settledRun(segment), item)
    segment = []
  }
  if (active && segment.length > 1) out.push(toolGroup(segment, true))
  else out.push(...settledRun(segment))
  return out
}

export function groupTools(body: TranscriptEntry[], running: boolean): TranscriptEntry[] {
  const out: TranscriptEntry[] = []
  for (let index = 0; index < body.length; ) {
    if (body[index].kind !== 'tool') {
      out.push(body[index])
      index += 1
      continue
    }
    let end = index
    while (end < body.length && body[end].kind === 'tool') end += 1
    const run = body.slice(index, end) as ToolEntry[]
    out.push(...aggregate(run, running && end === body.length))
    index = end
  }
  return out
}

import type { WorkspaceProjectState, WorkspaceProjectSummary } from '../../../../shared/workspaceProjects'
import { isHiddenThread } from '../../../../shared/hiddenThreads'
import type { ThreadSummary } from '../../../types/thread'
import { isSubAgentThread } from '../../../utils/subAgentThreads'

export type ArchivedSourceKind = 'local' | 'remote' | 'chat'

export interface ArchivedThreadSource {
  key: string
  kind: ArchivedSourceKind
  name: string
  path: string
  identityWorkspacePath: string
  state: WorkspaceProjectState
  live: boolean
  foreground: boolean
}

export interface ArchivedThreadEntry {
  thread: ThreadSummary
  source: ArchivedThreadSource
}

export type ArchivedProjectFilter = 'all' | 'automations' | `source:${string}`
export type ArchivedSort = 'updated' | 'created' | 'title'

export interface ArchivedThreadGroup {
  id: string
  kind: 'source' | 'automations'
  source?: ArchivedThreadSource
  entries: ArchivedThreadEntry[]
}

const AUTOMATION_ORIGIN = 'automations'

export function sourceFilter(source: ArchivedThreadSource): ArchivedProjectFilter {
  return `source:${source.key}`
}

export function archivedThreadSources(payload: {
  projects: WorkspaceProjectSummary[]
  chat: WorkspaceProjectSummary | null
}): ArchivedThreadSource[] {
  const sources: ArchivedThreadSource[] = []
  for (const project of payload.projects) {
    if (project.kind === 'remote' && !isLiveProject(project)) continue
    const source = toSource(project, project.kind === 'remote' ? 'remote' : 'local')
    if (source) sources.push(source)
  }
  const chat = payload.chat ? toSource(payload.chat, 'chat') : null
  if (chat) sources.push(chat)
  return sources
}

function toSource(project: WorkspaceProjectSummary, kind: ArchivedSourceKind): ArchivedThreadSource | null {
  const key = project.projectId?.trim()
  if (!key) return null
  return {
    key,
    kind,
    name: project.name,
    path: project.path,
    identityWorkspacePath: project.identityWorkspacePath || project.path,
    state: project.state,
    live: isLiveProject(project),
    foreground: project.state === 'foreground'
  }
}

function isLiveProject(project: WorkspaceProjectSummary): boolean {
  return project.loaded && (project.state === 'foreground' || project.state === 'secondary')
}

export function isStartableSource(source: ArchivedThreadSource): boolean {
  return source.kind === 'local' && (source.state === 'cold' || source.state === 'error')
}

export function archivedThreadsFromList(threads: ThreadSummary[]): ThreadSummary[] {
  return threads.filter((thread) => thread.status === 'archived' && !isSubAgentThread(thread) && !isHiddenThread(thread))
}

export function isAutomationRun(thread: ThreadSummary): boolean {
  return thread.originChannel?.toLowerCase() === AUTOMATION_ORIGIN
}

export function groupArchivedThreads(
  sources: ArchivedThreadSource[],
  threadsBySource: ReadonlyMap<string, ThreadSummary[]>,
  options: { query: string; project: ArchivedProjectFilter; sort: ArchivedSort }
): ArchivedThreadGroup[] {
  const query = options.query.trim().toLocaleLowerCase()
  const groups: ArchivedThreadGroup[] = []
  const automations: ArchivedThreadEntry[] = []
  for (const source of sources) {
    const entries: ArchivedThreadEntry[] = []
    for (const thread of threadsBySource.get(source.key) ?? []) {
      if (query && !(thread.displayName ?? '').toLocaleLowerCase().includes(query)) continue
      if (isAutomationRun(thread)) automations.push({ thread, source })
      else entries.push({ thread, source })
    }
    if (entries.length > 0 && (options.project === 'all' || options.project === sourceFilter(source))) {
      groups.push({ id: sourceFilter(source), kind: 'source', source, entries: sortEntries(entries, options.sort) })
    }
  }
  if (automations.length > 0 && (options.project === 'all' || options.project === 'automations')) {
    groups.push({ id: 'automations', kind: 'automations', entries: sortEntries(automations, options.sort) })
  }
  return groups
}

function sortEntries(entries: ArchivedThreadEntry[], sort: ArchivedSort): ArchivedThreadEntry[] {
  const sorted = [...entries]
  if (sort === 'title') {
    sorted.sort((left, right) => (left.thread.displayName ?? '').localeCompare(right.thread.displayName ?? ''))
  } else {
    const field = sort === 'created' ? 'createdAt' : 'lastActiveAt'
    sorted.sort((left, right) => (Date.parse(right.thread[field]) || 0) - (Date.parse(left.thread[field]) || 0))
  }
  return sorted
}

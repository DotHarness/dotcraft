import { childFromWire, type SubAgentChild } from '../stores/subAgentChildren'
import type { SubAgentDiscovery } from '../stores/subAgentStore'
import type { ThreadSummary } from '../types/thread'

export interface SubAgentLookupSources {
  sourceThreadId: string
  childrenByParent: Map<string, SubAgentChild[]>
  discoveryByParent?: Map<string, SubAgentDiscovery>
  threadList: ThreadSummary[]
  activeThread?: ThreadSummary | null
}

export type SubAgentScope = 'children' | 'tree'

function sourceThread(lookup: SubAgentLookupSources, id: string): ThreadSummary | undefined {
  return (lookup.activeThread?.id === id ? lookup.activeThread : undefined)
    ?? lookup.threadList.find((thread) => thread.id === id)
    ?? Array.from(lookup.childrenByParent.values()).flat()
      .find((child) => child.childThreadId === id)?.threadSummary ?? undefined
}

function rootId(thread: ThreadSummary | undefined): string | undefined {
  if (!thread) return undefined
  return thread.source?.subAgent?.rootThreadId
    ?? (thread.source?.kind === 'subagent' || thread.source?.subAgent ? undefined : thread.id)
}

export function findSubAgentChild(
  lookup: SubAgentLookupSources,
  childThreadId: string | null | undefined,
  agentPath: string | null | undefined,
  scope: SubAgentScope = 'children'
): SubAgentChild | null {
  const source = sourceThread(lookup, lookup.sourceThreadId)
  const sourceRoot = rootId(source)
  const sourcePath = source?.source?.subAgent?.agentPath ?? '/root'
  const targetPath = agentPath?.startsWith('/') ? agentPath : `${sourcePath}/${agentPath}`
  const matches = (child: SubAgentChild): boolean => child.isPlaceholder !== true && (childThreadId
    ? child.childThreadId === childThreadId
    : !!agentPath && (child.agentPath === agentPath || child.agentPath === targetPath))
  const allowed = (child: SubAgentChild): boolean => {
    if (child.parentThreadId === lookup.sourceThreadId) return true
    if (scope !== 'tree' || !sourceRoot) return false
    const target = child.threadSummary ?? sourceThread(lookup, child.childThreadId)
    return target?.source?.subAgent?.rootThreadId === sourceRoot
  }
  const direct = lookup.childrenByParent.get(lookup.sourceThreadId)?.find(matches)
  if (direct) return direct
  if (scope === 'tree' && sourceRoot) {
    for (const children of lookup.childrenByParent.values()) {
      const child = children.find((entry) => allowed(entry) && matches(entry))
      if (child) return child
    }
  }
  const threads = lookup.activeThread ? [lookup.activeThread, ...lookup.threadList] : lookup.threadList
  for (const thread of threads) {
    const parentThreadId = thread.source?.subAgent?.parentThreadId
    if (!parentThreadId || lookup.discoveryByParent?.get(parentThreadId)?.discovered) continue
    const child = childFromWire(parentThreadId, { thread })
    if (child && allowed(child) && matches(child)) return child
  }
  return null
}

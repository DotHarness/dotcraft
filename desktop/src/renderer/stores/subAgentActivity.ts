import type { ThreadRuntimeSnapshot } from '../types/thread'
import { isSubAgentChildClosed, isTerminalSubAgentStatus, type SubAgentChild } from './subAgentChildren'
import type { SubAgentTasks } from './threadTaskState'

export interface ActivitySources {
  graph: ReadonlyMap<string, SubAgentChild[]>
  runtimes: ReadonlyMap<string, ThreadRuntimeSnapshot>
  tasks: ReadonlyMap<string, SubAgentTasks>
  reconciling: ReadonlySet<string>
}

export function childIsWorking(child: SubAgentChild, sources: ActivitySources): boolean {
  if (isSubAgentChildClosed(child)) return false
  const runtime = sources.runtimes.get(child.childThreadId)
  return runtime?.running === true
    || runtime?.hasBackgroundWork === true
    || (child.isPlaceholder === true && !isTerminalSubAgentStatus(child.status))
    || sources.reconciling.has(child.childThreadId)
}

export function subAgentDescendants(graph: ActivitySources['graph'], threadId: string): SubAgentChild[] {
  const result: SubAgentChild[] = []
  const visited = new Set([threadId])
  const visit = (id: string): void => {
    for (const child of graph.get(id) ?? []) {
      if (isSubAgentChildClosed(child) || visited.has(child.childThreadId)) continue
      visited.add(child.childThreadId)
      result.push(child)
      visit(child.childThreadId)
    }
  }
  visit(threadId)
  return result
}

export function projectThreadActivity(threadId: string | null, sources: ActivitySources) {
  const children = threadId ? subAgentDescendants(sources.graph, threadId) : []
  const owners = children.flatMap(child => {
    const work = sources.tasks.get(child.childThreadId)
    return work ? [{ threadId: child.childThreadId, ...work }] : []
  })
  const ownTasks = threadId ? sources.tasks.get(threadId) : undefined
  const activeAgents = children.filter(child => childIsWorking(child, sources)).length
  const terminals = new Set<string>()
  const workflows = new Set<string>()
  for (const owner of [...owners, ...(threadId && ownTasks ? [{ threadId, ...ownTasks }] : [])]) {
    for (const terminal of owner.terminals) terminals.add(owner.threadId + '\0' + terminal.sessionId)
    for (const workflow of owner.workflows) workflows.add(owner.threadId + '\0' + workflow.runId)
  }
  const delegates = activeAgents + workflows.size
  return { children, owners, ownTasks, activeAgents, delegates, count: delegates + terminals.size }
}

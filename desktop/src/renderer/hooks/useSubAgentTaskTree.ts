import { useEffect, useMemo, useRef } from 'react'
import { isSubAgentChildClosed, useSubAgentStore, type SubAgentChild } from '../stores/subAgentStore'
import type { ThreadRuntimeSnapshot } from '../types/thread'
import { useSubAgentTaskActivity, type SubAgentTasks } from '../stores/subAgentTaskActivity'
import { useThreadStore } from '../stores/threadStore'
import { useConnectionStore } from '../stores/connectionStore'

export function subAgentDescendants(graph: ReadonlyMap<string, SubAgentChild[]>, threadId: string): SubAgentChild[] {
  const result: SubAgentChild[] = []
  const visit = (id: string): void => {
    for (const child of graph.get(id) ?? []) {
      if (child.isPlaceholder || isSubAgentChildClosed(child)) continue
      result.push(child)
      visit(child.childThreadId)
    }
  }
  visit(threadId)
  return result
}

const EMPTY_GRAPH = new Map<string, SubAgentChild[]>()
const EMPTY_TASKS = new Map<string, SubAgentTasks>()
const EMPTY_RUNTIMES = new Map<string, ThreadRuntimeSnapshot>()

export function useSubAgentTaskTree(threadId: string | null) {
  const graph = useSubAgentStore(state => threadId ? state.childrenByParent : EMPTY_GRAPH)
  const tasks = useSubAgentTaskActivity(state => threadId ? state.tasksByThread : EMPTY_TASKS)
  const runtimes = useThreadStore(state => threadId ? state.runtimeSnapshots : EMPTY_RUNTIMES)
  const epoch = useConnectionStore(state => threadId ? state.connectionEpoch : 0)
  const observed = useRef({ threadId, epoch, ids: new Set<string>() })
  const children = useMemo(() => threadId ? subAgentDescendants(graph, threadId) : [], [graph, threadId])
  useEffect(() => {
    if (!threadId) return
    if (observed.current.threadId !== threadId || observed.current.epoch !== epoch) {
      observed.current = { threadId, epoch, ids: new Set() }
    }
    for (const id of [threadId, ...children.map(child => child.childThreadId)]) {
      if (observed.current.ids.has(id)) continue
      observed.current.ids.add(id)
      void useSubAgentStore.getState().ensureChildren(id).catch(() => {})
    }
  }, [threadId, children, epoch])
  return useMemo(() => ({
    children,
    owners: children.flatMap(child => {
      const work = tasks.get(child.childThreadId)
      return work ? [{ threadId: child.childThreadId, ...work }] : []
    }),
    ownTasks: threadId ? tasks.get(threadId) : undefined,
    activeAgents: children.filter(child => runtimes.get(child.childThreadId)?.running === true).length
  }), [threadId, children, tasks, runtimes])
}

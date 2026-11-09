import type { DesktopPluginSubAgent, DesktopPluginSubAgentState } from '@dotcraft/plugin'

import {
  isSubAgentChildClosed,
  isSubAgentChildWorking,
  isSubAgentChildFinished,
  subAgentChildRuntime,
  useSubAgentStore,
  type SubAgentChild
} from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useSubAgentTaskActivity } from '../stores/subAgentTaskActivity'
import type { ThreadRuntimeSnapshot } from '../types/thread'

const NO_SUBAGENTS: readonly DesktopPluginSubAgent[] = []
const snapshots = new WeakMap<SubAgentChild[], {
  graph: ReadonlyMap<string, SubAgentChild[]>
  runtimes: ReadonlyMap<string, ThreadRuntimeSnapshot>
  tasks: ReadonlySet<string>
  pendingTasks: ReadonlySet<string>
  agents: readonly DesktopPluginSubAgent[]
}>()

export function desktopPluginSubAgentState(child: SubAgentChild): DesktopPluginSubAgentState {
  if (isSubAgentChildClosed(child)) return 'done'
  if (isSubAgentChildWorking(child)) {
    const runtime = subAgentChildRuntime(child)
    return runtime?.waitingOnApproval === true || runtime?.waitingOnInput === true
      ? 'waiting'
      : 'working'
  }
  const status = child.status.trim().toLowerCase()
  if (status === 'failed') return 'failed'
  if (status === 'cancelled' || status === 'canceled') return 'cancelled'
  return isSubAgentChildFinished(child) ? 'done' : 'waiting'
}

function toDesktopPluginSubAgent(parentThreadId: string, child: SubAgentChild): DesktopPluginSubAgent {
  return {
    parentThreadId,
    childThreadId: child.childThreadId,
    agentPath: child.agentPath ?? null,
    nickname: child.nickname,
    state: desktopPluginSubAgentState(child)
  }
}

export function listDesktopPluginSubAgents(parentThreadId: string): readonly DesktopPluginSubAgent[] {
  const graph = useSubAgentStore.getState().childrenByParent
  const children = graph.get(parentThreadId)
  if (!children || children.length === 0) return NO_SUBAGENTS
  const runtimes = useThreadStore.getState().runtimeSnapshots
  const { activeThreadIds: tasks, pendingThreadIds: pendingTasks } = useSubAgentTaskActivity.getState()
  let snapshot = snapshots.get(children)
  if (!snapshot || snapshot.graph !== graph || snapshot.runtimes !== runtimes || snapshot.tasks !== tasks || snapshot.pendingTasks !== pendingTasks) {
    snapshot = { graph, runtimes, tasks, pendingTasks, agents: children.map((child) => toDesktopPluginSubAgent(parentThreadId, child)) }
    snapshots.set(children, snapshot)
  }
  return snapshot.agents
}

function sameSubAgents(a: readonly DesktopPluginSubAgent[], b: readonly DesktopPluginSubAgent[]): boolean {
  return a.length === b.length && a.every((entry, index) => {
    const other = b[index]
    return entry.childThreadId === other.childThreadId
      && entry.agentPath === other.agentPath
      && entry.nickname === other.nickname
      && entry.state === other.state
  })
}

export function onDesktopPluginSubAgentsChange(
  parentThreadId: string,
  listener: (subagents: readonly DesktopPluginSubAgent[]) => void
): () => void {
  let current = listDesktopPluginSubAgents(parentThreadId)
  const changed = (): void => {
    const next = listDesktopPluginSubAgents(parentThreadId)
    if (sameSubAgents(current, next)) return
    current = next
    listener(next)
  }
  const stopChildren = useSubAgentStore.subscribe((state, previous) => {
    if (state.childrenByParent !== previous.childrenByParent) changed()
  })
  const stopRuntime = useThreadStore.subscribe((state, previous) => {
    if (state.runtimeSnapshots !== previous.runtimeSnapshots) changed()
  })
  const stopTasks = useSubAgentTaskActivity.subscribe(changed)
  return () => { stopChildren(); stopRuntime(); stopTasks() }
}

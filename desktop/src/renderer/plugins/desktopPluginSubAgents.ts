import type { DesktopPluginSubAgent, DesktopPluginSubAgentState } from '@dotcraft/plugin'

import {
  isSubAgentChildClosed,
  isSubAgentChildRunning,
  useSubAgentStore,
  type SubAgentChild
} from '../stores/subAgentStore'

const NO_SUBAGENTS: readonly DesktopPluginSubAgent[] = []
const snapshots = new WeakMap<SubAgentChild[], readonly DesktopPluginSubAgent[]>()

export function desktopPluginSubAgentState(child: SubAgentChild): DesktopPluginSubAgentState {
  if (isSubAgentChildClosed(child)) return 'done'
  if (isSubAgentChildRunning(child)) {
    return child.runtime?.waitingOnApproval === true || child.runtime?.waitingOnInput === true
      ? 'waiting'
      : 'working'
  }
  const status = child.status.trim().toLowerCase()
  if (status === 'failed') return 'failed'
  if (status === 'cancelled' || status === 'canceled') return 'cancelled'
  return 'done'
}

function toDesktopPluginSubAgent(parentThreadId: string, child: SubAgentChild): DesktopPluginSubAgent {
  return {
    parentThreadId,
    childThreadId: child.childThreadId,
    agentPath: child.agentPath ?? null,
    nickname: child.nickname,
    state: desktopPluginSubAgentState(child),
    summary: child.lastMessagePreview
  }
}

export function listDesktopPluginSubAgents(parentThreadId: string): readonly DesktopPluginSubAgent[] {
  const children = useSubAgentStore.getState().childrenByParent.get(parentThreadId)
  if (!children || children.length === 0) return NO_SUBAGENTS
  let snapshot = snapshots.get(children)
  if (!snapshot) {
    snapshot = children.map((child) => toDesktopPluginSubAgent(parentThreadId, child))
    snapshots.set(children, snapshot)
  }
  return snapshot
}

function sameSubAgents(a: readonly DesktopPluginSubAgent[], b: readonly DesktopPluginSubAgent[]): boolean {
  return a.length === b.length && a.every((entry, index) => {
    const other = b[index]
    return entry.childThreadId === other.childThreadId
      && entry.agentPath === other.agentPath
      && entry.nickname === other.nickname
      && entry.state === other.state
      && entry.summary === other.summary
  })
}

export function onDesktopPluginSubAgentsChange(
  parentThreadId: string,
  listener: (subagents: readonly DesktopPluginSubAgent[]) => void
): () => void {
  let current = listDesktopPluginSubAgents(parentThreadId)
  return useSubAgentStore.subscribe((state, previous) => {
    if (state.childrenByParent === previous.childrenByParent) return
    const next = listDesktopPluginSubAgents(parentThreadId)
    if (sameSubAgents(current, next)) return
    current = next
    listener(next)
  })
}

import { useEffect } from 'react'
import { useConnectionStore } from '../stores/connectionStore'
import { undiscoveredSubAgents, useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useSubAgentTaskActivity } from '../stores/subAgentTaskActivity'
import type { SubAgentLookupSources } from '../utils/subAgentIdentity'

export function useSubAgentLookup(sourceThreadId: string, enabled = true) {
  const childrenByParent = useSubAgentStore((state) => state.childrenByParent)
  const discoveryByParent = useSubAgentStore((state) => state.discoveryByParent)
  const discovery = discoveryByParent.get(sourceThreadId) ?? undiscoveredSubAgents
  const threadList = useThreadStore((state) => state.threadList)
  const activeThread = useThreadStore((state) => state.activeThread)
  useThreadStore((state) => state.runtimeSnapshots)
  useSubAgentTaskActivity()
  const supported = useConnectionStore((state) => state.capabilities?.subAgentSessions === true)
  const connectionEpoch = useConnectionStore((state) => state.connectionEpoch)
  useEffect(() => {
    if (enabled && sourceThreadId && supported) {
      void useSubAgentStore.getState().ensureChildren(sourceThreadId).catch(() => {})
    }
  }, [sourceThreadId, enabled, supported, connectionEpoch, discovery.status])
  const lookup: SubAgentLookupSources = { sourceThreadId, childrenByParent, discoveryByParent, threadList, activeThread }
  return { lookup, discovery }
}

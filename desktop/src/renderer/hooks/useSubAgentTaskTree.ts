import { useMemo } from 'react'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useSubAgentTaskActivity } from '../stores/subAgentTaskActivity'
import { useThreadStore } from '../stores/threadStore'
import { projectThreadActivity } from '../stores/subAgentActivity'

export { subAgentDescendants } from '../stores/subAgentActivity'

export function useSubAgentTaskTree(threadId: string | null) {
  const graph = useSubAgentStore(state => state.childrenByParent)
  const tasks = useSubAgentTaskActivity(state => state.tasksByThread)
  const reconciling = useSubAgentTaskActivity(state => state.reconcilingThreads)
  const runtimes = useThreadStore(state => state.runtimeSnapshots)
  return useMemo(() => projectThreadActivity(threadId, { graph, tasks, runtimes, reconciling }),
    [threadId, graph, tasks, runtimes, reconciling])
}

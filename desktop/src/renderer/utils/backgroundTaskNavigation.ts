import { requestToolCallReveal } from '../stores/toolCallRevealStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { openSubAgent } from './subAgentNavigation'

export type BackgroundTaskKind = 'terminal' | 'subagent' | 'workflow'

export function openBackgroundTask(threadId: string, kind: BackgroundTaskKind, taskId: string, workflowTabLabel: string): void {
  if (kind === 'workflow') {
    const tabId = useDetailPanelStore.getState().openWorkflow({ threadId, runId: taskId, initialLabel: workflowTabLabel })
    useDetailPanelStore.getState().setActiveViewerTab(tabId)
    return
  }
  if (kind === 'subagent') {
    const child = useSubAgentStore.getState().childrenByParent.get(threadId)?.find((candidate) => candidate.agentPath === taskId)
    if (child) openSubAgent(threadId, child.childThreadId)
    else requestToolCallReveal(threadId, { agentPath: taskId })
    return
  }
  requestToolCallReveal(threadId, { terminalSessionId: taskId })
}

import { requestToolCallReveal } from '../stores/toolCallRevealStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { openSubAgent } from './subAgentNavigation'

export type BackgroundTaskKind = 'terminal' | 'subagent' | 'workflow'

export function openBackgroundTask(
  threadId: string, kind: BackgroundTaskKind, taskId: string, workflowTabLabel: string, subAgentThreadId?: string
): void {
  if (kind === 'workflow') {
    const tabId = useDetailPanelStore.getState().openWorkflow({ threadId, runId: taskId, initialLabel: workflowTabLabel })
    useDetailPanelStore.getState().setActiveViewerTab(tabId)
    return
  }
  if (kind === 'subagent') {
    if (subAgentThreadId) openSubAgent(threadId, subAgentThreadId)
    else requestToolCallReveal(threadId, { agentPath: taskId })
    return
  }
  requestToolCallReveal(threadId, { terminalSessionId: taskId })
}

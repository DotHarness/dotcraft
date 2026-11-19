import { requestToolCallReveal } from '../stores/toolCallRevealStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { openSubAgent } from './subAgentNavigation'
import { openThread } from '../components/sidebar/projectConnectionActions'

export type BackgroundTaskKind = 'terminal' | 'subagent' | 'workflow' | 'thread'

export function openBackgroundTask(
  threadId: string, kind: BackgroundTaskKind, taskId: string, workflowTabLabel: string, subAgentThreadId?: string
): void {
  if (kind === 'workflow') {
    const tabId = useDetailPanelStore.getState().openWorkflow({ threadId, runId: taskId, initialLabel: workflowTabLabel })
    useDetailPanelStore.getState().setActiveViewerTab(tabId)
    return
  }
  if (kind === 'thread') {
    void openThread(taskId)
    return
  }
  if (kind === 'subagent') {
    if (subAgentThreadId) openSubAgent(threadId, subAgentThreadId)
    else requestToolCallReveal(threadId, { agentPath: taskId })
    return
  }
  requestToolCallReveal(threadId, { terminalSessionId: taskId })
}

import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'

export function openSubAgent(sourceThreadId: string, childThreadId: string | null | undefined): void {
  const panelThreadId = useThreadStore.getState().activeThreadId ?? sourceThreadId
  useSubAgentStore.getState().selectChild(panelThreadId, childThreadId ?? null)
  useDetailPanelStore.getState().setActiveDetailTab('subagents')
}

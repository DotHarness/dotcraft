import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

export function openSubAgent(sourceThreadId: string, childThreadId: string | null | undefined): void {
  const panelThreadId = useThreadStore.getState().activeThreadId ?? sourceThreadId
  useSubAgentStore.getState().selectChild(panelThreadId, childThreadId ?? null)
  useUIStore.getState().setActiveDetailTab('subagents')
}

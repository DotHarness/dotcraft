import type { SubAgentChild } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

export function openSubAgent(sourceThreadId: string, child: SubAgentChild | null): void {
  useThreadStore.getState().setActiveThreadId(child?.childThreadId ?? sourceThreadId)
  useUIStore.getState().setActiveMainView('conversation')
  if (!child) useUIStore.getState().setActiveDetailTab('subagents')
}

import { useThreadStore } from '../stores/threadStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'

export function welcomeScopeKey(projectKey: string): string {
  return `welcome:${projectKey}`
}

export function activeDetailScopeId(): string | null {
  return useThreadStore.getState().activeThreadId ?? useDetailPanelStore.getState().welcomeScopeId
}

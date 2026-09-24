import { useThreadStore } from '../stores/threadStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

export function welcomeScopeKey(projectKey: string): string {
  return `welcome:${projectKey}`
}

export function activeDetailScopeId(): string | null {
  return useThreadStore.getState().activeThreadId ?? useViewerTabStore.getState().welcomeScopeId
}

import { useThreadStore } from '../stores/threadStore'

export function hasRunningBackgroundWork(threadId: string): boolean {
  return useThreadStore.getState().runtimeSnapshots.get(threadId)?.hasBackgroundWork === true
}

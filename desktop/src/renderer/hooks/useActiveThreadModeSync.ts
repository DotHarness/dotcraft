import { useEffect } from 'react'
import { useThreadStore } from '../stores/threadStore'
import { resetThreadModeScope, synchronizeThreadMode } from '../stores/threadModeSynchronization'

export { requestThreadModeChange } from '../stores/threadModeSynchronization'

export function useActiveThreadModeSync(selectedWorkspaceKey: string): void {
  const activeThreadId = useThreadStore((state) => state.activeThreadId)
  const activeThread = useThreadStore((state) => state.activeThread)

  useEffect(() => {
    synchronizeThreadMode(selectedWorkspaceKey)
    const unsubscribe = useThreadStore.subscribe((state, previous) => {
      if (state.activeThreadId !== previous.activeThreadId) {
        resetThreadModeScope()
      } else {
        synchronizeThreadMode(selectedWorkspaceKey)
      }
    })
    return () => {
      unsubscribe()
      resetThreadModeScope()
    }
  }, [selectedWorkspaceKey])

  useEffect(() => {
    synchronizeThreadMode(selectedWorkspaceKey)
  }, [activeThread, activeThreadId, selectedWorkspaceKey])
}

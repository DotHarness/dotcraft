import { useEffect } from 'react'
import { useThreadStore } from '../stores/threadStore'
import { useUserInputAutoResolutionStore } from '../stores/userInputAutoResolutionStore'

function activeThreadId(): string | null {
  return useThreadStore.getState().activeThreadId
}

export function useUserInputAutoResolutionBridge(): void {
  useEffect(() => {
    const replace = useUserInputAutoResolutionStore.getState().replace
    const unsubscribeState = window.api.appServer.onUserInputAutoResolutionChanged(replace)
    void window.api.appServer.getUserInputAutoResolutionSnapshot().then(replace)

    const syncPresentedThread = (): void => {
      void window.api.appServer.setUserInputConversationPresented(activeThreadId())
    }
    syncPresentedThread()
    const unsubscribeThread = useThreadStore.subscribe(syncPresentedThread)

    const recordActivity = (event: KeyboardEvent | PointerEvent): void => {
      if (useUserInputAutoResolutionStore.getState().states.size === 0) return
      const target = event.target
      if (target instanceof Element && target.closest('[data-user-input-request="true"]')) return
      const threadId = activeThreadId()
      if (threadId) void window.api.appServer.recordUserInputConversationActivity(threadId)
    }
    document.addEventListener('keydown', recordActivity, true)
    document.addEventListener('pointerdown', recordActivity, true)

    return () => {
      unsubscribeState()
      unsubscribeThread()
      document.removeEventListener('keydown', recordActivity, true)
      document.removeEventListener('pointerdown', recordActivity, true)
      void window.api.appServer.setUserInputConversationPresented(null)
    }
  }, [])
}

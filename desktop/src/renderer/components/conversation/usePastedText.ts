import { useCallback } from 'react'
import { create } from 'zustand'
import { useComposerContextStore } from '../../stores/composerContextStore'
import { addToast } from '../../stores/toastStore'

export interface PendingPaste { id: string; threadId: string; text: string; status: 'writing' | 'failed' }
export const usePendingPasteStore = create<{ pastes: PendingPaste[]; setPaste(paste: PendingPaste): void; remove(id: string): void }>((set) => ({
  pastes: [],
  setPaste: (paste) => set((state) => ({ pastes: [...state.pastes.filter((item) => item.id !== paste.id), paste] })),
  remove: (id) => set((state) => ({ pastes: state.pastes.filter((item) => item.id !== id) }))
}))

export function hasPendingPastedText(threadId: string): boolean {
  return usePendingPasteStore.getState().pastes.some((paste) => paste.threadId === threadId)
}

export function usePastedText(threadId: string, workspacePath: string, remoteWorkspace = false) {
  const pastes = usePendingPasteStore((state) => state.pastes)
  const writePaste = useCallback((paste: PendingPaste): void => {
    usePendingPasteStore.getState().setPaste({ ...paste, status: 'writing' })
    void window.api.workspace.createPastedText({ text: paste.text, workspacePath }).then((context) => {
      if (!usePendingPasteStore.getState().pastes.some((item) => item.id === paste.id)) return
      useComposerContextStore.getState().addContext(threadId, context)
      usePendingPasteStore.getState().remove(paste.id)
    }).catch((error) => {
      if (!usePendingPasteStore.getState().pastes.some((item) => item.id === paste.id)) return
      usePendingPasteStore.getState().setPaste({ ...paste, status: 'failed' })
      addToast(error instanceof Error ? error.message : String(error), 'error')
    })
  }, [threadId, workspacePath])
  const onPasteText = useCallback((text: string): boolean => {
    if (remoteWorkspace) return false
    writePaste({ id: crypto.randomUUID(), threadId, text, status: 'writing' })
    return true
  }, [threadId, remoteWorkspace, writePaste])
  const pendingPastes = pastes.filter((paste) => paste.threadId === threadId)
  return { onPasteText, pending: pendingPastes.length, pendingPastes, retry: writePaste, discard: usePendingPasteStore.getState().remove }
}

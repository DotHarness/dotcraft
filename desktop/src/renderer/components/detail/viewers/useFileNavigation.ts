import { useEffect, type RefObject } from 'react'
import type { EditorView } from '@codemirror/view'
import type { FileNavigationHint } from '../../../../shared/viewer/types'
import { useFileEditorStore } from '../../../stores/fileEditorStore'
import { fileNavigationRange, navigateFile } from './fileNavigation'

interface FileNavigationOptions {
  tabId: string
  absolutePath: string
  view: RefObject<EditorView | null>
  hint?: FileNavigationHint
  revision: number
  status?: string
  mode: string
  review: boolean
}

export function useFileNavigation({
  tabId, absolutePath, view, hint, revision, status, mode, review
}: FileNavigationOptions): void {
  useEffect(() => {
    const store = useFileEditorStore.getState()
    const session = store.sessions.get(tabId)
    if (!view.current || status !== 'ready' || review || session?.lastNavigationRevision === revision) return
    let cancelled = false
    let frame: number | undefined
    if (session?.markdown && mode === 'preview' && fileNavigationRange(view.current.state, hint)) {
      void store.switchMode(tabId, 'source')
    } else {
      // The editor's snapshot restoration frame was queued first; explicit navigation wins after it.
      frame = requestAnimationFrame(() => {
        if (cancelled || !view.current) return
        navigateFile(view.current, hint)
        useFileEditorStore.getState().markNavigationHandled(tabId, revision)
      })
    }
    return () => {
      cancelled = true
      if (frame !== undefined) cancelAnimationFrame(frame)
    }
  }, [tabId, absolutePath, view, hint, revision, status, mode, review])
}

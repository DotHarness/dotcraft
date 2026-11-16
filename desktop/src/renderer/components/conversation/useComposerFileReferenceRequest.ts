import { useEffect, type RefObject } from 'react'
import { useComposerFileReferenceStore } from '../../stores/composerFileReferenceStore'
import { toWorkspaceRelativePath } from '../../utils/workspacePaths'
import type { RichInputAreaHandle } from './RichInputArea'

export function useComposerFileReferenceRequest(
  richRef: RefObject<RichInputAreaHandle | null>,
  workspacePath: string,
  disabled: boolean,
  scopeId: string | null,
  ready: boolean
): void {
  const request = useComposerFileReferenceStore((state) => state.pendingByScope.get(scopeId))

  useEffect(() => {
    if (!request || disabled || !ready || !richRef.current) return
    const paths = useComposerFileReferenceStore.getState().consume(scopeId)
    if (paths.length === 0) return
    for (const path of paths) {
      richRef.current.insertFileTagAtSelection(toWorkspaceRelativePath(workspacePath, path))
    }
  }, [disabled, ready, request, richRef, scopeId, workspacePath])
}

import { useEffect, type RefObject } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { toWorkspaceRelativePath } from '../../utils/workspacePaths'
import type { RichInputAreaHandle } from './RichInputArea'

export function useComposerFileReferenceRequest(
  richRef: RefObject<RichInputAreaHandle | null>,
  workspacePath: string,
  remoteWorkspace: boolean,
  disabled: boolean,
  scopeId: string | null,
  ready: boolean
): void {
  const t = useT()
  const request = useUIStore((state) => state.composerFileReferenceRequest)

  useEffect(() => {
    if (!request || request.scopeId !== scopeId || disabled || !ready || !richRef.current) return
    const paths = useUIStore.getState().consumeComposerFileReferenceRequest(scopeId)
    if (paths.length === 0) return
    if (remoteWorkspace) {
      addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
      return
    }
    for (const path of paths) {
      richRef.current.insertFileTagAtSelection(toWorkspaceRelativePath(workspacePath, path))
    }
  }, [disabled, ready, remoteWorkspace, request, richRef, scopeId, t, workspacePath])
}

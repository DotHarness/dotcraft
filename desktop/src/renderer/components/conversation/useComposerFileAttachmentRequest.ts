import { useEffect, useRef } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import type { ComposerFileAttachment } from '../../types/conversation'

export function useComposerFileAttachmentRequest(
  remoteWorkspace: boolean,
  attach: (file: ComposerFileAttachment) => void
): void {
  const t = useT()
  const request = useUIStore((s) => s.composerFileAttachmentRequest)
  const attachRef = useRef(attach)
  attachRef.current = attach

  useEffect(() => {
    if (!request) return
    const attachment = useUIStore.getState().consumeComposerFileAttachmentRequest()
    if (!attachment) return
    if (remoteWorkspace) {
      addToast(t('input.remoteLocalFilesUnavailable'), 'warning')
      return
    }
    attachRef.current(attachment)
  }, [request, remoteWorkspace, t])
}

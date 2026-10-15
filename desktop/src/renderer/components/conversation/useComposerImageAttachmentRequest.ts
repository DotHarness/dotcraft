import { useEffect, useRef } from 'react'
import { useUIStore, type ComposerImageAttachmentRequest } from '../../stores/uiStore'

export function useComposerImageAttachmentRequest(
  attach: (image: ComposerImageAttachmentRequest) => void,
  enabled = true
): void {
  const request = useUIStore((s) => s.composerImageAttachmentRequest)
  const attachRef = useRef(attach)
  attachRef.current = attach

  useEffect(() => {
    if (!request || !enabled) return
    const image = useUIStore.getState().consumeComposerImageAttachmentRequest()
    if (image) attachRef.current(image)
  }, [enabled, request])
}

import { useEffect, type RefObject } from 'react'
import { useUIStore } from '../../stores/uiStore'
import type { RichInputAreaHandle } from './RichInputArea'

export function useComposerPrefillRequest(richRef: RefObject<RichInputAreaHandle | null>, ready: boolean): void {
  const prefill = useUIStore((state) => state.composerPrefill)

  useEffect(() => {
    const input = richRef.current
    if (!prefill || !ready || !input) return
    useUIStore.getState().consumeComposerPrefill()
    input.setPlainText(prefill)
    input.setSelectionRange({ start: prefill.length, end: prefill.length })
    input.focus()
  }, [prefill, ready, richRef])
}

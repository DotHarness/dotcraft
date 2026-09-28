import { useEffect, type RefObject } from 'react'

export function useImportDialogFocus(ref: RefObject<HTMLDivElement | null>, onClose: () => void, busy: boolean): void {
  useEffect(() => {
    const opener = document.activeElement
    const dialog = ref.current
    dialog?.querySelector<HTMLElement>('button, input')?.focus()
    return () => { if (opener instanceof HTMLElement) opener.focus() }
  }, [ref])

  useEffect(() => {
    const dialog = ref.current
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        event.preventDefault()
        if (!busy) onClose()
      }
      if (event.key !== 'Tab' || !dialog) return
      const focusable = Array.from(dialog.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), [tabindex="0"]'))
        .filter(element => element.getClientRects().length > 0)
      const first = focusable[0]
      const last = focusable.at(-1)
      if (event.shiftKey && (document.activeElement === first || !dialog.contains(document.activeElement))) {
        event.preventDefault()
        last?.focus()
      } else if (!event.shiftKey && (document.activeElement === last || !dialog.contains(document.activeElement))) {
        event.preventDefault()
        first?.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [busy, onClose, ref])
}

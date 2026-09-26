import { useCallback, useEffect, useRef, useState } from 'react'

const OPEN_DELAY_MS = 250
const SKIP_DELAY_MS = 300
const CLOSE_GRACE_MS = 120

let lastClosedAt = Number.NEGATIVE_INFINITY

export interface PreviewCardVisibility {
  open: boolean
  show: () => void
  hide: () => void
  /** Closes after a grace period, so the pointer can cross from the rail into the card. */
  hideSoon: () => void
}

export function usePreviewCardVisibility(onEscape: () => void): PreviewCardVisibility {
  const [open, setOpen] = useState(false)
  const openRef = useRef(false)
  const openTimerRef = useRef<number | null>(null)
  const closeTimerRef = useRef<number | null>(null)
  const onEscapeRef = useRef(onEscape)
  onEscapeRef.current = onEscape

  const clearTimers = useCallback((): void => {
    if (openTimerRef.current !== null) window.clearTimeout(openTimerRef.current)
    if (closeTimerRef.current !== null) window.clearTimeout(closeTimerRef.current)
    openTimerRef.current = null
    closeTimerRef.current = null
  }, [])

  const reveal = useCallback((): void => {
    openTimerRef.current = null
    openRef.current = true
    setOpen(true)
  }, [])

  const show = useCallback((): void => {
    if (closeTimerRef.current !== null) {
      window.clearTimeout(closeTimerRef.current)
      closeTimerRef.current = null
    }
    if (openRef.current || openTimerRef.current !== null) return
    if (performance.now() - lastClosedAt < SKIP_DELAY_MS) reveal()
    else openTimerRef.current = window.setTimeout(reveal, OPEN_DELAY_MS)
  }, [reveal])

  const hide = useCallback((): void => {
    clearTimers()
    if (!openRef.current) return
    openRef.current = false
    lastClosedAt = performance.now()
    setOpen(false)
  }, [clearTimers])

  const hideSoon = useCallback((): void => {
    if (!openRef.current) hide()
    else if (closeTimerRef.current === null) closeTimerRef.current = window.setTimeout(hide, CLOSE_GRACE_MS)
  }, [hide])

  useEffect(() => {
    if (!open) return
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      onEscapeRef.current()
      hide()
    }
    document.addEventListener('keydown', onKeyDown, true)
    window.addEventListener('blur', hide)
    return () => {
      document.removeEventListener('keydown', onKeyDown, true)
      window.removeEventListener('blur', hide)
    }
  }, [hide, open])

  useEffect(() => clearTimers, [clearTimers])

  return { open, show, hide, hideSoon }
}

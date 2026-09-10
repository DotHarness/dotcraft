import { useCallback, useEffect, useRef, useState } from 'react'
import type { PetStatus, PetStatusInfo } from '../../../shared/desktopPet'

export type PetActivityViewState = 'hidden' | 'pill'

const SEVERITY: Record<PetStatus, number> = { idle: 0, running: 1, review: 2, failed: 3, waiting: 4 }
const IDLE_GRACE_MS = 2500

/**
 * When the activity pill shows itself: it opens for a new or escalating turn, hides a while after
 * the source goes quiet, and a dismissal sticks to its turn until that turn needs more attention.
 */
export function usePetActivityView(status: PetStatusInfo | undefined, hold: boolean): {
  view: PetActivityViewState
  show: () => void
  dismiss: () => void
} {
  const [view, setView] = useState<PetActivityViewState>('hidden')
  const manual = useRef(false)
  const previous = useRef<{ status: PetStatus; turnId: string; line: string } | null>(null)
  const dismissed = useRef<{ turnId: string; severity: number } | null>(null)
  const key: PetStatus = status?.status ?? 'idle'
  const severity = SEVERITY[key]
  const turnId = status?.turnId ?? ''
  const line = status?.line ?? ''
  const current = useRef({ turnId, severity })
  current.current = { turnId, severity }

  useEffect(() => {
    const prev = previous.current
    previous.current = { status: key, turnId, line }
    if (severity === 0) return
    const newTurn = prev == null || prev.turnId !== turnId
    const escalated = prev != null && SEVERITY[prev.status] < severity
    const lineChanged = key === 'running' && prev != null && prev.line !== line
    const suppressed = dismissed.current?.turnId === turnId && dismissed.current.severity >= severity
    if (!suppressed && (newTurn || escalated || lineChanged)) setView('pill')
  }, [key, turnId, line, severity])

  useEffect(() => {
    if (severity > 0 || view === 'hidden' || hold || manual.current) return undefined
    const timer = setTimeout(() => setView('hidden'), IDLE_GRACE_MS)
    return () => clearTimeout(timer)
  }, [severity, view, hold])

  const show = useCallback(() => { manual.current = true; setView('pill') }, [])
  const dismiss = useCallback(() => {
    manual.current = false
    dismissed.current = { ...current.current }
    setView('hidden')
  }, [])
  return { view, show, dismiss }
}

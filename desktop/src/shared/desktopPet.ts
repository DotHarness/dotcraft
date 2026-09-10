import type { AppLocale } from './locales/types'

export interface PetPoint { x: number; y: number }
export interface PetRect extends PetPoint { width: number; height: number }
export type PetActivity = 'idle' | 'thinking' | 'working' | 'waiting' | 'blocked' | 'done'
export function petActivity(value: unknown): PetActivity {
  return value === 'thinking' || value === 'working' || value === 'waiting' || value === 'blocked' || value === 'done' ? value : 'idle'
}

export type PetStatus = 'idle' | 'running' | 'waiting' | 'review' | 'failed'
export const PET_STATUSES: readonly PetStatus[] = ['idle', 'running', 'waiting', 'review', 'failed']
export type PetLineTone = 'neutral' | 'warning' | 'danger' | 'success'
export const PET_LINE_TONES: readonly PetLineTone[] = ['neutral', 'warning', 'danger', 'success']
/** What the pill's send control does with a draft while a turn runs: mirrors the Desktop composer preference. */
export type PetFollowUpMode = 'steer' | 'queue'
export const PET_FOLLOW_UP_MODES: readonly PetFollowUpMode[] = ['steer', 'queue']

export interface PetDecisionOption { value: string; label: string }
export interface PetDecision {
  /** Owner-minted key of the pending approval, echoed back verbatim with the chosen option. */
  id: string
  question: string
  operation: string
  target: string
  reason: string
  options: PetDecisionOption[]
  declineValue: string
}

/** What the companion knows about the source thread's work: derived by the owner, never streamed tokens. */
export interface PetStatusInfo {
  status: PetStatus
  title: string
  line: string
  lineTone: PetLineTone
  turnId: string
  canStop: boolean
  /** An interruption is in flight; set only while true and never together with canStop. */
  stopping?: boolean
  decision?: PetDecision
  patch?: { additions: number; deletions: number; files: number }
}

export function petPose(status: PetStatus): PetActivity {
  switch (status) {
    case 'waiting': return 'waiting'
    case 'failed': return 'blocked'
    case 'review': return 'done'
    case 'running': return 'working'
    default: return 'idle'
  }
}

export interface PetSnapshot {
  activity?: PetActivity
  status?: PetStatusInfo
  name: string
  text: string
  theme: 'dark' | 'light'
  locale: AppLocale
  reducedMotion: boolean
  canChat: boolean
  /** The source can chat but is not accepting input right now (sending, loading). */
  busy?: boolean
  followUpMode: PetFollowUpMode
  editRevision?: number
}
export type PetEvent =
  | { type: 'position'; point: PetPoint; size: number; phase: 'leaving' | 'pet' | 'returning'; held?: boolean; pose?: { scaleX: number; scaleY: number; rotation: number } }
  | { type: 'snapshot'; snapshot: PetSnapshot }
  | { type: 'ownership'; detached: boolean }
  | { type: 'return-seat' }
  | { type: 'edit'; text: string; submit: boolean; revision: number }
  | { type: 'stop'; turnId: string }
  | { type: 'read'; turnId: string }
  | { type: 'decision'; id: string; value: string }
export type PetCommand =
  | { type: 'detach'; seat: PetRect; point: PetPoint; snapshot: PetSnapshot; pointerHeld?: boolean }
  | { type: 'source-drag'; stage: 'move' | 'end' }
  /** Measured height of the activity pill below the pet, so placement leaves room for it. */
  | { type: 'layout'; height: number }
  | { type: 'stop'; turnId: string }
  | { type: 'read'; turnId: string }
  | { type: 'decision'; id: string; value: string }
  | { type: 'ready' }
  | { type: 'hidden' }
  | { type: 'return' }
  | { type: 'seat'; seat: PetRect | null }
  | { type: 'snapshot'; snapshot: PetSnapshot }
  | { type: 'edit'; text: string; submit: boolean; revision: number }
  | { type: 'drag'; stage: 'start' | 'move' | 'end' }
  | { type: 'interactive'; value: boolean }

export function clampPet(point: PetPoint, area: PetRect, size = 80, snap = false): PetPoint {
  const margin = 8
  const left = area.x + margin
  const right = Math.max(left, area.x + area.width - size - margin)
  let x = Math.max(left, Math.min(point.x, right))
  if (snap && x - left < 32) x = left
  if (snap && right - x < 32) x = right
  return { x, y: Math.max(area.y + margin, Math.min(point.y, area.y + area.height - size - margin)) }
}

export function inPetDetachZone(point: PetPoint, viewport: { width: number; height: number }, margin = 48): boolean {
  return point.x <= margin || point.y <= margin || point.x >= viewport.width - margin || point.y >= viewport.height - margin
}

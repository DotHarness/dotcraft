export interface PetPoint { x: number; y: number }
export interface PetRect extends PetPoint { width: number; height: number }
export type PetActivity = 'idle' | 'thinking' | 'working' | 'waiting' | 'blocked' | 'done'
export function petActivity(value: unknown): PetActivity {
  return value === 'thinking' || value === 'working' || value === 'waiting' || value === 'blocked' || value === 'done' ? value : 'idle'
}
export interface PetSnapshot {
  activity?: PetActivity
  name: string
  text: string
  theme: 'dark' | 'light'
  reducedMotion: boolean
  canChat: boolean
  editRevision?: number
}
export type PetEvent =
  | { type: 'position'; point: PetPoint; size: number; phase: 'leaving' | 'pet' | 'returning'; held?: boolean; landing?: PetPoint; pose?: { scaleX: number; scaleY: number; rotation: number } }
  | { type: 'snapshot'; snapshot: PetSnapshot }
  | { type: 'ownership'; detached: boolean }
  | { type: 'return-seat' }
  | { type: 'edit'; text: string; submit: boolean; revision: number }
export type PetCommand =
  | { type: 'detach'; seat: PetRect; point: PetPoint; snapshot: PetSnapshot; pointerHeld?: boolean }
  | { type: 'source-drag'; stage: 'move' | 'end' }
  | { type: 'chat'; open: boolean }
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

import {
  PET_FOLLOW_UP_MODES, PET_LINE_TONES, PET_STATUSES, petActivity,
  type PetDecision, type PetPoint, type PetRect, type PetSnapshot, type PetStatusInfo
} from '../shared/desktopPet'
import { SUPPORTED_LOCALE_VALUES } from '../shared/locales/types'

// Renderer payloads cross a trust boundary here: the owner caps content for the product,
// these bounds only stop a runaway or hostile renderer.
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.length <= max
const count = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= 1_000_000

export function validPoint(value: unknown): value is PetPoint {
  const point = value as PetPoint | null
  return !!point && Number.isFinite(point.x) && Number.isFinite(point.y)
}

export function validRect(value: unknown): value is PetRect {
  const rect = value as PetRect | null
  return validPoint(rect) && Number.isFinite(rect.width) && Number.isFinite(rect.height) && rect.width > 0 && rect.height > 0
}

function validDecisionShape(value: unknown): value is PetDecision {
  const decision = value as PetDecision | null
  return !!decision && typeof decision === 'object'
    && text(decision.id, 200) && decision.id.length > 0
    && text(decision.question, 400) && text(decision.operation, 400) && text(decision.target, 400) && text(decision.reason, 600)
    && Array.isArray(decision.options) && decision.options.length > 0 && decision.options.length <= 8
    // The owner clips labels to 80 code points; this bound counts UTF-16 units, so it must stay wider.
    && decision.options.every((option) => !!option && text(option.value, 64) && text(option.label, 160))
    && new Set(decision.options.map((option) => option.value)).size === decision.options.length
    && text(decision.declineValue, 64)
}

export function validStatus(value: unknown): value is PetStatusInfo {
  const status = value as PetStatusInfo | null
  if (!status || typeof status !== 'object') return false
  if (!PET_STATUSES.includes(status.status) || !PET_LINE_TONES.includes(status.lineTone)) return false
  if (!text(status.title, 200) || !text(status.line, 200) || !text(status.turnId, 200) || typeof status.canStop !== 'boolean') return false
  if (status.stopping !== undefined && typeof status.stopping !== 'boolean') return false
  if (status.decision !== undefined && !validDecisionShape(status.decision)) return false
  if (status.patch !== undefined && !(status.patch && count(status.patch.additions) && count(status.patch.deletions) && count(status.patch.files))) return false
  return true
}

export function validLayout(command: { height?: unknown }): boolean {
  const height = command.height
  return typeof height === 'number' && Number.isFinite(height) && height >= 0 && height <= 4000
}

export function validRead(command: { turnId?: unknown }): boolean {
  return text(command.turnId, 200) && command.turnId.length > 0
}

export function validStop(command: { turnId?: unknown }, snapshot: PetSnapshot | null): boolean {
  const status = snapshot?.status
  return !!status?.canStop && text(command.turnId, 200) && command.turnId === status.turnId
}

export function validDecisionCommand(command: { id?: unknown; value?: unknown }, snapshot: PetSnapshot | null): boolean {
  const decision = snapshot?.status?.decision
  return !!decision && text(command.id, 200) && command.id === decision.id
    && text(command.value, 64) && decision.options.some((option) => option.value === command.value)
}

export function validSnapshot(value: unknown): value is PetSnapshot {
  const snapshot = value as PetSnapshot | null
  return !!snapshot && text(snapshot.name, 1000) && text(snapshot.text, 100000)
    && (snapshot.theme === 'dark' || snapshot.theme === 'light')
    && SUPPORTED_LOCALE_VALUES.includes(snapshot.locale)
    && typeof snapshot.reducedMotion === 'boolean' && typeof snapshot.canChat === 'boolean'
    && (snapshot.busy === undefined || typeof snapshot.busy === 'boolean')
    && PET_FOLLOW_UP_MODES.includes(snapshot.followUpMode)
    && (snapshot.activity === undefined || petActivity(snapshot.activity) === snapshot.activity)
    && (snapshot.status === undefined || validStatus(snapshot.status))
}

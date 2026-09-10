export const MASCOT_SIZE = 58
export const MASCOT_SCALE = 0.75
export const MASCOT_HIDDEN_RATIO = 0.06
export const MASCOT_RAISE = 3
export const MASCOT_SLEEP_AFTER_MS = 90_000
export const MASCOT_WAVE_DURATION_MS = 1_600
export const MASCOT_ACTIVE_IDLE_MIN_MS = 35_000
export const MASCOT_ACTIVE_IDLE_JITTER_MS = 30_000
export const MASCOT_ACTIVE_IDLE_ACTIVITY_THROTTLE_MS = 500

export type MascotActiveIdleMotion = 'hop' | 'rocket' | 'hover'
export type MascotActiveIdlePhase = 'outbound' | 'away' | 'inbound'
export type MascotIdleDirection = 'left' | 'right'
export const MASCOT_ACTIVE_IDLE_MOTIONS: readonly MascotActiveIdleMotion[] = ['hop', 'rocket', 'hover']

export interface MascotActiveIdleState {
  motion: MascotActiveIdleMotion
  phase: MascotActiveIdlePhase
}

export interface MascotActiveIdle extends MascotActiveIdleState {
  /** Where the trip heads; the inbound leg travels the other way. */
  direction: MascotIdleDirection
  travel: MascotIdleDirection
}

export interface MascotActiveIdlePlan {
  direction?: MascotIdleDirection
  motions?: readonly MascotActiveIdleMotion[]
}

export interface MascotProfileTransition {
  fromAccent: string
  toAccent: string
}

export const MASCOT_PROFILE_TRANSITION_SWAP_MS = 620
export const MASCOT_PROFILE_TRANSITION_DURATION_MS = 1240

export const MASCOT_ACTIVE_IDLE_TRAVEL_MS: Record<MascotActiveIdleMotion, number> = {
  hop: 2400,
  rocket: 1800,
  hover: 1800
}

export const MASCOT_ACTIVE_IDLE_HOLD_MS: Record<MascotActiveIdleMotion, number> = {
  hop: 1400,
  rocket: 1400,
  hover: 2800
}

export function pickMascotActiveIdle(
  random: number,
  previous: MascotActiveIdleMotion | null,
  allowed: readonly MascotActiveIdleMotion[] = MASCOT_ACTIVE_IDLE_MOTIONS
): MascotActiveIdleMotion {
  const preferred: MascotActiveIdleMotion = random < 0.65 ? 'hop' : random < 0.9 ? 'rocket' : 'hover'
  const selected = allowed.includes(preferred)
    ? preferred
    : MASCOT_ACTIVE_IDLE_MOTIONS.find((motion) => allowed.includes(motion)) ?? preferred
  if (selected !== previous || allowed.length < 2) return selected
  const alternate: MascotActiveIdleMotion = selected === 'hop' ? 'rocket' : selected === 'rocket' ? 'hop' : 'rocket'
  return allowed.includes(alternate) ? alternate : allowed.find((motion) => motion !== selected) ?? selected
}

export const MASCOT_SPARKLES = Array.from({ length: 7 }, (_, i) => {
  const angle = ((-150 + i * 40) * Math.PI) / 180
  const radius = 26 + (i % 3) * 9
  return {
    dx: `${(Math.cos(angle) * radius).toFixed(1)}px`,
    dy: `${(Math.sin(angle) * radius - 8).toFixed(1)}px`,
    delay: `${i * 40}ms`
  }
})

export function prefersReducedMotion(): boolean {
  const configuredPreference =
    typeof document !== 'undefined' ? document.documentElement.dataset.reduceMotion : undefined
  if (configuredPreference === 'on') return true
  if (configuredPreference === 'off') return false

  return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-reduced-motion: reduce)')?.matches ?? false
    : false
}

export function mascotNameKey(name?: string): string {
  return name?.trim().normalize('NFC') ?? ''
}

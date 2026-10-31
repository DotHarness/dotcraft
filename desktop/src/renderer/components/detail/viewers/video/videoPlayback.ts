export const PLAYBACK_RATES = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2] as const

export const SEEK_STEP_SECONDS = 5

export function seekTarget(seconds: number, duration: number): number {
  const end = Number.isFinite(duration) && duration > 0 ? duration : Number.POSITIVE_INFINITY
  return Math.min(Math.max(0, seconds), end)
}

export function formatClock(seconds: number, duration: number): string {
  const whole = Number.isFinite(seconds) && seconds > 0 ? Math.floor(seconds) : 0
  const hours = Math.floor(whole / 3600)
  const minutes = Math.floor((whole % 3600) / 60)
  const rest = String(whole % 60).padStart(2, '0')
  return duration >= 3600 ? `${hours}:${String(minutes).padStart(2, '0')}:${rest}` : `${hours * 60 + minutes}:${rest}`
}

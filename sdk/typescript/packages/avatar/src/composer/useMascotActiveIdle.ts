import { useCallback, useEffect, useRef, useState, type AnimationEvent } from 'react'
import {
  MASCOT_ACTIVE_IDLE_ACTIVITY_THROTTLE_MS,
  MASCOT_ACTIVE_IDLE_HOLD_MS,
  MASCOT_ACTIVE_IDLE_JITTER_MS,
  MASCOT_ACTIVE_IDLE_MIN_MS,
  MASCOT_ACTIVE_IDLE_TRAVEL_MS,
  pickMascotActiveIdle,
  type MascotActiveIdle,
  type MascotActiveIdleMotion,
  type MascotActiveIdlePhase,
  type MascotActiveIdlePlan,
  type MascotIdleDirection
} from './constants.js'

export interface MascotActiveIdleAttributes {
  'data-mascot-active-idle'?: MascotActiveIdleMotion
  'data-mascot-idle-phase'?: MascotActiveIdlePhase
  'data-mascot-idle-direction'?: MascotIdleDirection
  'data-mascot-idle-travel'?: MascotIdleDirection
}

export interface MascotActiveIdleOptions {
  /** True while the character is free to wander: idle, awake, unfocused, and motion allowed. */
  enabled: boolean
  onStart?: () => void
  /** Runs on user activity, before the schedule-reset throttle. */
  onActivity?: () => void
  /** Decides direction and motion pool at launch; `null` skips this round. */
  plan?: () => MascotActiveIdlePlan | null
  /** Whether pointer movement aborts a trip in flight; it always defers the next one. */
  interruptOnPointerMove?: boolean
}

export interface MascotActiveIdleHandle {
  activeIdle: MascotActiveIdle | null
  activityRevision: number
  className: 'composer-mascot-active-idle' | undefined
  attributes: MascotActiveIdleAttributes
  cancel: () => void
  onAnimationEnd: (event: AnimationEvent<Element>) => void
}

const RETRY_MS = 5000
const LEG_SETTLE_MS = 160

function opposite(direction: MascotIdleDirection): MascotIdleDirection {
  return direction === 'left' ? 'right' : 'left'
}

function finisherOf(trip: MascotActiveIdle): string {
  if (trip.motion === 'hop') return 'composer-mascot-idle-hop-travel'
  if (trip.motion === 'rocket') return 'composer-mascot-idle-rocket-flight-x'
  return trip.phase === 'outbound' ? 'composer-mascot-idle-hover-launch-body' : 'composer-mascot-idle-hover-land-body'
}

function advance(trip: MascotActiveIdle | null): MascotActiveIdle | null {
  if (!trip) return null
  if (trip.phase === 'outbound') return { ...trip, phase: 'away' }
  if (trip.phase === 'away') return { ...trip, phase: 'inbound', travel: opposite(trip.direction) }
  return null
}

/**
 * Schedules the traveling idle antics (hop, rocket, hover) after a quiet stretch. Any host that
 * renders the stage classes (see MascotIdleStage) can spread the returned attributes on its root.
 */
export function useMascotActiveIdle(options: MascotActiveIdleOptions): MascotActiveIdleHandle {
  const { enabled, interruptOnPointerMove = true } = options
  const [activeIdle, setActiveIdle] = useState<MascotActiveIdle | null>(null)
  const [activityRevision, setActivityRevision] = useState(0)
  const optionsRef = useRef(options)
  optionsRef.current = options
  const activeRef = useRef(activeIdle)
  activeRef.current = activeIdle
  const lastActivityRef = useRef(0)
  const lastMotionRef = useRef<MascotActiveIdleMotion | null>(null)

  const cancel = useCallback(() => setActiveIdle(null), [])

  const markActivity = useCallback((interrupt: boolean) => {
    if (interrupt) setActiveIdle(null)
    optionsRef.current.onActivity?.()
    const now = Date.now()
    if (now - lastActivityRef.current < MASCOT_ACTIVE_IDLE_ACTIVITY_THROTTLE_MS) return
    lastActivityRef.current = now
    setActivityRevision((value) => value + 1)
  }, [])

  useEffect(() => {
    const interrupt = (): void => markActivity(true)
    const pointerMove = (): void => {
      if (Date.now() - lastActivityRef.current >= MASCOT_ACTIVE_IDLE_ACTIVITY_THROTTLE_MS) {
        markActivity(interruptOnPointerMove)
      }
    }
    window.addEventListener('keydown', interrupt)
    window.addEventListener('pointerdown', interrupt)
    window.addEventListener('pointermove', pointerMove, { passive: true })
    window.addEventListener('wheel', interrupt, { passive: true })
    window.addEventListener('focusin', interrupt)
    return () => {
      window.removeEventListener('keydown', interrupt)
      window.removeEventListener('pointerdown', interrupt)
      window.removeEventListener('pointermove', pointerMove)
      window.removeEventListener('wheel', interrupt)
      window.removeEventListener('focusin', interrupt)
    }
  }, [markActivity, interruptOnPointerMove])

  useEffect(() => {
    if (!enabled) {
      setActiveIdle(null)
      return undefined
    }
    let timer = 0
    const wait = (): number => MASCOT_ACTIVE_IDLE_MIN_MS + Math.random() * MASCOT_ACTIVE_IDLE_JITTER_MS
    const start = (): void => {
      if (document.hidden || activeRef.current) {
        timer = window.setTimeout(start, RETRY_MS)
        return
      }
      const plan = optionsRef.current.plan?.()
      if (plan === null) {
        timer = window.setTimeout(start, wait())
        return
      }
      const motion = pickMascotActiveIdle(Math.random(), lastMotionRef.current, plan?.motions)
      lastMotionRef.current = motion
      optionsRef.current.onStart?.()
      const direction = plan?.direction ?? 'left'
      setActiveIdle({ motion, phase: 'outbound', direction, travel: direction })
    }
    timer = window.setTimeout(start, wait())
    return () => window.clearTimeout(timer)
  }, [enabled, activityRevision])

  useEffect(() => {
    if (!activeIdle) return undefined
    const delay = activeIdle.phase === 'away'
      ? MASCOT_ACTIVE_IDLE_HOLD_MS[activeIdle.motion]
      : MASCOT_ACTIVE_IDLE_TRAVEL_MS[activeIdle.motion] + LEG_SETTLE_MS
    const timer = window.setTimeout(() => setActiveIdle(advance), delay)
    return () => window.clearTimeout(timer)
  }, [activeIdle])

  const onAnimationEnd = useCallback((event: AnimationEvent<Element>) => {
    setActiveIdle((current) => {
      if (!current || event.animationName !== finisherOf(current)) return current
      return current.phase === 'outbound' ? { ...current, phase: 'away' } : null
    })
  }, [])

  const attributes: MascotActiveIdleAttributes = activeIdle
    ? {
        'data-mascot-active-idle': activeIdle.motion,
        'data-mascot-idle-phase': activeIdle.phase,
        'data-mascot-idle-direction': activeIdle.direction,
        'data-mascot-idle-travel': activeIdle.travel
      }
    : {}
  return {
    activeIdle,
    activityRevision,
    className: activeIdle ? 'composer-mascot-active-idle' : undefined,
    attributes,
    cancel,
    onAnimationEnd
  }
}

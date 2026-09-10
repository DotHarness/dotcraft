import { useEffect, useRef, useState, type AnimationEvent } from 'react'
import {
  MASCOT_SLEEP_AFTER_MS, useComposerAvatarBehavior, useMascotActiveIdle,
  type MascotActiveIdle, type MascotActiveIdleAttributes
} from '@dotcraft/avatar/react'
import type { PetActivity } from '../../../shared/desktopPet'
import { petActiveIdlePlan } from './petActiveIdlePlan'

export interface PetIdleOptions {
  activity: PetActivity
  /** Settled on the desktop with nothing else going on: no chat, no drag, no reaction. */
  available: boolean
  reduced: boolean
  position: { x: number; y: number; size: number }
}

export function usePetIdle({ activity, available, reduced, position }: PetIdleOptions): ReturnType<typeof useComposerAvatarBehavior> & {
  sleeping: boolean
  activeIdle: MascotActiveIdle | null
  idleClassName: string | undefined
  idleAttributes: MascotActiveIdleAttributes
  onAnimationEnd: (event: AnimationEvent<Element>) => void
} {
  const [sleeping, setSleeping] = useState(false)
  const ambient = available && activity === 'idle' && !reduced
  const positionRef = useRef(position)
  positionRef.current = position
  const idle = useMascotActiveIdle({
    enabled: ambient && !sleeping,
    onActivity: () => setSleeping(false),
    // The overlay spans the whole work area, so a pointer move anywhere would yank a trip home.
    interruptOnPointerMove: false,
    plan: () => petActiveIdlePlan(positionRef.current, { width: window.innerWidth, height: window.innerHeight })
  })
  useEffect(() => {
    if (!ambient) { setSleeping(false); return }
    if (sleeping || idle.activeIdle) return
    const timer = setTimeout(() => setSleeping(true), MASCOT_SLEEP_AFTER_MS)
    return () => clearTimeout(timer)
  }, [ambient, idle.activityRevision, sleeping, idle.activeIdle])
  const asleep = ambient && sleeping
  const behavior = useComposerAvatarBehavior({
    semanticPose: asleep ? 'sleep' : activity,
    baseExpression: 'neutral', focused: false, dragOver: false,
    sleeping: asleep, waving: false, activeIdle: !ambient || idle.activeIdle != null,
    bounceSignal: 0, reducedMotion: reduced,
    // Directional movement belongs to pointer gaze, avoiding competing face transforms.
    directionalGestures: false
  })
  return {
    ...behavior,
    sleeping: asleep,
    gesture: ambient && !asleep ? behavior.gesture : undefined,
    activeIdle: idle.activeIdle,
    idleClassName: idle.className,
    idleAttributes: idle.attributes,
    onAnimationEnd: idle.onAnimationEnd
  }
}

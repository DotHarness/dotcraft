import { useEffect, useState } from 'react'
import { MASCOT_SLEEP_AFTER_MS, useComposerAvatarBehavior } from '@dotcraft/avatar/react'
import type { PetActivity } from '../../../shared/desktopPet'

export function usePetIdle(activity: PetActivity, available: boolean, reduced: boolean): ReturnType<typeof useComposerAvatarBehavior> & { sleeping: boolean } {
  const [sleeping, setSleeping] = useState(false)
  const ambient = available && activity === 'idle' && !reduced
  useEffect(() => {
    setSleeping(false)
    if (!ambient) return
    let timer: ReturnType<typeof setTimeout>
    const wake = (): void => {
      clearTimeout(timer)
      setSleeping(false)
      timer = setTimeout(() => setSleeping(true), MASCOT_SLEEP_AFTER_MS)
    }
    wake()
    const events = ['pointermove', 'pointerdown', 'keydown', 'wheel', 'focusin'] as const
    for (const event of events) window.addEventListener(event, wake, { passive: true })
    return () => {
      clearTimeout(timer)
      for (const event of events) window.removeEventListener(event, wake)
    }
  }, [ambient])
  const asleep = ambient && sleeping
  const behavior = useComposerAvatarBehavior({
    semanticPose: asleep ? 'sleep' : activity,
    baseExpression: 'neutral', focused: false, dragOver: false,
    sleeping: asleep, waving: false, activeIdle: !ambient,
    bounceSignal: 0, reducedMotion: reduced,
    // Directional movement belongs to pointer gaze, avoiding competing face transforms.
    directionalGestures: false
  })
  return { ...behavior, sleeping: asleep, gesture: ambient && !asleep ? behavior.gesture : undefined }
}

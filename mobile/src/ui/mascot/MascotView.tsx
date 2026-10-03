'use dom'

import { deriveAppearance, originalAppearance } from '@dotcraft/avatar'
import { AppearanceAvatar } from '@dotcraft/avatar/react'
import '@dotcraft/avatar/styles.css'
import { IS_DOM, type DOMProps } from 'expo/dom'
import { useEffect, useMemo, useState } from 'react'

export type MascotPose = 'idle' | 'thinking' | 'working' | 'waiting' | 'blocked' | 'done' | 'greeting' | 'sleep'

const LOOKS = ['look-left', 'look-right'] as const

if (IS_DOM) document.title = ''

function useLookAround(active: boolean): { gesture?: (typeof LOOKS)[number]; sequence: number } {
  const [sequence, setSequence] = useState(0)
  useEffect(() => {
    if (!active) return
    const timer = window.setInterval(() => setSequence((value) => value + 1), 1600)
    return () => window.clearInterval(timer)
  }, [active])
  return active && sequence > 0 ? { gesture: LOOKS[(sequence - 1) % 2], sequence } : { sequence }
}

export default function MascotView({
  pose,
  happy,
  lookAround,
  profile,
  size,
  canvas,
  motion,
}: {
  pose: MascotPose
  happy: boolean
  lookAround: boolean
  profile: string | null
  size: number
  canvas: number
  motion: boolean
  dom?: DOMProps
}) {
  const appearance = useMemo(() => (profile ? deriveAppearance(profile) : originalAppearance), [profile])
  const look = useLookAround(motion && lookAround)

  useEffect(() => {
    if (!IS_DOM) return
    for (const element of [document.documentElement, document.body]) {
      element.style.margin = '0'
      element.style.padding = '0'
      element.style.background = 'transparent'
      element.style.overflow = 'hidden'
    }
  }, [])

  return (
    <span
      style={{ display: 'flex', width: canvas, height: canvas, alignItems: 'center', justifyContent: 'center', lineHeight: 0 }}
      aria-hidden="true"
    >
      <AppearanceAvatar
        appearance={appearance}
        size={size}
        state={pose}
        expression={happy ? 'happy' : undefined}
        gesture={look.gesture}
        gestureSequence={look.sequence}
        motion={motion ? 'on' : 'off'}
      />
    </span>
  )
}

import { useEffect, useState } from 'react'
import type { AvatarPose } from '@dotcraft/avatar'
import { prefersReducedMotion } from '../../../desktopPet/desktopPetSource'

const SUSTAINED: AvatarPose[] = ['thinking', 'working', 'waiting', 'blocked']
const ONE_SHOT: AvatarPose[] = ['greeting', 'acknowledge', 'done']
const ONE_SHOT_MS = 1800

export function usePetPose(): { pose: AvatarPose; eventSequence: number; random: () => void } {
  const [pose, setPose] = useState<AvatarPose>('idle')
  const [eventSequence, setEventSequence] = useState(0)
  useEffect(() => {
    if (!ONE_SHOT.includes(pose)) return
    const timer = window.setTimeout(() => setPose('idle'), ONE_SHOT_MS)
    return () => window.clearTimeout(timer)
  }, [pose, eventSequence])
  const random = (): void => {
    const pool = (prefersReducedMotion() ? SUSTAINED : [...SUSTAINED, ...ONE_SHOT]).filter((next) => next !== pose)
    setPose(pool[Math.floor(Math.random() * pool.length)])
    setEventSequence((value) => value + 1)
  }
  return { pose, eventSequence, random }
}

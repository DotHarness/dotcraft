import { useLayoutEffect, useRef, useState } from 'react'
import { deriveAppearance, mascotPaletteOf } from '../index.js'
import { mascotNameKey, MASCOT_PROFILE_TRANSITION_SWAP_MS, MASCOT_PROFILE_TRANSITION_DURATION_MS, type MascotProfileTransition } from './constants.js'
export function useComposerProfile(mascotName: string | undefined, reduced: boolean) {
  const [renderedMascotAvatar, setRenderedMascotAvatar] = useState(mascotName)
  const [transitionState, setTransitionState] = useState<{ revision: number; transition: MascotProfileTransition | null }>({ revision: 0, transition: null })
  const renderedMascotAvatarRef = useRef(renderedMascotAvatar)
  const targetMascotAvatarRef = useRef(mascotName)
  renderedMascotAvatarRef.current = renderedMascotAvatar
  targetMascotAvatarRef.current = mascotName
  const targetAvatarKey = mascotNameKey(mascotName)
  useLayoutEffect(() => {
    const currentAvatar = renderedMascotAvatarRef.current
    if (targetAvatarKey === mascotNameKey(currentAvatar)) {
      setTransitionState(current => ({ ...current, transition: null }))
      return undefined
    }

    const nextAvatar = targetMascotAvatarRef.current
    if (reduced) {
      renderedMascotAvatarRef.current = nextAvatar
      setRenderedMascotAvatar(nextAvatar)
      setTransitionState(current => ({ ...current, transition: null }))
      return undefined
    }

    setTransitionState(current => ({
      revision: current.revision + 1,
      transition: {
        fromAccent: mascotPaletteOf(deriveAppearance(currentAvatar ?? '')).accent,
        toAccent: mascotPaletteOf(deriveAppearance(nextAvatar ?? '')).accent
      }
    }))

    const swapTimer = window.setTimeout(() => {
      renderedMascotAvatarRef.current = nextAvatar
      setRenderedMascotAvatar(nextAvatar)
    }, MASCOT_PROFILE_TRANSITION_SWAP_MS)
    const finishTimer = window.setTimeout(() => {
      setTransitionState(current => ({ ...current, transition: null }))
    }, MASCOT_PROFILE_TRANSITION_DURATION_MS)

    return () => {
      window.clearTimeout(swapTimer)
      window.clearTimeout(finishTimer)
    }
  }, [targetAvatarKey, reduced])

  return { avatar: renderedMascotAvatar, profileTransition: transitionState.transition, profileTransitionRevision: transitionState.revision }
}

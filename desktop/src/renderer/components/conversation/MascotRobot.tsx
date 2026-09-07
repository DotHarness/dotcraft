import { Avatar } from '@dotcraft/avatar/react'
import type { AvatarPose } from '@dotcraft/avatar'
import type { JSX } from 'react'
import type { ComposerAvatarGesture } from './useComposerAvatarBehavior'

export type MascotExpression = 'neutral' | 'happy' | 'operator' | 'sleep'
export type MascotLight = 'default' | 'error' | 'success'

interface MascotRobotProps {
  state: AvatarPose
  size?: number
  className?: string
  name?: string
  eventSequence?: number
  expression?: 'base' | 'happy' | 'operator' | 'sleep'
  gesture?: ComposerAvatarGesture
  gestureSequence?: number
  onGestureComplete?: (sequence: number) => void
}

/** Composer compatibility wrapper backed by the shared name-derived avatar. */
export function MascotRobot({ state, size = 96, className, name = '', eventSequence = 0, expression, gesture, gestureSequence, onGestureComplete }: MascotRobotProps): JSX.Element {
  return <Avatar name={name} size={size} state={state} eventSequence={eventSequence} expression={expression} gesture={gesture} gestureSequence={gestureSequence} onGestureComplete={onGestureComplete} motion="system" className={className} />
}

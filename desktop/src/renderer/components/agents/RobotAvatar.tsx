import { Avatar } from '@dotcraft/avatar/react'
import type { JSX } from 'react'

interface RobotAvatarProps {
  name: string
  size?: number
  animated?: boolean
}

/** Compatibility wrapper for Desktop's compact agent identity surfaces. */
export function RobotAvatar({ name, size = 40, animated = false }: RobotAvatarProps): JSX.Element {
  return <Avatar name={name} size={size} motion={animated ? 'system' : 'off'} />
}

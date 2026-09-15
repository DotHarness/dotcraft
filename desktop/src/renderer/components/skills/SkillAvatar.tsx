import type { JSX } from 'react'
import { IdentityMark, type IdentityMarkRole } from '../ui/IdentityMark'
import { IdentityMarkFallback } from '../ui/IdentityMarkFallback'

interface SkillAvatarProps {
  size?: number
  iconDataUrl?: string | null
  role?: IdentityMarkRole
  framed?: boolean
}

export function SkillAvatar({
  size = 40,
  iconDataUrl,
  role = 'list',
  framed,
}: SkillAvatarProps): JSX.Element {
  return (
    <IdentityMark
      role={role}
      size={size}
      src={iconDataUrl}
      fallback={<IdentityMarkFallback kind="skill" />}
      framed={framed}
    />
  )
}

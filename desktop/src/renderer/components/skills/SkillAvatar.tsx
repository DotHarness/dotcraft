import type { CSSProperties, JSX } from 'react'
import { IdentityMark } from '../ui/IdentityMark'

interface SkillAvatarProps {
  name: string
  displayName?: string
  size?: number
  iconDataUrl?: string | null
}

/**
 * Keeps a subtle per-skill hue hint while letting theme surface tokens drive contrast
 * in both light and dark modes.
 */
export function SkillAvatar({ name, displayName, size = 40, iconDataUrl }: SkillAvatarProps): JSX.Element {
  const letter = getSkillLetter(displayName ?? name)
  const hue = hashHue(name)
  const accent = `hsl(${hue} 58% 52%)`
  const accentStrong = `hsl(${hue} 52% 40%)`

  return (
    <IdentityMark
      role="list"
      size={size}
      src={iconDataUrl}
      fallback={letter}
      framed={!iconDataUrl}
      style={{
        '--identity-mark-fallback-background': `color-mix(in srgb, var(--bg-tertiary) 68%, ${accent} 32%)`,
        '--identity-mark-border': `color-mix(in srgb, var(--border-default) 58%, ${accent} 42%)`,
        '--identity-mark-fallback-color': `color-mix(in srgb, var(--text-primary) 72%, ${accentStrong} 28%)`,
      } as CSSProperties}
    />
  )
}

function getSkillLetter(name: string): string {
  return (name.trim()[0] ?? '?').toUpperCase()
}

function hashHue(s: string): number {
  let h = 0
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0
  return h % 360
}

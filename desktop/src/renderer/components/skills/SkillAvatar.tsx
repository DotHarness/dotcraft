import type { CSSProperties, JSX } from 'react'

interface SkillAvatarProps {
  name: string
  displayName?: string
  size?: number
  iconDataUrl?: string | null
}

/**
 * Theme-adaptive fallback avatar for a skill.
 *
 * Keeps a subtle per-skill hue hint while letting theme surface tokens drive
 * contrast and overall weight in both light and dark modes.
 */
export function SkillAvatar({ name, displayName, size = 40, iconDataUrl }: SkillAvatarProps): JSX.Element {
  if (iconDataUrl) {
    return (
      <img
        src={iconDataUrl}
        alt=""
        width={size}
        height={size}
        style={{
          width: `${size}px`,
          height: `${size}px`,
          minWidth: `${size}px`,
          minHeight: `${size}px`,
          borderRadius: `${Math.max(8, Math.round(size * 0.22))}px`,
          objectFit: 'cover',
          flexShrink: 0,
          display: 'block'
        }}
      />
    )
  }

  const letter = getSkillLetter(displayName ?? name)
  const hue = hashHue(name)
  const accent = `hsl(${hue} 58% 52%)`
  const accentStrong = `hsl(${hue} 52% 40%)`
  const radius = Math.max(8, Math.round(size * 0.22))
  const fontSize = Math.max(15, Math.round(size * 0.4))

  const style: CSSProperties = {
    width: `${size}px`,
    height: `${size}px`,
    minWidth: `${size}px`,
    minHeight: `${size}px`,
    borderRadius: `${radius}px`,
    background: 'var(--bg-tertiary)',
    border: '1px solid var(--border-default)',
    backgroundColor: 'var(--bg-tertiary)',
    color: 'var(--text-primary)',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontSize: `${fontSize}px`,
    fontWeight: 700,
    lineHeight: 1,
    boxSizing: 'border-box',
    flexShrink: 0,
    userSelect: 'none'
  }

  style.background = `color-mix(in srgb, var(--bg-tertiary) 68%, ${accent} 32%)`
  style.border = `1px solid color-mix(in srgb, var(--border-default) 58%, ${accent} 42%)`
  style.color = `color-mix(in srgb, var(--text-primary) 72%, ${accentStrong} 28%)`

  return (
    <div aria-hidden style={style}>
      {letter}
    </div>
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

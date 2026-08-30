import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { useAppearance } from './useAppearance'

export function MascotStatusRing({
  context
}: DesktopPluginSurfaceProps<'composer.mascot'>): JSX.Element | null {
  const appearance = useAppearance()
  if (!appearance?.statusRing || context.light === 'default') return null

  return (
    <svg
      className="grok-status-ring"
      data-light={context.light}
      data-reduced-motion={context.reducedMotion ? 'true' : 'false'}
      width={context.size}
      height={context.size}
      viewBox="0 0 100 100"
      fill="none"
      aria-hidden="true"
    >
      <circle cx="50" cy="50" r="47" stroke="currentColor" strokeWidth="2.5" />
    </svg>
  )
}

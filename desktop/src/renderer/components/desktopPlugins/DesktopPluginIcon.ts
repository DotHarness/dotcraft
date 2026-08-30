import type { DesktopPluginContributionIcon, DesktopPluginIconComponent } from '@dotcraft/plugin'
import { UsersRound } from 'lucide-react'

const FALLBACK_GLYPH: DesktopPluginIconComponent = UsersRound

/** Uses a plugin-owned component when supplied, otherwise a Host glyph so no row goes iconless. */
export function resolveDesktopPluginIcon(icon?: DesktopPluginContributionIcon | null): DesktopPluginIconComponent {
  return icon ?? FALLBACK_GLYPH
}

import type {
  DesktopPluginContributionIcon,
  DesktopPluginIconComponent
} from '@dotcraft/plugin'
import { Bot, SquareKanban, UsersRound } from 'lucide-react'

/** Uses a plugin-owned component when supplied, otherwise resolves a Host-owned glyph token. */
export function resolveDesktopPluginIcon(icon?: DesktopPluginContributionIcon | null): DesktopPluginIconComponent {
  if (icon && typeof icon !== 'string') return icon
  switch (icon) {
    case 'board':
    case 'kanban':
      return SquareKanban
    case 'bot':
    case 'agent':
      return Bot
    default:
      return UsersRound
  }
}

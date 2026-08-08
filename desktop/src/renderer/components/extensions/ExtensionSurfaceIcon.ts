import { Bot, SquareKanban, UsersRound, type LucideIcon } from 'lucide-react'

import { OratorioBatonIcon } from '../oratorio/OratorioBatonIcon'

/** Resolves the optional icon declared by a desktop extension surface. */
export function resolveExtensionSurfaceIcon(icon?: string | null): LucideIcon {
  switch (icon) {
    case 'oratorio-baton':
      return OratorioBatonIcon
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

import { AppearanceAvatar } from '@dotcraft/avatar/react'
import type { PersonalMascotProps } from '@dotcraft/plugin'
import { usePetStore } from '../../pet/petStore'

export function DesktopPluginPersonalMascot({ size = 96, greeting = false }: PersonalMascotProps): JSX.Element {
  const appearance = usePetStore((state) => state.appearance)
  return <AppearanceAvatar appearance={appearance} size={size} state={greeting ? 'greeting' : 'idle'} motion="system" />
}

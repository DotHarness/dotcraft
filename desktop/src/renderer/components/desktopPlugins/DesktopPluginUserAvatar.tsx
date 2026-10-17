import type { UserAvatarProps } from '@dotcraft/plugin'

import { CurrentUserAvatar } from '../ui/UserAvatar'

export function DesktopPluginUserAvatar({ size = 24 }: UserAvatarProps): JSX.Element {
  return <CurrentUserAvatar size={size} />
}

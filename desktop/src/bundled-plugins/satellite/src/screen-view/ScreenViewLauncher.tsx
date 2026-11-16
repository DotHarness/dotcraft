import type { RefObject } from 'react'
import { Monitor } from 'lucide-react'
import { useT } from '../i18n'
import { SCREEN_VIEW_CAPABILITY } from './screenViewTypes'
import { IconButton } from '@dotcraft/plugin'

export interface ScreenViewLauncherProps {
  hostId: string | null
  hostName: string
  online: boolean
  capabilities: readonly string[]
  open: boolean
  live: boolean
  anchorRef?: RefObject<HTMLSpanElement | null>
  onToggle(): void
}

export function canWatchHost(props: Pick<ScreenViewLauncherProps, 'hostId' | 'online' | 'capabilities'>): boolean {
  if (!props.hostId || !props.online) return false
  return props.capabilities.includes(SCREEN_VIEW_CAPABILITY)
}

export function ScreenViewLauncher(props: ScreenViewLauncherProps): JSX.Element | null {
  const t = useT()
  if (!props.open && !canWatchHost(props)) return null

  const label = t('screenView.launcher', { name: props.hostName })
  return (
    <span ref={props.anchorRef}>
      <IconButton
        size={28}
        label={label}
        tooltipLabel={label}
        tooltipPlacement="bottom"
        active={props.open && props.live}
        aria-pressed={props.open}
        onClick={props.onToggle}
        icon={<Monitor size={16} aria-hidden />}
      />
    </span>
  )
}

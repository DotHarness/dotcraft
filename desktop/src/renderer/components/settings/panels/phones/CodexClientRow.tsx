import { useRef, type JSX } from 'react'
import { Monitor, Smartphone } from 'lucide-react'

import { SettingsRow } from '../../SettingsGroup'
import { MoreActionsButton } from '../../../ui/MoreActionsButton'
import { useT } from '../../../../contexts/LocaleContext'
import { lastSeenLabel } from '../../../../../shared/satellites'
import type { CodexRemoteClient } from '../../../../../shared/codexRemote'
import * as s from '../connections/connectionsStyles'

const PLATFORM_NAMES: Record<string, string> = { ios: 'iOS', ipados: 'iPadOS', android: 'Android' }
const MOBILE = new Set(['phone', 'tablet', 'ios', 'ipados', 'android'])

export function codexClientName(client: CodexRemoteClient): string | null {
  return client.displayName || client.deviceModel || client.platform || null
}

function platformText(client: CodexRemoteClient): string | null {
  if (!client.platform) return null
  const name = PLATFORM_NAMES[client.platform.toLowerCase()] ?? client.platform
  return client.osVersion ? `${name} ${client.osVersion}` : name
}

export function CodexClientRow({
  client,
  removing,
  menuOpen,
  onMenu
}: {
  client: CodexRemoteClient
  removing: boolean
  menuOpen: boolean
  onMenu: (anchor: HTMLElement) => void
}): JSX.Element {
  const t = useT()
  const menuButton = useRef<HTMLButtonElement>(null)
  const name = codexClientName(client) ?? t('settings.codexApp.devices.unknown')
  const seen = client.lastSeenAt ? lastSeenLabel(client.lastSeenAt) : null
  const meta = [
    platformText(client),
    seen ? t('settings.codexApp.devices.lastSeen', { time: t(seen.key, seen.params) }) : t('settings.codexApp.devices.authorized')
  ].filter(Boolean).join(' · ')
  const mobile = [client.deviceType, client.platform].some((value) => value && MOBILE.has(value.toLowerCase()))

  return (
    <SettingsRow>
      <span className="dc-codex-client">
        <span style={s.listRowIcon} aria-hidden>
          {mobile ? <Smartphone size={17} /> : <Monitor size={17} />}
        </span>
        <span className="dc-satellite-row__text">
          <span className="dc-satellite-row__title">
            <span className="dc-satellite-row__name">{name}</span>
          </span>
          <span className="dc-satellite-row__meta">{meta}</span>
        </span>
        <MoreActionsButton
          ref={menuButton}
          label={t('settings.phones.row.moreActions', { name })}
          open={menuOpen}
          disabled={removing}
          onClick={() => menuButton.current && onMenu(menuButton.current)}
        />
      </span>
    </SettingsRow>
  )
}

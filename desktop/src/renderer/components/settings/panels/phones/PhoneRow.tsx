import { useRef, type JSX } from 'react'
import { Smartphone } from 'lucide-react'

import { MoreActionsButton } from '../../../ui/MoreActionsButton'
import { useLocale, useT } from '../../../../contexts/LocaleContext'
import { lastSeenLabel } from '../../../../../shared/satellites'
import type { MobileDevice } from '../../../../../shared/mobile'
import { StatusIndicator, statusTextStyle } from '../settingsStatusStyles'
import * as s from '../connections/connectionsStyles'
import { addedDay, platformLabel } from './phonesFormat'

export function PhoneRow({
  device,
  first,
  gatewayOn,
  menuOpen,
  onMenu
}: {
  device: MobileDevice
  first: boolean
  gatewayOn: boolean
  menuOpen: boolean
  onMenu: (anchor: HTMLElement) => void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const menuButton = useRef<HTMLButtonElement>(null)
  const live = gatewayOn && device.connected
  const seen = lastSeenLabel(device.lastSeenAt ?? device.pairedAt)

  return (
    <div style={{ ...s.listRow, cursor: 'default', borderTop: first ? 'none' : '1px solid var(--border-default)' }}>
      <span style={s.listRowIcon} aria-hidden>
        <Smartphone size={17} />
      </span>
      <span className="dc-satellite-row__text">
        <span className="dc-satellite-row__title">
          <span className="dc-satellite-row__name">{device.displayName}</span>
          <span style={statusTextStyle()}>
            <StatusIndicator tone={live ? 'success' : 'neutral'} />
            {live
              ? t('settings.phones.row.connected')
              : t('settings.phones.row.lastSeen', { time: t(seen.key, seen.params) })}
          </span>
        </span>
        <span className="dc-satellite-row__meta">
          {platformLabel(device)} · {t('settings.phones.row.added', { date: addedDay(device.pairedAt, locale) })}
        </span>
      </span>
      <MoreActionsButton
        ref={menuButton}
        label={t('settings.phones.row.moreActions', { name: device.displayName })}
        open={menuOpen}
        onClick={() => menuButton.current && onMenu(menuButton.current)}
      />
    </div>
  )
}

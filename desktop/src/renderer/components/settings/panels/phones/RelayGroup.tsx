import { useState, type JSX } from 'react'
import { Globe } from 'lucide-react'

import { SettingsGroup } from '../../SettingsGroup'
import { ActionTooltip } from '../../../ui/ActionTooltip'
import { Button } from '../../../ui/Button'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { useT } from '../../../../contexts/LocaleContext'
import { useMobileStore } from '../../../../stores/mobileStore'
import { addToast } from '../../../../stores/toastStore'
import type { MobileGateway } from '../../../../../shared/mobile'
import type { MessageKey } from '../../../../../shared/locales'
import { StatusIndicator, statusTextStyle, type StatusIndicatorTone } from '../settingsStatusStyles'
import * as s from '../connections/connectionsStyles'
import { RelayDialog } from './RelayDialog'

type RelayState = NonNullable<MobileGateway['relay']>['state']

const RELAY_STATUS: Record<RelayState, { tone: StatusIndicatorTone; key: MessageKey }> = {
  connecting: { tone: 'pending', key: 'settings.phones.relay.status.connecting' },
  connected: { tone: 'success', key: 'settings.phones.relay.status.connected' },
  failed: { tone: 'error', key: 'settings.phones.relay.status.failed' }
}

const PAUSED = { tone: 'neutral', key: 'settings.phones.relay.status.paused' } as const

export function RelayGroup({ relay, on }: { relay: MobileGateway['relay']; on: boolean }): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [editing, setEditing] = useState(false)
  const disabledReason = on
    ? undefined
    : t(relay ? 'settings.phones.relay.requiresAccess' : 'settings.phones.relay.setUp.requiresAccess')

  async function remove(): Promise<void> {
    const confirmed = await confirm({
      title: t('settings.phones.relay.remove.title'),
      message: t('settings.phones.relay.remove.message'),
      confirmLabel: t('settings.phones.remove'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!confirmed) return
    const failure = await useMobileStore.getState().clearRelay()
    if (failure) addToast(t('settings.phones.relay.remove.failed'), 'error')
  }

  function action(label: string, onClick: () => void, variant: 'secondary' | 'danger' = 'secondary'): JSX.Element {
    return (
      <ActionTooltip label="" disabledReason={disabledReason}>
        <Button variant={variant} disabled={!on} onClick={onClick}>
          {label}
        </Button>
      </ActionTooltip>
    )
  }

  const status = on && relay ? RELAY_STATUS[relay.state] : PAUSED

  return (
    <SettingsGroup
      title={t('settings.phones.relay.title')}
      description={t('settings.phones.relay.description')}
      headerAction={relay ? undefined : action(t('settings.phones.relay.setUp'), () => setEditing(true))}
      framed={Boolean(relay)}
    >
      {relay && (
        <div style={{ ...s.listRow, cursor: 'default' }}>
          <span style={s.listRowIcon} aria-hidden>
            <Globe size={17} />
          </span>
          <span className="dc-satellite-row__text">
            <span className="dc-satellite-row__title">
              <span className="dc-satellite-row__name">{relay.url}</span>
            </span>
            <span className="dc-satellite-row__meta">
              <span style={statusTextStyle()}>
                <StatusIndicator tone={status.tone} />
                {t(status.key)}
              </span>
            </span>
          </span>
          <span className="dc-phones-relay__actions">
            {action(t('settings.phones.relay.change'), () => setEditing(true))}
            {action(t('settings.phones.remove'), () => void remove(), 'danger')}
          </span>
        </div>
      )}
      {editing && <RelayDialog initialUrl={relay?.url ?? ''} onClose={() => setEditing(false)} />}
    </SettingsGroup>
  )
}

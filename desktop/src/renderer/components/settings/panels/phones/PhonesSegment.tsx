import { useCallback, useEffect, useState, type JSX, type ReactNode } from 'react'
import { AlertTriangle, Monitor, Plus, Smartphone } from 'lucide-react'

import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { ActionTooltip } from '../../../ui/ActionTooltip'
import { Button } from '../../../ui/Button'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { ContextMenu, type ContextMenuPosition } from '../../../ui/ContextMenu'
import { PillSwitch } from '../../../ui/PillSwitch'
import { SkeletonRow } from '../../../ui/Skeleton'
import { useT } from '../../../../contexts/LocaleContext'
import { connectMobile, useMobileStore } from '../../../../stores/mobileStore'
import { addToast } from '../../../../stores/toastStore'
import type { MobileDevice } from '../../../../../shared/mobile'
import { SegmentSetupState } from '../connections/SegmentSetupState'
import * as s from '../connections/connectionsStyles'
import { AddPhoneDialog } from './AddPhoneDialog'
import { PhoneRow } from './PhoneRow'
import { RelayGroup } from './RelayGroup'
import { failureReason } from './phonesFormat'

function PhonesNotice({ title, reason, action }: { title: string; reason: string; action?: ReactNode }): JSX.Element {
  return (
    <div style={s.banner} role="alert">
      <span className="dc-phones-banner__glyph" aria-hidden>
        <AlertTriangle size={20} />
      </span>
      <div className="dc-phones-banner__text">
        <strong>{title}</strong>
        <span>{reason}</span>
      </div>
      {action && <div className="dc-phones-banner__action">{action}</div>}
    </div>
  )
}

export function PhonesSegment(): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const supported = useMobileStore((state) => state.supported)
  const loaded = useMobileStore((state) => state.loaded)
  const error = useMobileStore((state) => state.error)
  const gateway = useMobileStore((state) => state.gateway)
  const devices = useMobileStore((state) => state.devices)
  const pendingEnabled = useMobileStore((state) => state.pendingEnabled)
  const [adding, setAdding] = useState(false)
  const [menu, setMenu] = useState<{ id: string; position: ContextMenuPosition } | null>(null)

  const closeAdding = useCallback(() => setAdding(false), [])

  useEffect(() => connectMobile(), [])
  useEffect(() => () => useMobileStore.getState().closePairing(), [])

  if (loaded && !supported) {
    return (
      <SegmentSetupState
        icon={<Smartphone size={22} />}
        title={t('settings.phones.setup.title')}
        description={t('settings.phones.setup.description')}
      />
    )
  }

  if (!loaded) {
    return (
      <div role="status" aria-busy="true" aria-label={t('settings.phones.loading')}>
        <SettingsGroup>
          <SettingsRow>
            <SkeletonRow lines={['46%', '64%']} style={{ flex: 1 }} />
          </SettingsRow>
        </SettingsGroup>
      </div>
    )
  }

  const on = gateway?.state === 'on'
  const switching = pendingEnabled !== null
  const menuDevice = menu ? devices.find((device) => device.deviceId === menu.id) : undefined

  async function toggle(next: boolean): Promise<void> {
    const failure = await useMobileStore.getState().setEnabled(next)
    if (failure && !next) addToast(t('settings.phones.disableFailed'), 'error')
  }

  async function remove(device: MobileDevice): Promise<void> {
    const confirmed = await confirm({
      title: t('settings.phones.remove.title', { name: device.displayName }),
      message: t('settings.phones.remove.message', { name: device.displayName }),
      confirmLabel: t('settings.phones.remove'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!confirmed) return
    const failure = await useMobileStore.getState().revoke(device.deviceId)
    if (failure) addToast(t('settings.phones.remove.failed', { name: device.displayName }), 'error')
  }

  const addButton = (
    <ActionTooltip label="" disabledReason={on ? undefined : t('settings.phones.add.requiresAccess')}>
      <Button variant="primary" iconLeft={<Plus size={15} aria-hidden />} disabled={!on} onClick={() => setAdding(true)}>
        {t('settings.phones.add')}
      </Button>
    </ActionTooltip>
  )

  return (
    <div className="dc-phones-page">
      {error != null && (
        <PhonesNotice
          title={t('settings.phones.loadFailed')}
          reason={error || t('settings.phones.hubNoAnswer')}
          action={
            <Button variant="secondary" onClick={() => void useMobileStore.getState().load()}>
              {t('settings.phones.retry')}
            </Button>
          }
        />
      )}

      {gateway?.state === 'failed' && (
        <PhonesNotice
          title={t('settings.phones.failed.title')}
          reason={failureReason(
            t,
            { code: gateway.failureCode, message: gateway.failureMessage },
            'settings.phones.failed.unknown',
            gateway.port
          )}
        />
      )}

      {gateway && (
        <SettingsGroup>
          <SettingsRow
            label={t('settings.phones.access.label')}
            description={t('settings.phones.access.description')}
            control={
              <PillSwitch
                checked={pendingEnabled ?? on}
                disabled={switching}
                aria-busy={switching}
                aria-label={t('settings.phones.access.label')}
                onChange={(next) => void toggle(next)}
              />
            }
          />
        </SettingsGroup>
      )}

      {on && devices.length === 0 && (
        <SettingsGroup title={t('settings.phones.group.title')} framed={false}>
          <div style={s.emptyBox}>
            <span className="dc-phones-empty__glyph" aria-hidden>
              <Monitor size={18} />
              <span>···</span>
              <Smartphone size={18} />
            </span>
            <p className="dc-phones-empty__copy">{t('settings.phones.empty')}</p>
            <div className="dc-phones-empty__action">{addButton}</div>
          </div>
        </SettingsGroup>
      )}

      {devices.length > 0 && (
        <SettingsGroup title={t('settings.phones.group.title')} headerAction={addButton}>
          {devices.map((device, index) => (
            <PhoneRow
              key={device.deviceId}
              device={device}
              first={index === 0}
              gatewayOn={on}
              menuOpen={menu?.id === device.deviceId}
              onMenu={(anchor) => {
                const rect = anchor.getBoundingClientRect()
                setMenu({ id: device.deviceId, position: { x: rect.right - 200, y: rect.bottom + 4 } })
              }}
            />
          ))}
        </SettingsGroup>
      )}

      {gateway && <RelayGroup relay={gateway.relay} on={on} />}

      {menu && menuDevice && (
        <ContextMenu
          position={menu.position}
          onClose={() => setMenu(null)}
          items={[{ label: t('settings.phones.remove'), danger: true, onClick: () => void remove(menuDevice) }]}
        />
      )}

      {adding && <AddPhoneDialog onClose={closeAdding} />}
    </div>
  )
}

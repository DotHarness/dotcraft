import { useEffect, useState, type JSX } from 'react'
import { ChevronRight, Monitor, Plus } from 'lucide-react'

import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { Button } from '../../../ui/Button'
import { SkeletonRow } from '../../../ui/Skeleton'
import { StatusIndicator } from '../../../ui/StatusIndicator'
import { useT } from '../../../../contexts/LocaleContext'
import { connectPairedComputers, usePairedComputersStore } from '../../../../stores/pairedComputersStore'
import * as s from '../connections/connectionsStyles'
import { PhonesNotice } from '../phones/PhonesSegment'
import { statusTextStyle } from '../settingsStatusStyles'
import { AddOtherDeviceDialog } from './AddOtherDeviceDialog'
import { PairedComputerDetail } from './PairedComputerDetail'
import { STATUS_LABEL, STATUS_TONE, statusMessage } from './pairedComputerPresentation'

export function OtherDevicesSegment({
  onSubPageChange
}: {
  onSubPageChange: (open: boolean) => void
}): JSX.Element {
  const t = useT()
  const loaded = usePairedComputersStore((state) => state.loaded)
  const error = usePairedComputersStore((state) => state.error)
  const computers = usePairedComputersStore((state) => state.computers)
  const pendingLink = usePairedComputersStore((state) => state.pendingLink)
  const [adding, setAdding] = useState<{ link: string } | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)

  useEffect(() => connectPairedComputers(), [])

  useEffect(() => {
    if (!pendingLink) return
    const link = usePairedComputersStore.getState().takePendingLink()
    if (!link) return
    setOpenId(null)
    setAdding({ link })
  }, [pendingLink])

  const openComputer = openId ? computers.find((computer) => computer.computerId === openId) : undefined

  useEffect(() => {
    onSubPageChange(openComputer != null)
  }, [onSubPageChange, openComputer])

  if (openComputer) {
    return <PairedComputerDetail computer={openComputer} onBack={() => setOpenId(null)} />
  }

  if (!loaded) {
    return (
      <div role="status" aria-busy="true" aria-label={t('settings.otherDevices.loading')}>
        <SettingsGroup>
          <SettingsRow>
            <SkeletonRow lines={['46%', '64%']} style={{ flex: 1 }} />
          </SettingsRow>
        </SettingsGroup>
      </div>
    )
  }

  const addButton = (
    <Button variant="primary" iconLeft={<Plus size={15} aria-hidden />} onClick={() => setAdding({ link: '' })}>
      {t('settings.otherDevices.add')}
    </Button>
  )

  return (
    <div className="dc-phones-page">
      {error != null && (
        <PhonesNotice
          title={t('settings.otherDevices.loadFailed')}
          reason={error}
          action={
            <Button variant="secondary" onClick={() => void usePairedComputersStore.getState().load()}>
              {t('settings.phones.retry')}
            </Button>
          }
        />
      )}

      {computers.length === 0 ? (
        <SettingsGroup title={t('settings.otherDevices.group.title')} framed={false}>
          <div style={s.emptyBox}>
            <span className="dc-phones-empty__glyph" aria-hidden>
              <Monitor size={18} />
              <span>···</span>
              <Monitor size={18} />
            </span>
            <p className="dc-phones-empty__copy">{t('settings.otherDevices.empty')}</p>
            <div className="dc-phones-empty__action">{addButton}</div>
          </div>
        </SettingsGroup>
      ) : (
        <SettingsGroup title={t('settings.otherDevices.group.title')} headerAction={addButton}>
          {computers.map((computer, index) => {
            const message = statusMessage(t, computer)
            return (
              <button
                key={computer.computerId}
                type="button"
                className="dc-satellite-row"
                style={{ ...s.listRow, borderTop: index === 0 ? 'none' : '1px solid var(--border-default)' }}
                onClick={() => setOpenId(computer.computerId)}
              >
                <span style={s.listRowIcon} aria-hidden>
                  <Monitor size={17} />
                </span>
                <span className="dc-satellite-row__text">
                  <span className="dc-satellite-row__title">
                    <span className="dc-satellite-row__name">{computer.name}</span>
                    <span style={statusTextStyle()}>
                      <StatusIndicator tone={STATUS_TONE[computer.status]} />
                      {t(STATUS_LABEL[computer.status])}
                    </span>
                  </span>
                  {message && <span className="dc-satellite-row__meta">{message}</span>}
                </span>
                <span className="dc-satellite-row__chevron" aria-hidden>
                  <ChevronRight size={18} />
                </span>
              </button>
            )
          })}
        </SettingsGroup>
      )}

      {adding && (
        <AddOtherDeviceDialog
          initialLink={adding.link}
          onClose={() => setAdding(null)}
          onOpenComputer={(computerId) => {
            setAdding(null)
            setOpenId(computerId)
          }}
        />
      )}
    </div>
  )
}

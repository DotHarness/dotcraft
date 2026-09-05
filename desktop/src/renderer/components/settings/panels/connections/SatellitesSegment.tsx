import { useEffect, useMemo, useState, type JSX } from 'react'
import { AlertTriangle, Plus, RefreshCw, SatelliteDish } from 'lucide-react'

import { SettingsGroup } from '../../SettingsGroup'
import { Button } from '../../../ui/Button'
import { IconButton } from '../../../ui/IconButton'
import { useT } from '../../../../contexts/LocaleContext'
import { useConnectionStore } from '../../../../stores/connectionStore'
import { bootstrapSatellites, useSatellitesStore, withLeases } from '../../../../stores/satellitesStore'
import { useThreadRouteStore } from '../../../../stores/threadRouteStore'
import { SatelliteDetail } from '../satellites/SatelliteDetail'
import { SatelliteInviteDialog } from '../satellites/SatelliteInviteDialog'
import { SatelliteList } from '../satellites/SatelliteList'
import { SegmentSetupState } from './SegmentSetupState'
import * as s from '../servers/serversStyles'

interface SatellitesSegmentProps {
  /** Reports whether a detail page is open, so the shell can yield the surface. */
  onSubPageChange?: (open: boolean) => void
}

/** Presence comes from Hub; the lease overlay rides the route store's host listing. */
function readLeases(): Promise<void> {
  return useThreadRouteStore.getState().list().catch(() => undefined)
}

export function SatellitesSegment({ onSubPageChange }: SatellitesSegmentProps = {}): JSX.Element {
  const t = useT()
  const store = useSatellitesStore()
  const [inviting, setInviting] = useState(false)
  const leasesReadable = useConnectionStore(
    (state) => state.status === 'connected' && state.capabilities?.remoteToolHost === true
  )

  useEffect(() => bootstrapSatellites(), [])

  useEffect(() => {
    if (leasesReadable) void readLeases()
  }, [leasesReadable])

  const satellites = useMemo(
    () => withLeases(store.satellites, store.busy),
    [store.satellites, store.busy]
  )
  const selected = satellites.find((satellite) => satellite.peerId === store.selectedPeerId) ?? null
  const subPageOpen = selected != null

  useEffect(() => {
    onSubPageChange?.(subPageOpen)
  }, [onSubPageChange, subPageOpen])

  function refresh(): void {
    void store.load()
    if (leasesReadable) void readLeases()
  }

  if (selected) {
    return (
      <SatelliteDetail
        satellite={selected}
        onBack={() => store.select(null)}
        onRefresh={refresh}
      />
    )
  }

  if (store.loaded && !store.supported) {
    return (
      <SegmentSetupState
        icon={<SatelliteDish size={22} />}
        title={t('settings.satellites.empty.title')}
        description={t('settings.satellites.empty.description')}
      />
    )
  }

  const loading = !store.loaded
  const empty = !loading && satellites.length === 0
  // A failed listing says nothing about whether machines exist, so the banner stands
  // alone rather than over a first-run invitation.
  const failed = empty && store.error != null

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {inviting && <SatelliteInviteDialog onClose={() => setInviting(false)} />}

      {store.error != null && (
        <div style={s.banner}>
          <span style={{ color: 'var(--error)', flexShrink: 0, marginTop: 1 }} aria-hidden>
            <AlertTriangle size={20} />
          </span>
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 13, fontWeight: 600 }}>{t('settings.satellites.error.loadFailed')}</div>
            <div style={{ marginTop: 4, color: 'var(--text-secondary)', fontSize: 12 }}>
              {t('settings.satellites.error.reason')}
            </div>
          </div>
        </div>
      )}

      <SettingsGroup
        title={t('settings.satellites.group.title')}
        description={t('settings.satellites.description')}
        framed={!empty}
        headerAction={
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <IconButton
              icon={<RefreshCw size={15} />}
              label={t('settings.connections.refresh')}
              onClick={refresh}
            />
            <Button variant="primary" iconLeft={<Plus size={15} />} onClick={() => setInviting(true)}>
              {t('settings.satellites.invite.short')}
            </Button>
          </div>
        }
      >
        {failed ? null : (
          <SatelliteList
            satellites={satellites}
            loading={loading}
            onOpen={(peerId) => store.select(peerId)}
            onInvite={() => setInviting(true)}
          />
        )}
      </SettingsGroup>
    </div>
  )
}

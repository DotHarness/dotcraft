import { useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Plus, RefreshCw, SatelliteDish } from 'lucide-react'
import { SettingsGroup, SettingsPanelShell, SegmentedControl, Button, IconButton } from '@dotcraft/plugin'
import { useT } from '../i18n'
import { useSatellitesStore, withLeases } from '../state'
import { satelliteHost, useRemoteToolHost } from '../runtime'
import { SatelliteDetail } from './SatelliteDetail'
import { SatelliteInviteDialog } from './SatelliteInviteDialog'
import { SatelliteList } from './SatelliteList'
import { SharePcSegment } from './SharePcSegment'
import { useSharePcStatus } from './useSharePcStatus'
import { SegmentSetupState } from './SetupState'
import './satellites.css'
import './settings-layout.css'

export function SatelliteSettings(): JSX.Element {
  const t = useT()
  const store = useSatellitesStore((state) => state)
  const route = useRemoteToolHost()
  const [tab, setTab] = useState('satellites')
  const [inviting, setInviting] = useState(false)
  const share = useSharePcStatus()
  const showShare = share.status != null && (share.status.installed || share.status.peers.length > 0)
  const satellites = useMemo(() => withLeases(store.satellites, store.busy), [store.satellites, store.busy])
  const selected = satellites.find((satellite) => satellite.peerId === store.selectedPeerId)
  useEffect(() => { if (route.supported && !route.remote) void satelliteHost().remoteToolHost.list().catch(() => {}) }, [route.supported, route.remote])
  function refresh(): void {
    void store.load()
    if (route.supported && !route.remote) void satelliteHost().remoteToolHost.list().catch(() => {})
  }
  if (selected && tab === 'satellites') return <SatelliteDetail satellite={selected} onBack={() => store.select(null)} onRefresh={refresh} />
  const empty = store.loaded && satellites.length === 0
  return <SettingsPanelShell title={t('settings.connections.segments.satellites')}>
    {showShare && <SegmentedControl value={tab} onValueChange={setTab} ariaLabel={t('settings.connections.segments.satellites')} options={[{ value: 'satellites', label: t('settings.connections.segments.satellites') }, { value: 'share', label: t('settings.connections.segments.share') }]} />}
    {tab === 'share' && showShare ? <SharePcSegment status={share.status} onRefresh={share.reload} /> : <>
      {inviting && <SatelliteInviteDialog onClose={() => setInviting(false)} />}
      {store.error && <div className="satellite-banner"><AlertTriangle size={20} /><div><strong>{t('settings.satellites.error.loadFailed')}</strong><p>{t('settings.satellites.error.reason')}</p></div></div>}
      {store.loaded && !store.supported ? <SegmentSetupState icon={<SatelliteDish size={22} />} title={t('settings.satellites.empty.title')} description={t('settings.satellites.empty.description')} /> : <SettingsGroup title={t('settings.satellites.group.title')} description={t('settings.satellites.description')} framed={!empty} headerAction={<div className="satellite-header-actions"><IconButton icon={<RefreshCw size={15} />} label={t('settings.connections.refresh')} onClick={refresh} /><Button variant="primary" iconLeft={<Plus size={15} />} onClick={() => setInviting(true)}>{t('settings.satellites.invite.short')}</Button></div>}>
        {empty && store.error ? null : <SatelliteList satellites={satellites} loading={!store.loaded} onOpen={store.select} onInvite={() => setInviting(true)} />}
      </SettingsGroup>}
    </>}
  </SettingsPanelShell>
}

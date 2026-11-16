import { MonitorSmartphone, RefreshCw } from 'lucide-react'
import { SettingsGroup, IconButton } from '@dotcraft/plugin'
import { useLocale, useT } from '../i18n'
import type { SharePcStatus } from '../types'
import { formatDay } from './format'
import { SegmentSetupState } from './SetupState'

export function SharePcSegment({ status, onRefresh }: { status: SharePcStatus | null; onRefresh: () => void }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const peers = status?.peers ?? []
  return <SettingsGroup title={t('settings.share.group.title')} description={t('settings.share.group.description')} framed={peers.length > 0} headerAction={<IconButton icon={<RefreshCw size={15} />} label={t('settings.connections.refresh')} onClick={onRefresh} />}>
    {peers.length === 0 ? <SegmentSetupState icon={<MonitorSmartphone size={22} />} title={t('settings.share.empty.title')} description={t('settings.share.empty.description')} /> : <>
      {peers.map((peer, index) => {
        const paired = formatDay(peer.pairedAt, locale)
        const meta = [t(peer.authorizationMode === 'fullAccess' ? 'settings.share.mode.full' : peer.authorizationMode === 'workspacePreferred' ? 'settings.share.mode.preferred' : 'settings.share.mode.review'), peer.folderPath ? t('settings.share.pairing.folder', { folder: peer.folderPath }) : null, paired ? t('settings.share.pairing.paired', { date: paired }) : null].filter(Boolean)
        return <div key={peer.peerId} className="satellite-share-row" data-first={index === 0}><span className="satellite-row-icon" aria-hidden><MonitorSmartphone size={17} /></span><span className="dc-share-row__text"><span className="dc-share-row__title">{peer.hubLabel}</span>{meta.length > 0 && <span className="dc-share-row__meta">{meta.join(' · ')}</span>}</span></div>
      })}
      <div className="satellite-share-note-row"><span className="dc-share-note">{t('settings.share.managedInApp')}</span></div>
    </>}
  </SettingsGroup>
}

import type { JSX } from 'react'
import { MonitorSmartphone, RefreshCw } from 'lucide-react'

import { SettingsGroup } from '../../SettingsGroup'
import { IconButton } from '../../../ui/IconButton'
import { useLocale, useT } from '../../../../contexts/LocaleContext'
import type { SharePcStatus } from '../../../../../shared/satellites'
import { formatDay } from '../satellites/satellitesFormat'
import { SegmentSetupState } from './SegmentSetupState'
import * as s from '../servers/serversStyles'

interface SharePcSegmentProps {
  status: SharePcStatus | null
  onRefresh: () => void
}

/** Read-only: Satellite is the only writer of pairings, so this segment says so. */
export function SharePcSegment({ status, onRefresh }: SharePcSegmentProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const peers = status?.peers ?? []

  return (
    <SettingsGroup
      title={t('settings.share.group.title')}
      description={t('settings.share.group.description')}
      framed={peers.length > 0}
      headerAction={
        <IconButton
          icon={<RefreshCw size={15} />}
          label={t('settings.connections.refresh')}
          onClick={onRefresh}
        />
      }
    >
      {peers.length === 0 ? (
        <SegmentSetupState
          icon={<MonitorSmartphone size={22} />}
          title={t('settings.share.empty.title')}
          description={t('settings.share.empty.description')}
        />
      ) : (
        <>
          {peers.map((peer, index) => {
            const paired = formatDay(peer.pairedAt, locale)
            const meta = [
              peer.folderPath ? t('settings.share.pairing.folder', { folder: peer.folderPath }) : null,
              paired ? t('settings.share.pairing.paired', { date: paired }) : null
            ].filter((part): part is string => Boolean(part))
            return (
              <div
                key={peer.peerId}
                style={{
                  ...s.serverRow,
                  cursor: 'default',
                  borderTop: index === 0 ? 'none' : '1px solid var(--border-default)'
                }}
              >
                <span style={s.serverRowIcon} aria-hidden>
                  <MonitorSmartphone size={17} />
                </span>
                <span className="dc-share-row__text">
                  <span className="dc-share-row__title">{peer.hubLabel}</span>
                  {meta.length > 0 && <span className="dc-share-row__meta">{meta.join(' · ')}</span>}
                </span>
              </div>
            )
          })}
          <div
            style={{
              ...s.serverRow,
              cursor: 'default',
              padding: '11px 14px',
              borderTop: '1px solid var(--border-default)'
            }}
          >
            <span className="dc-share-note">{t('settings.share.managedInApp')}</span>
          </div>
        </>
      )}
    </SettingsGroup>
  )
}

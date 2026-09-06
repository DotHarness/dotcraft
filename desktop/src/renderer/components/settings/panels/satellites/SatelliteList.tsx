import type { JSX } from 'react'
import { ChevronRight, Monitor, Plus, SatelliteDish } from 'lucide-react'

import { Button } from '../../../ui/Button'
import { SkeletonRow } from '../../../ui/Skeleton'
import { useT } from '../../../../contexts/LocaleContext'
import { lastSeenLabel, satelliteState, type Satellite } from '../../../../../shared/satellites'
import { SegmentSetupState } from '../connections/SegmentSetupState'
import { SATELLITE_STATE_KEY, SatelliteStatusText } from './satellitesStatus'
import * as s from '../servers/serversStyles'

interface SatelliteListProps {
  satellites: Satellite[]
  loading: boolean
  onOpen: (peerId: string) => void
  onInvite: () => void
}

export function SatelliteList({ satellites, loading, onOpen, onInvite }: SatelliteListProps): JSX.Element {
  const t = useT()

  if (loading) {
    return (
      <div role="status" aria-label={t('settings.satellites.list.loading')}>
        {[0, 1, 2].map((index) => (
          <div
            key={index}
            className="dc-satellite-row dc-satellite-row--skeleton"
            style={{ ...s.serverRow, borderTop: index === 0 ? 'none' : '1px solid var(--border-default)' }}
          >
            <SkeletonRow media={34} mediaRadius={8} lines={['46%', '30%']} style={{ flex: 1 }} />
          </div>
        ))}
      </div>
    )
  }

  if (satellites.length === 0) {
    return (
      <SegmentSetupState
        icon={<SatelliteDish size={22} />}
        title={t('settings.satellites.list.emptyTitle')}
        description={t('settings.satellites.list.emptyHint')}
        action={
          <Button variant="primary" onClick={onInvite} iconLeft={<Plus size={15} />}>
            {t('settings.satellites.addMachine')}
          </Button>
        }
      />
    )
  }

  return (
    <>
      {satellites.map((satellite, index) => {
        const state = satelliteState(satellite)
        const folders = t(
          satellite.workspaces.length === 1
            ? 'settings.satellites.list.folderCount.one'
            : 'settings.satellites.list.folderCount.other',
          { count: satellite.workspaces.length }
        )
        const seen = lastSeenLabel(satellite.lastSeenAt)
        const meta = [satellite.userName, folders]
        if (state === 'offline') {
          meta.push(t('settings.satellites.list.lastSeen', { time: t(seen.key, seen.params) }))
        }

        return (
          <button
            key={satellite.peerId}
            type="button"
            className="dc-satellite-row"
            style={{ ...s.serverRow, borderTop: index === 0 ? 'none' : '1px solid var(--border-default)' }}
            onClick={() => onOpen(satellite.peerId)}
          >
            <span style={s.serverRowIcon} aria-hidden>
              <Monitor size={17} />
            </span>
            <span className="dc-satellite-row__text">
              <span className="dc-satellite-row__title">
                <span className="dc-satellite-row__name">{satellite.displayName}</span>
                <SatelliteStatusText state={state}>{t(SATELLITE_STATE_KEY[state])}</SatelliteStatusText>
              </span>
              <span className="dc-satellite-row__meta">{meta.filter(Boolean).join(' · ')}</span>
            </span>
            <span className="dc-satellite-row__chevron" aria-hidden>
              <ChevronRight size={18} />
            </span>
          </button>
        )
      })}
    </>
  )
}

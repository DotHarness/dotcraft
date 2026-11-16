import type { JSX } from 'react'
import { ChevronRight, Monitor, Plus, SatelliteDish } from 'lucide-react'

import { Button, Skeleton } from '@dotcraft/plugin'
import { useT } from '../i18n'
import { lastSeenLabel, satelliteState } from '../model'
import type { Satellite } from '../types'
import { SegmentSetupState } from './SetupState'
import { SATELLITE_STATE_KEY, SatelliteStatusText } from './Status'

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
            data-first={index === 0}
          >
            <Skeleton width="80%" height={34} />
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
            data-first={index === 0}
            onClick={() => onOpen(satellite.peerId)}
          >
            <span className="satellite-row-icon" aria-hidden>
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

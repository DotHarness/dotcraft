import { useEffect, useState, type JSX } from 'react'

import { SettingsPageHeader } from '../../SettingsPageHeader'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { useT } from '../../../../contexts/LocaleContext'
import type { MessageKey } from '../../../../../shared/locales'
import { ServersPanel } from '../servers/ServersPanel'
import { SatellitesSegment } from './SatellitesSegment'
import { SharePcSegment } from './SharePcSegment'
import { useSharePcStatus } from './useSharePcStatus'
import { WorkspaceSegment, type WorkspaceSegmentProps } from './WorkspaceSegment'

type ConnectionsSegment = 'workspace' | 'satellites' | 'share' | 'ssh'

const SEGMENTS: { value: ConnectionsSegment; labelKey: MessageKey }[] = [
  { value: 'workspace', labelKey: 'settings.connections.segments.workspace' },
  { value: 'satellites', labelKey: 'settings.connections.segments.satellites' },
  { value: 'share', labelKey: 'settings.connections.segments.share' },
  { value: 'ssh', labelKey: 'settings.connections.segments.ssh' }
]

interface ConnectionsPanelProps {
  workspace: WorkspaceSegmentProps
}

export function ConnectionsPanel({ workspace }: ConnectionsPanelProps): JSX.Element {
  const t = useT()
  const [segment, setSegment] = useState<ConnectionsSegment>('workspace')
  const [subPageOpen, setSubPageOpen] = useState(false)
  const share = useSharePcStatus()

  // With no runtime and no pairing there is nothing to read, so the segment is dropped.
  const showShare = share.status != null && (share.status.installed || share.status.peers.length > 0)
  const segments = SEGMENTS.filter(({ value }) => value !== 'share' || showShare)

  useEffect(() => {
    if (segment === 'share' && share.loaded && !showShare) setSegment('workspace')
  }, [segment, share.loaded, showShare])

  // A second-level page takes the whole surface, so the segmented control yields to it.
  const active = segments.some(({ value }) => value === segment) ? segment : 'workspace'
  const fullPage = subPageOpen && (active === 'ssh' || active === 'satellites')

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
      {!fullPage && (
        <>
          <SettingsPageHeader title={t('settings.tab.connections')} />
          <div>
            <SegmentedControl
              ariaLabel={t('settings.tab.connections')}
              value={active}
              options={segments.map(({ value, labelKey }) => ({ value, label: t(labelKey) }))}
              onChange={(next) => {
                setSubPageOpen(false)
                setSegment(next)
              }}
            />
          </div>
        </>
      )}

      {active === 'workspace' && <WorkspaceSegment {...workspace} />}
      {active === 'satellites' && <SatellitesSegment onSubPageChange={setSubPageOpen} />}
      {active === 'share' && <SharePcSegment status={share.status} onRefresh={share.reload} />}
      {active === 'ssh' && <ServersPanel embedded onSubPageChange={setSubPageOpen} />}
    </div>
  )
}

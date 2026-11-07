import { useEffect, useState, type JSX } from 'react'

import { SettingsPageHeader } from '../../SettingsPageHeader'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { useT } from '../../../../contexts/LocaleContext'
import { usePairedComputersStore } from '../../../../stores/pairedComputersStore'
import type { MessageKey } from '../../../../../shared/locales'
import { OtherDevicesSegment } from '../otherDevices/OtherDevicesSegment'
import { PhonesSegment } from '../phones/PhonesSegment'
import { SshMachinesSegment } from '../ssh/SshMachinesSegment'
import { SatellitesSegment } from './SatellitesSegment'
import { SharePcSegment } from './SharePcSegment'
import { useSharePcStatus } from './useSharePcStatus'
import { WorkspaceSegment, type WorkspaceSegmentProps } from './WorkspaceSegment'

type ConnectionsSegment = 'workspace' | 'otherDevices' | 'phones' | 'ssh' | 'satellites' | 'share'

function segmentsFor(platform: string): { value: ConnectionsSegment; labelKey: MessageKey }[] {
  return [
    { value: 'workspace', labelKey: 'settings.connections.segments.workspace' },
    { value: 'otherDevices', labelKey: 'settings.connections.segments.otherDevices' },
    {
      value: 'phones',
      labelKey: platform === 'darwin' ? 'settings.connections.segments.phones.mac' : 'settings.connections.segments.phones'
    },
    { value: 'ssh', labelKey: 'settings.connections.segments.ssh' },
    { value: 'satellites', labelKey: 'settings.connections.segments.satellites' },
    { value: 'share', labelKey: 'settings.connections.segments.share' }
  ]
}

interface ConnectionsPanelProps {
  workspace: WorkspaceSegmentProps
}

export function ConnectionsPanel({ workspace }: ConnectionsPanelProps): JSX.Element {
  const t = useT()
  const pendingLink = usePairedComputersStore((state) => state.pendingLink)
  const [segment, setSegment] = useState<ConnectionsSegment>(pendingLink ? 'otherDevices' : 'workspace')
  const [subPageOpen, setSubPageOpen] = useState(false)
  const share = useSharePcStatus()

  // With no runtime and no pairing there is nothing to read, so the segment is dropped.
  const showShare = share.status != null && (share.status.installed || share.status.peers.length > 0)
  const segments = segmentsFor(window.api.platform).filter(({ value }) => value !== 'share' || showShare)

  useEffect(() => {
    if (pendingLink) setSegment('otherDevices')
  }, [pendingLink])

  useEffect(() => {
    if (segment === 'share' && share.loaded && !showShare) setSegment('workspace')
  }, [segment, share.loaded, showShare])

  // A second-level page takes the whole surface, so the segmented control yields to it.
  const active = segments.some(({ value }) => value === segment) ? segment : 'workspace'
  const fullPage = subPageOpen && (active === 'satellites' || active === 'otherDevices')

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

      {active === 'workspace' && <WorkspaceSegment {...workspace} onShowSsh={() => setSegment('ssh')} />}
      {active === 'otherDevices' && <OtherDevicesSegment onSubPageChange={setSubPageOpen} />}
      {active === 'phones' && <PhonesSegment />}
      {active === 'ssh' && <SshMachinesSegment />}
      {active === 'satellites' && <SatellitesSegment onSubPageChange={setSubPageOpen} />}
      {active === 'share' && <SharePcSegment status={share.status} onRefresh={share.reload} />}
    </div>
  )
}

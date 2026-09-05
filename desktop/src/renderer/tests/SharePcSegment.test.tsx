// @vitest-environment jsdom
import { createRef } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConnectionsPanel } from '../components/settings/panels/connections/ConnectionsPanel'
import type { WorkspaceSegmentProps } from '../components/settings/panels/connections/WorkspaceSegment'
import type { SharePcStatus } from '../../shared/satellites'
import { installDesktopApiMock } from './desktopApiMock'

const shareStatus = vi.fn()

const SHARED: SharePcStatus = {
  installed: true,
  peers: [
    {
      peerId: 'q1',
      hubLabel: 'Ann’s workstation',
      folderPath: 'D:\\Perf',
      pairedAt: '2026-09-01T08:00:00.000Z'
    }
  ]
}

const workspace: WorkspaceSegmentProps = {
  connectionMode: 'local',
  onConnectionModeChange: vi.fn(),
  activeRemoteStackConnection: false,
  manualRemoteConnection: false,
  remoteUrl: '',
  onRemoteUrlChange: vi.fn(),
  remoteUrlErrorKey: null,
  remoteToken: '',
  onRemoteTokenChange: vi.fn(),
  binarySource: 'bundled',
  onBinarySourceChange: vi.fn(),
  binaryPath: '',
  onBinaryPathChange: vi.fn(),
  binaryPathInputRef: createRef<HTMLInputElement>(),
  onPickBinary: vi.fn(),
  resolvingBinary: false,
  resolvedBinaryPath: null,
  connectionDirty: false,
  onRevert: vi.fn(),
  revertDisabled: true
}

beforeEach(() => {
  vi.clearAllMocks()
  installDesktopApiMock({
    initialLocale: 'en',
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    satellites: { shareStatus }
  })
})

async function renderPanel(status: SharePcStatus): Promise<void> {
  shareStatus.mockResolvedValue(status)
  render(
    <LocaleProvider>
      <ConnectionsPanel workspace={workspace} />
    </LocaleProvider>
  )
  await waitFor(() => expect(shareStatus).toHaveBeenCalled())
}

describe('Share this PC', () => {
  it('drops the segment when no Satellite runtime state exists', async () => {
    await renderPanel({ installed: false, peers: [] })
    await waitFor(() => expect(screen.getByRole('button', { name: 'SSH' })).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Share this PC' })).not.toBeInTheDocument()
  })

  it('lists who may run tools here and where that is changed', async () => {
    await renderPanel(SHARED)
    fireEvent.click(await screen.findByRole('button', { name: 'Share this PC' }))

    expect(screen.getByText('Who can run tools on this PC')).toBeInTheDocument()
    expect(
      screen.getByText('Computers allowed to run tools on this PC through Satellite.')
    ).toBeInTheDocument()
    expect(screen.getByText('Ann’s workstation')).toBeInTheDocument()
    expect(screen.getByText(/shares D:\\Perf · Paired/)).toBeInTheDocument()
    expect(
      screen.getByText('Pairings are added and removed in the Satellite app on this PC.')
    ).toBeInTheDocument()
  })

  it('says the PC is not shared while the runtime has no pairings', async () => {
    await renderPanel({ installed: true, peers: [] })
    fireEvent.click(await screen.findByRole('button', { name: 'Share this PC' }))

    expect(screen.getByText('This PC is not shared')).toBeInTheDocument()
    expect(screen.queryByText(/Paired/)).not.toBeInTheDocument()
  })

  it('re-reads the runtime state on refresh', async () => {
    await renderPanel(SHARED)
    fireEvent.click(await screen.findByRole('button', { name: 'Share this PC' }))
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(shareStatus).toHaveBeenCalledTimes(2))
  })
})

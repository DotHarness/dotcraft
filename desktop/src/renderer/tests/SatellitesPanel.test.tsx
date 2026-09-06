// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { SatellitesSegment } from '../components/settings/panels/connections/SatellitesSegment'
import { useConnectionStore } from '../stores/connectionStore'
import { useSatellitesStore } from '../stores/satellitesStore'
import type { Satellite, SatelliteListResult } from '../../shared/satellites'
import { installDesktopApiMock } from './desktopApiMock'

const list = vi.fn()
const activity = vi.fn()
const revoke = vi.fn()
const onEvent = vi.fn()

function machine(overrides: Partial<Satellite> = {}): Satellite {
  return {
    peerId: 'p1',
    hostId: 'p1',
    displayName: 'B-Laptop',
    userName: 'alan',
    osName: 'Windows 11 Pro',
    connected: true,
    enrolledAt: '2026-08-28T09:12:00.000Z',
    workspaces: [
      { workspaceId: 'w1', path: 'D:\\Perf\\Engine', busy: false },
      { workspaceId: 'w2', path: 'C:\\work\\api', busy: false }
    ],
    ...overrides
  }
}

const OFFLINE = machine({
  peerId: 'p3',
  displayName: 'QA Bench',
  userName: 'kenji',
  connected: false,
  lastSeenAt: new Date(Date.now() - 2 * 24 * 60 * 60 * 1000).toISOString(),
  workspaces: [{ workspaceId: 'w3', path: 'C:\\qa', busy: false }]
})

beforeEach(() => {
  vi.clearAllMocks()
  onEvent.mockReturnValue(() => undefined)
  activity.mockResolvedValue([])
  installDesktopApiMock({
    initialLocale: 'en',
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    satellites: { list, activity, revoke, onEvent }
  })
  useConnectionStore.getState().reset()
  useSatellitesStore.setState({
    satellites: [],
    supported: true,
    loaded: false,
    bootstrapped: false,
    error: null,
    selectedPeerId: null,
    invite: null,
    creatingInvite: false,
    inviteError: null,
    revoking: null,
    activity: {},
    busy: {}
  })
})

async function renderSegment(result: SatelliteListResult): Promise<void> {
  list.mockResolvedValue(result)
  render(
    <LocaleProvider>
      <SatellitesSegment />
      <ConfirmDialogHost />
    </LocaleProvider>
  )
  await waitFor(() => expect(list).toHaveBeenCalled())
}

describe('SatellitesSegment', () => {
  it('shows the setup state when Hub has no satellite surface', async () => {
    await renderSegment({ supported: false, satellites: [] })
    expect(await screen.findByText('No satellites yet')).toBeInTheDocument()
    expect(screen.queryByText('PCs your agent can use')).not.toBeInTheDocument()
  })

  it('offers an invitation when nothing is paired yet', async () => {
    await renderSegment({ supported: true, satellites: [] })
    expect(await screen.findByText('No machines yet')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Invite a machine' })).toBeInTheDocument()
    expect(screen.getByText('PCs your agent can use')).toBeInTheDocument()
  })

  it('lists each machine with a neutral state label', async () => {
    await renderSegment({ supported: true, satellites: [machine(), OFFLINE] })
    expect(await screen.findByText('B-Laptop')).toBeInTheDocument()
    expect(screen.getByText('Ready')).toBeInTheDocument()
    expect(screen.getByText('alan · 2 folders')).toBeInTheDocument()
    expect(screen.getByText('Offline')).toBeInTheDocument()
    expect(screen.getByText('kenji · 1 folder · Last seen 2 days ago')).toBeInTheDocument()
  })

  it('reports a failed listing in a banner with no dismiss control', async () => {
    await renderSegment({ supported: true, satellites: [machine()], error: 'hub unreachable' })
    expect(await screen.findByText('Couldn’t load your machines.')).toBeInTheDocument()
    expect(screen.getByText('DotCraft Hub didn’t answer. Refresh to try again.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /dismiss/i })).not.toBeInTheDocument()
    expect(screen.getByText('B-Laptop')).toBeInTheDocument()
  })

  it('opens the detail page from a row and lists its folders', async () => {
    await renderSegment({ supported: true, satellites: [machine()] })
    fireEvent.click(await screen.findByText('B-Laptop'))
    expect(await screen.findByRole('button', { name: 'Back to Satellites' })).toBeInTheDocument()
    expect(screen.getByText('Folders')).toBeInTheDocument()
    expect(screen.getByText('D:\\Perf\\Engine')).toBeInTheDocument()
    expect(screen.getByText('Recent activity')).toBeInTheDocument()
    expect(activity).toHaveBeenCalledWith('p1')
  })

  it('revokes a machine only after the confirmation is accepted', async () => {
    revoke.mockResolvedValue(undefined)
    await renderSegment({ supported: true, satellites: [machine()] })
    fireEvent.click(await screen.findByText('B-Laptop'))

    fireEvent.click(await screen.findByRole('button', { name: 'Ready' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Remove' }))
    expect(await screen.findByText('Remove B-Laptop?')).toBeInTheDocument()
    expect(revoke).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Remove' }))
    await waitFor(() => expect(revoke).toHaveBeenCalledWith('p1'))
    expect(await screen.findByText('No machines yet')).toBeInTheDocument()
  })
})

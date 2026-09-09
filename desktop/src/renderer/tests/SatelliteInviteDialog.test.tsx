// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SatellitesSegment } from '../components/settings/panels/connections/SatellitesSegment'
import { useConnectionStore } from '../stores/connectionStore'
import { useSatellitesStore } from '../stores/satellitesStore'
import type { Satellite, SatelliteInvite } from '../../shared/satellites'
import { installDesktopApiMock } from './desktopApiMock'

const list = vi.fn()
const createInvite = vi.fn()
const onEvent = vi.fn()
const writeText = vi.fn()

const TERMS_NOTE = 'Works once. Expires in 24 hours.'

const LIVE: SatelliteInvite = {
  inviteId: 'i1',
  url: 'http://ann-pc:47600/i/inv_x1y2',
  expiresAt: new Date(Date.now() + 23 * 60 * 60 * 1000).toISOString()
}

const EXPIRED: SatelliteInvite = {
  ...LIVE,
  expiresAt: new Date(Date.now() - 60 * 60 * 1000).toISOString()
}

const MACHINE: Satellite = {
  peerId: 'p1',
  hostId: 'p1',
  displayName: 'B-Laptop',
  userName: 'alan',
  osName: 'Windows 11 Pro',
  connected: true,
  enrolledAt: '2026-08-28T09:12:00.000Z',
  workspaces: [{ workspaceId: 'w1', path: 'D:\\Perf\\Engine', busy: false }]
}

beforeEach(() => {
  vi.clearAllMocks()
  writeText.mockResolvedValue(undefined)
  onEvent.mockReturnValue(() => undefined)
  createInvite.mockResolvedValue(LIVE)
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } })
  installDesktopApiMock({
    initialLocale: 'en',
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    satellites: { list, createInvite, onEvent }
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

async function renderSegment(satellites: Satellite[] = [MACHINE]): Promise<void> {
  list.mockResolvedValue({ supported: true, satellites })
  render(
    <LocaleProvider>
      <SatellitesSegment />
    </LocaleProvider>
  )
  await waitFor(() => expect(list).toHaveBeenCalled())
}

/** Pressing focuses the control in a browser; jsdom needs to be told. */
function press(button: HTMLElement): void {
  button.focus()
  fireEvent.click(button)
}

async function openFromHeader(satellites: Satellite[] = [MACHINE]): Promise<HTMLElement> {
  await renderSegment(satellites)
  const invite = await screen.findByRole('button', { name: 'Invite' })
  press(invite)
  expect(await screen.findByRole('dialog')).toBeInTheDocument()
  return invite
}

describe('SatelliteInviteDialog', () => {
  it('opens from the header action and mints a link with nothing to fill in', async () => {
    await openFromHeader()

    await waitFor(() => expect(createInvite).toHaveBeenCalledWith())
    expect(await screen.findByLabelText('Invite link')).toHaveValue(LIVE.url)
    expect(screen.queryByLabelText('Purpose')).not.toBeInTheDocument()
  })

  it('opens from the empty state call to action', async () => {
    await renderSegment([])
    press(await screen.findByRole('button', { name: 'Invite a machine' }))

    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(await screen.findByLabelText('Invite link')).toHaveValue(LIVE.url)
  })

  it('mints exactly one link per opening', async () => {
    await openFromHeader()

    expect(await screen.findByLabelText('Invite link')).toBeInTheDocument()
    expect(createInvite).toHaveBeenCalledTimes(1)
  })

  it('states the link terms once it exists and keeps them through a copy', async () => {
    let settle: (invite: SatelliteInvite) => void = () => undefined
    createInvite.mockReturnValue(new Promise<SatelliteInvite>((resolve) => (settle = resolve)))
    await openFromHeader()

    expect(await screen.findByLabelText('Creating the link…')).toBeInTheDocument()
    expect(screen.queryByText(TERMS_NOTE)).not.toBeInTheDocument()

    await act(async () => settle(LIVE))
    expect(await screen.findByText(TERMS_NOTE)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Copy link' }))
    expect(writeText).toHaveBeenCalledWith(LIVE.url)

    // The action reports the copy; the note keeps stating the terms.
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Copy link' })).toBeNull()
    expect(screen.getByText(TERMS_NOTE)).toBeInTheDocument()
  })

  it('mints a second link for another invitation', async () => {
    useSatellitesStore.setState({ invite: LIVE })
    await openFromHeader()

    fireEvent.click(screen.getByRole('button', { name: 'Create another' }))

    await waitFor(() => expect(createInvite).toHaveBeenCalledTimes(1))
    expect(await screen.findByLabelText('Invite link')).toHaveValue(LIVE.url)
  })

  it('closes on Done and gives the focus back to the Invite button', async () => {
    useSatellitesStore.setState({ invite: LIVE })
    const opener = await openFromHeader()

    fireEvent.click(screen.getByRole('button', { name: 'Done' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(document.activeElement).toBe(opener)
  })

  it('shows the kept link again instead of minting a second one', async () => {
    useSatellitesStore.setState({ invite: LIVE })
    await openFromHeader()

    expect(screen.getByLabelText('Invite link')).toHaveValue(LIVE.url)
    expect(createInvite).not.toHaveBeenCalled()
  })

  it('mints again after the kept link is consumed', async () => {
    useSatellitesStore.setState({ invite: LIVE })
    await openFromHeader()

    fireEvent.click(screen.getByRole('button', { name: 'Done' }))
    act(() => {
      useSatellitesStore.getState().applyEvent({
        kind: 'joined',
        at: new Date().toISOString(),
        peerId: MACHINE.peerId,
        inviteId: LIVE.inviteId,
        satellite: MACHINE
      })
    })
    fireEvent.click(screen.getByRole('button', { name: 'Invite' }))

    await waitFor(() => expect(createInvite).toHaveBeenCalledTimes(1))
  })

  it('offers a new link once the kept invitation has expired', async () => {
    useSatellitesStore.setState({ invite: EXPIRED })
    await openFromHeader()

    expect(screen.getByText('This link has expired.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Copy link' })).not.toBeInTheDocument()
    expect(createInvite).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'New link' }))

    await waitFor(() => expect(createInvite).toHaveBeenCalled())
    expect(await screen.findByText(TERMS_NOTE)).toBeInTheDocument()
  })

  it('reports a Hub failure in a banner with no dismiss control', async () => {
    createInvite.mockRejectedValue(new Error('hub said no'))
    await openFromHeader()

    expect(await screen.findByText('Couldn’t create an invite link. Try again.')).toBeInTheDocument()
    expect(screen.getByText('hub said no')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /dismiss/i })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create invite link' })).toBeInTheDocument()
  })

  it('closes on Escape while the link is still being minted', async () => {
    createInvite.mockReturnValue(new Promise<SatelliteInvite>(() => undefined))
    await openFromHeader()

    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})

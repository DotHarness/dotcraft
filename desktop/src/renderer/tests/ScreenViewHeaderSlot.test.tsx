import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import type { RemoteToolRouteInfo } from '@dotcraft/sdk/contracts'
import type { Satellite } from '../../shared/satellites'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ScreenViewHeaderSlot } from '../components/conversation/screenView/ScreenViewHeaderSlot'
import { useSatellitesStore } from '../stores/satellitesStore'
import { useScreenViewStore } from '../stores/screenViewStore'
import { useThreadRouteStore } from '../stores/threadRouteStore'

vi.mock('../components/conversation/screenView/ScreenViewDock', () => ({
  ScreenViewDock: ({ hostName }: { hostName: string }) => <div data-testid="dock">{hostName}</div>
}))
vi.mock('../components/conversation/screenView/ScreenViewTheater', () => ({
  ScreenViewTheater: () => <div data-testid="theater" />
}))
vi.mock('../stores/satellitesStore', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../stores/satellitesStore')>()),
  bootstrapSatellites: () => undefined
}))

const screenViewOpen = vi.fn(async () => ({ ok: true }))
const screenViewClose = vi.fn(async () => ({ ok: true }))
const originalApi = window.api

function satellite(connected: boolean): Satellite {
  return {
    peerId: 'peer-1',
    hostId: 'peer-1',
    displayName: 'Studio PC',
    connected,
    workspaces: [],
    capabilities: ['tools', 'screen-v1']
  }
}

function route(status = 'connected'): RemoteToolRouteInfo {
  return { hostId: 'peer-1', status, threadId: 'thread-1', workspaceId: 'ws-1' }
}

function setPresence(options: { route?: RemoteToolRouteInfo | null; connected?: boolean } = {}): void {
  const next = options.route === undefined ? route() : options.route
  useThreadRouteStore.setState({ routes: next ? { 'thread-1': next } : {} })
  useSatellitesStore.setState({ satellites: [satellite(options.connected ?? true)] })
}

function renderSlot(): void {
  render(
    <LocaleProvider loadSettings={false}>
      <ScreenViewHeaderSlot threadId="thread-1" />
    </LocaleProvider>
  )
}

function launcher(): HTMLElement | null {
  return screen.queryByRole('button', { name: /Studio PC/ })
}

beforeEach(() => {
  screenViewOpen.mockClear()
  screenViewClose.mockClear()
  window.api = {
    settings: { get: async () => ({}), set: async () => undefined },
    screenView: {
      open: screenViewOpen,
      close: screenViewClose,
      tune: vi.fn(),
      ack: vi.fn(),
      onFrame: () => () => undefined,
      onState: () => () => undefined
    }
  } as unknown as typeof window.api
  useScreenViewStore.setState({
    viewId: null,
    threadId: null,
    peerId: null,
    mode: 'dock',
    status: { kind: 'connecting' },
    modeByThread: {},
    dockGeometryLoaded: true,
    requestedWidth: null
  })
})

afterEach(() => {
  window.api = originalApi
})

describe('ScreenViewHeaderSlot', () => {
  it('keeps an open view while the machine is offline, and closes it when the route goes', () => {
    act(() => setPresence())
    renderSlot()
    act(() => useScreenViewStore.getState().toggle('thread-1', 'peer-1'))
    expect(screen.getByTestId('dock')).toBeInTheDocument()

    act(() => setPresence({ connected: false }))
    expect(screen.getByTestId('dock')).toBeInTheDocument()
    expect(launcher()).not.toBeNull()

    act(() => setPresence({ route: null }))
    expect(screen.queryByTestId('dock')).toBeNull()
    expect(screenViewClose).toHaveBeenCalled()
    expect(useScreenViewStore.getState().viewId).toBeNull()
  })

  it('treats a lost lease as offline, so nothing can be opened', () => {
    act(() => setPresence({ route: route('leaseLost') }))
    renderSlot()

    expect(launcher()).toBeNull()
  })

  it('does not reopen a remembered view while the machine is offline', () => {
    useScreenViewStore.setState({ modeByThread: { 'thread-1': 'dock' } })
    act(() => setPresence({ connected: false }))
    renderSlot()

    expect(screenViewOpen).not.toHaveBeenCalled()
    expect(screen.queryByTestId('dock')).toBeNull()
  })

  it('reopens the thread remembered view once the machine can be watched', () => {
    useScreenViewStore.setState({ modeByThread: { 'thread-1': 'dock' } })
    act(() => setPresence())
    renderSlot()

    expect(screenViewOpen).toHaveBeenCalledTimes(1)
    expect(screen.getByTestId('dock')).toBeInTheDocument()
  })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { RunOnPicker, useRunOnVisible } from '../components/conversation/RunOnPicker'
import { useConnectionStore } from '../stores/connectionStore'
import { useSatellitesStore } from '../stores/satellitesStore'
import { useThreadRouteStore } from '../stores/threadRouteStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const satellitesList = vi.fn()
const settingsGet = vi.fn()

const THREAD_ID = 'thread_1'

const STUDIO_PC = {
  hostId: 'sat_studio',
  displayName: 'Studio PC',
  online: true,
  workspaces: [
    { workspaceId: 'ws_shaders', displayName: 'shaders', available: true },
    { workspaceId: 'ws_art', displayName: 'art', available: false, busyOwner: 'other' }
  ]
}

const OFFLINE_PC = {
  hostId: 'sat_qa',
  displayName: 'QA Laptop',
  online: false,
  workspaces: [{ workspaceId: 'ws_qa', displayName: 'qa', available: true }]
}

function renderPicker(disabled = false): void {
  render(
    <LocaleProvider>
      <RunOnPicker threadId={THREAD_ID} disabled={disabled} />
    </LocaleProvider>
  )
}

function renderWelcomePicker(): void {
  useThreadStore.getState().setActiveThread(null)
  render(
    <LocaleProvider>
      <RunOnPicker workspacePath="X:\\fixtures\\workspace" />
    </LocaleProvider>
  )
}

describe('RunOnPicker', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useToastStore.setState({ toasts: [] })
    useThreadRouteStore.setState({
      supported: false,
      hosts: [],
      routes: {},
      pendingRoute: null,
      connecting: null,
      attempted: new Set<string>(),
      generation: 0
    })
    useConnectionStore.setState({ capabilities: { remoteToolHost: true } })
    useSatellitesStore.setState({ satellites: [], supported: true, loaded: false, bootstrapped: false })
    useThreadStore.getState().reset()
    useThreadStore.getState().setActiveThread({
      id: THREAD_ID,
      displayName: null,
      status: 'active',
      originChannel: 'dotcraft-desktop',
      createdAt: new Date().toISOString(),
      lastActiveAt: new Date().toISOString(),
      workspacePath: 'X:\\fixtures\\workspace',
      userId: 'local',
      metadata: {},
      configuration: {},
      turns: []
    })

    satellitesList.mockResolvedValue({
      supported: true,
      satellites: [{ peerId: 'sat_studio', hostId: 'sat_studio' }]
    })
    settingsGet.mockResolvedValue({ locale: 'en' })
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'remoteToolHost/list') return { hosts: [STUDIO_PC, OFFLINE_PC], route: null }
      return {}
    })

    installDesktopApiMock({
      appServer: { sendRequest },
      settings: { get: settingsGet, set: vi.fn() },
      satellites: { list: satellitesList, onEvent: vi.fn(() => vi.fn()) }
    })
  })

  it('stays hidden until a machine is enrolled', async () => {
    satellitesList.mockResolvedValue({ supported: true, satellites: [] })

    renderPicker()

    await waitFor(() => expect(satellitesList).toHaveBeenCalled())
    expect(screen.queryByTestId('run-on-trigger')).toBeNull()
  })

  it('rests on This PC and lists every machine and folder with its state', async () => {
    renderPicker()

    const trigger = await screen.findByTestId('run-on-trigger')
    expect(trigger).toHaveTextContent('This PC')
    expect(trigger).toHaveAttribute('aria-haspopup', 'listbox')

    fireEvent.click(trigger)

    const local = await screen.findByTestId('run-on-option-this-pc')
    expect(local).toHaveTextContent('workspace')

    const free = screen.getByTestId('run-on-option-sat_studio:ws_shaders')
    expect(free).toHaveTextContent('Studio PC')
    expect(free).toHaveTextContent('shaders')
    expect(free).not.toHaveAttribute('aria-disabled')

    const busy = screen.getByTestId('run-on-option-sat_studio:ws_art')
    expect(busy).toHaveAttribute('aria-disabled', 'true')
    expect(busy).toHaveTextContent('In use by another agent')

    const offline = screen.getByTestId('run-on-option-sat_qa:ws_qa')
    expect(offline).toHaveAttribute('aria-disabled', 'true')
    expect(offline).toHaveTextContent('Offline')
  })

  it('is disabled with a reason while a turn runs', async () => {
    renderPicker(true)

    const trigger = await screen.findByTestId('run-on-trigger')
    expect(trigger).toBeDisabled()
    expect(trigger).toHaveAttribute('aria-label', 'Finish or stop the current turn first.')
  })

  it('raises a tinted toast naming the busy folder when the route is refused', async () => {
    const failure = Object.assign(new Error('This folder is already in use.'), {
      data: {
        code: 'remote_workspace_busy',
        messageKey: 'error.remoteToolHost.remoteWorkspaceBusy',
        params: { owner: 'other' },
        fallbackText: 'This folder is already in use.'
      }
    })
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'remoteToolHost/list') return { hosts: [STUDIO_PC, OFFLINE_PC], route: null }
      throw failure
    })

    renderPicker()

    fireEvent.click(await screen.findByTestId('run-on-trigger'))
    fireEvent.click(await screen.findByTestId('run-on-option-sat_studio:ws_shaders'))

    await waitFor(() => expect(useToastStore.getState().toasts).toHaveLength(1))
    const [toast] = useToastStore.getState().toasts
    expect(toast.type).toBe('error')
    expect(toast.message).toBe('Couldn’t run on Studio PC')
    expect(toast.description).toBe('Another agent is using that folder right now.')
    expect(useThreadRouteStore.getState().routes[THREAD_ID]).toBeUndefined()
  })

  it('offers the same machines on the welcome composer, before any thread exists', async () => {
    renderWelcomePicker()

    const trigger = await screen.findByTestId('run-on-trigger')
    expect(trigger).toHaveTextContent('This PC')

    await waitFor(() =>
      expect(sendRequest).toHaveBeenCalledWith('remoteToolHost/list', {})
    )

    fireEvent.click(trigger)

    expect(await screen.findByTestId('run-on-option-this-pc')).toHaveTextContent('workspace')
    expect(screen.getByTestId('run-on-option-sat_studio:ws_shaders')).toHaveTextContent('Studio PC')
    expect(screen.getByTestId('run-on-option-sat_qa:ws_qa')).toHaveAttribute('aria-disabled', 'true')
  })

  it('holds the welcome choice as a pending route instead of connecting', async () => {
    renderWelcomePicker()

    const trigger = await screen.findByTestId('run-on-trigger')
    fireEvent.click(trigger)
    fireEvent.click(await screen.findByTestId('run-on-option-sat_studio:ws_shaders'))

    await waitFor(() =>
      expect(useThreadRouteStore.getState().pendingRoute).toEqual({
        hostId: 'sat_studio',
        workspaceId: 'ws_shaders'
      })
    )
    expect(sendRequest.mock.calls.some(([method]) => method === 'remoteToolHost/connect')).toBe(false)
    expect(trigger).toHaveTextContent('Studio PC')
    expect(screen.getByTestId('run-on-routed-dot')).toBeInTheDocument()
  })

  it('drops the pending route when the welcome chip goes back to This PC', async () => {
    renderWelcomePicker()

    const trigger = await screen.findByTestId('run-on-trigger')
    fireEvent.click(trigger)
    fireEvent.click(await screen.findByTestId('run-on-option-sat_studio:ws_shaders'))
    await waitFor(() => expect(useThreadRouteStore.getState().pendingRoute).not.toBeNull())

    fireEvent.click(trigger)
    fireEvent.click(await screen.findByTestId('run-on-option-this-pc'))

    await waitFor(() => expect(useThreadRouteStore.getState().pendingRoute).toBeNull())
    expect(trigger).toHaveTextContent('This PC')
    expect(sendRequest.mock.calls.some(([method]) => method === 'remoteToolHost/disconnect')).toBe(false)
  })

  it('reports the chip hidden without the remote tool host capability', async () => {
    useConnectionStore.setState({ capabilities: {} })
    useSatellitesStore.setState({ satellites: [{ peerId: 'sat_studio' }] as never, supported: true })

    const { result } = renderHook(() => useRunOnVisible())

    await waitFor(() => expect(satellitesList).toHaveBeenCalled())
    expect(result.current).toBe(false)
  })

  it('takes the satellite subscription once however many chips ask', async () => {
    renderHook(() => useRunOnVisible())
    renderHook(() => useRunOnVisible())
    renderPicker()

    await waitFor(() => expect(satellitesList).toHaveBeenCalled())
    expect(satellitesList).toHaveBeenCalledTimes(1)
  })

  it('follows a route/changed notification instead of writing the label optimistically', async () => {
    renderPicker()

    const trigger = await screen.findByTestId('run-on-trigger')
    expect(trigger).toHaveTextContent('This PC')

    act(() => {
      useThreadRouteStore.getState().handleRouteChanged({
        threadId: THREAD_ID,
        reason: 'connected',
        route: {
          threadId: THREAD_ID,
          hostId: 'sat_studio',
          workspaceId: 'ws_shaders',
          status: 'connected'
        }
      })
    })

    await waitFor(() => expect(trigger).toHaveTextContent('Studio PC'))
    expect(trigger).not.toHaveTextContent('shaders')
    expect(screen.getByTestId('run-on-routed-dot')).toBeInTheDocument()
  })
})

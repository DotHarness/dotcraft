import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  bootstrapSatellites,
  onSatelliteEvent,
  useSatellitesStore,
  withLeases
} from '../stores/satellitesStore'
import { satelliteState, type Satellite } from '../../shared/satellites'

const list = vi.fn()
const createInvite = vi.fn()
const revoke = vi.fn()
const activity = vi.fn()
const onEvent = vi.fn()

function machine(overrides: Partial<Satellite> = {}): Satellite {
  return {
    peerId: 'p1',
    hostId: 'p1',
    displayName: 'B-Laptop',
    userName: 'alan',
    connected: true,
    workspaces: [{ workspaceId: 'w1', path: 'D:\\Perf\\Engine', busy: false }],
    ...overrides
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  onEvent.mockReturnValue(() => undefined)
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {} })
  Object.defineProperty(globalThis.window, 'api', {
    configurable: true,
    value: { satellites: { list, createInvite, revoke, activity, onEvent } }
  })
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

describe('satellitesStore', () => {
  it('loads the enrolled machines', async () => {
    list.mockResolvedValue({ supported: true, satellites: [machine()] })
    await useSatellitesStore.getState().load()
    const state = useSatellitesStore.getState()
    expect(state.loaded).toBe(true)
    expect(state.supported).toBe(true)
    expect(state.error).toBeNull()
    expect(state.satellites).toHaveLength(1)
  })

  it('keeps an old Hub in the setup state instead of reporting an error', async () => {
    list.mockResolvedValue({ supported: false, satellites: [] })
    await useSatellitesStore.getState().load()
    expect(useSatellitesStore.getState().supported).toBe(false)
    expect(useSatellitesStore.getState().error).toBeNull()
  })

  it('records the reason when the listing itself fails', async () => {
    list.mockResolvedValue({ supported: true, satellites: [], error: 'hub unreachable' })
    await useSatellitesStore.getState().load()
    expect(useSatellitesStore.getState().error).toBe('hub unreachable')
  })

  it('stays loaded when the bridge rejects', async () => {
    list.mockRejectedValue(new Error('ipc down'))
    await useSatellitesStore.getState().load()
    expect(useSatellitesStore.getState().loaded).toBe(true)
    expect(useSatellitesStore.getState().error).toBe('ipc down')
  })

  it('reads the list and subscribes to Hub events once for every consumer', () => {
    list.mockResolvedValue({ supported: true, satellites: [] })
    bootstrapSatellites()
    bootstrapSatellites()
    expect(list).toHaveBeenCalledTimes(1)
    expect(onEvent).toHaveBeenCalledTimes(1)
  })

  it('hands a Hub event to the store and to the notice listeners', () => {
    list.mockResolvedValue({ supported: true, satellites: [] })
    const seen = vi.fn()
    const stopListening = onSatelliteEvent(seen)
    bootstrapSatellites()
    useSatellitesStore.setState({ satellites: [machine()], loaded: true })

    const event = { kind: 'offline', at: '2026-09-05T10:00:00.000Z', peerId: 'p1' } as const
    onEvent.mock.calls[0][0](event)

    expect(seen).toHaveBeenCalledWith(event)
    expect(useSatellitesStore.getState().satellites[0].connected).toBe(false)
    stopListening()
  })

  it('takes a machine offline and remembers the event', () => {
    useSatellitesStore.setState({ satellites: [machine()], loaded: true })
    useSatellitesStore.getState().applyEvent({
      kind: 'offline',
      at: '2026-09-05T10:00:00.000Z',
      peerId: 'p1'
    })
    const state = useSatellitesStore.getState()
    expect(state.satellites[0].connected).toBe(false)
    expect(state.satellites[0].lastSeenAt).toBe('2026-09-05T10:00:00.000Z')
    expect(state.activity.p1).toHaveLength(1)
  })

  it('drops a revoked machine and its open detail page', () => {
    useSatellitesStore.setState({
      satellites: [machine()],
      selectedPeerId: 'p1',
      activity: { p1: [] },
      loaded: true
    })
    useSatellitesStore.getState().applyEvent({ kind: 'revoked', at: '2026-09-05T10:00:00.000Z', peerId: 'p1' })
    const state = useSatellitesStore.getState()
    expect(state.satellites).toHaveLength(0)
    expect(state.selectedPeerId).toBeNull()
    expect(state.activity.p1).toBeUndefined()
  })

  it('removes a machine the user revoked', async () => {
    revoke.mockResolvedValue(undefined)
    useSatellitesStore.setState({ satellites: [machine()], selectedPeerId: 'p1', loaded: true })
    const removed = await useSatellitesStore.getState().revoke('p1')
    expect(removed).toBe(true)
    expect(revoke).toHaveBeenCalledWith('p1')
    expect(useSatellitesStore.getState().satellites).toHaveLength(0)
    expect(useSatellitesStore.getState().revoking).toBeNull()
  })

  it('keeps the machine listed when revoking fails', async () => {
    revoke.mockRejectedValue(new Error('nope'))
    useSatellitesStore.setState({ satellites: [machine()], loaded: true })
    const removed = await useSatellitesStore.getState().revoke('p1')
    expect(removed).toBe(false)
    expect(useSatellitesStore.getState().satellites).toHaveLength(1)
  })

  it('holds a minted invitation in memory and reports a failure', async () => {
    createInvite.mockResolvedValue({
      inviteId: 'i1',
      url: 'http://ann-pc:47600/i/inv_x1y2',
      expiresAt: '2026-09-06T10:00:00.000Z'
    })
    await useSatellitesStore.getState().createInvite({ purpose: 'perf run' })
    expect(createInvite).toHaveBeenCalledWith({ purpose: 'perf run' })
    expect(useSatellitesStore.getState().invite?.inviteId).toBe('i1')

    createInvite.mockRejectedValue(new Error('hub said no'))
    useSatellitesStore.getState().clearInvite()
    await useSatellitesStore.getState().createInvite({})
    expect(useSatellitesStore.getState().invite).toBeNull()
    expect(useSatellitesStore.getState().inviteError).toBe('hub said no')
  })

  it('reads recent activity for one machine', async () => {
    activity.mockResolvedValue([{ kind: 'joined', at: '2026-09-05T09:12:00.000Z', peerId: 'p1' }])
    await useSatellitesStore.getState().loadActivity('p1')
    expect(activity).toHaveBeenCalledWith('p1')
    expect(useSatellitesStore.getState().activity.p1).toHaveLength(1)
  })

  it('overlays the AppServer lease view so a machine can read as in use', () => {
    useSatellitesStore.getState().applyBusy([
      {
        hostId: 'p1',
        workspaces: [
          { workspaceId: 'w1', available: false, busyOwner: 'other', leaseExpiresAt: '2026-09-05T11:00:00.000Z' }
        ]
      }
    ])
    const overlaid = withLeases([machine()], useSatellitesStore.getState().busy)
    expect(satelliteState(overlaid[0])).toBe('inUse')
    expect(overlaid[0].workspaces[0].busyOwner).toBe('other')
  })

  it('reads ready without an AppServer overlay', () => {
    const overlaid = withLeases([machine()], {})
    expect(satelliteState(overlaid[0])).toBe('ready')
  })
})

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
  it('records support, errors and machines from each kind of listing outcome', async () => {
    const cases: Array<[() => void, boolean, string | null, number]> = [
      [() => list.mockResolvedValue({ supported: true, satellites: [machine()] }), true, null, 1],
      [() => list.mockResolvedValue({ supported: false, satellites: [] }), false, null, 0],
      [() => list.mockResolvedValue({ supported: true, satellites: [], error: 'hub unreachable' }), true, 'hub unreachable', 0],
      [() => list.mockRejectedValue(new Error('ipc down')), true, 'ipc down', 0]
    ]
    for (const [arrange, supported, error, count] of cases) {
      useSatellitesStore.setState({ satellites: [], supported: true, loaded: false, error: null })
      arrange()
      await useSatellitesStore.getState().load()
      const state = useSatellitesStore.getState()
      expect(state).toMatchObject({ loaded: true, supported, error })
      expect(state.satellites).toHaveLength(count)
    }
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
    const state = useSatellitesStore.getState()
    expect(state.satellites[0]).toMatchObject({ connected: false, lastSeenAt: '2026-09-05T10:00:00.000Z' })
    expect(state.activity.p1).toHaveLength(1)
    stopListening()
  })

  it('brings a machine online even when the attached record still reads offline', () => {
    useSatellitesStore.setState({ satellites: [machine({ connected: false })], loaded: true })
    useSatellitesStore.getState().applyEvent({
      kind: 'online',
      at: '2026-09-05T10:05:00.000Z',
      peerId: 'p1',
      satellite: machine({ connected: false })
    })
    const state = useSatellitesStore.getState()
    expect(state.satellites[0].connected).toBe(true)
    expect(satelliteState(state.satellites[0])).toBe('ready')
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

  it('removes a revoked machine only when revoking succeeds', async () => {
    revoke.mockResolvedValue(undefined)
    useSatellitesStore.setState({ satellites: [machine()], selectedPeerId: 'p1', loaded: true })
    const removed = await useSatellitesStore.getState().revoke('p1')
    expect(removed).toBe(true)
    expect(revoke).toHaveBeenCalledWith('p1')
    expect(useSatellitesStore.getState().satellites).toHaveLength(0)
    expect(useSatellitesStore.getState().revoking).toBeNull()

    revoke.mockRejectedValue(new Error('nope'))
    useSatellitesStore.setState({ satellites: [machine()], loaded: true })
    expect(await useSatellitesStore.getState().revoke('p1')).toBe(false)
    expect(useSatellitesStore.getState().satellites).toHaveLength(1)
  })

  it('holds a minted invitation in memory and reports a failure', async () => {
    createInvite.mockResolvedValue({
      inviteId: 'i1',
      url: 'http://ann-pc:47600/i/inv_x1y2',
      expiresAt: '2026-09-06T10:00:00.000Z'
    })
    await useSatellitesStore.getState().createInvite()
    expect(createInvite).toHaveBeenCalledWith()
    expect(useSatellitesStore.getState().invite?.inviteId).toBe('i1')

    createInvite.mockRejectedValue(new Error('hub said no'))
    useSatellitesStore.getState().clearInvite()
    await useSatellitesStore.getState().createInvite()
    expect(useSatellitesStore.getState().invite).toBeNull()
    expect(useSatellitesStore.getState().inviteError).toBe('hub said no')
  })

  it('clears only the invitation consumed by a joined event', () => {
    const invite = {
      inviteId: 'inv_current',
      url: 'http://ann-pc:47600/i/inv_current',
      expiresAt: '2999-01-01T00:00:00.000Z'
    }
    useSatellitesStore.setState({ invite, inviteError: 'old error' })

    useSatellitesStore.getState().applyEvent({
      kind: 'joined',
      at: '2026-09-05T10:00:00.000Z',
      peerId: 'p1',
      inviteId: 'inv_other',
      satellite: machine()
    })
    expect(useSatellitesStore.getState().invite).toEqual(invite)

    useSatellitesStore.getState().applyEvent({
      kind: 'joined',
      at: '2026-09-05T10:01:00.000Z',
      peerId: 'p1',
      inviteId: 'inv_current',
      satellite: machine()
    })
    expect(useSatellitesStore.getState().invite).toBeNull()
    expect(useSatellitesStore.getState().inviteError).toBeNull()
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
})

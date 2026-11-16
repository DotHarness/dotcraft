import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginHost, DesktopPluginSatellite, DesktopPluginSatelliteEvent } from '@dotcraft/plugin'
import { startSatellites, useSatellitesStore, withLeases } from './state'

const list = vi.fn()
const createInvite = vi.fn()
const revoke = vi.fn()
const activity = vi.fn()
const unsubscribe = vi.fn()
let receive: (event: DesktopPluginSatelliteEvent) => void
const host = { satellites: { list, createInvite, revoke, activity, onEvent: (listener: typeof receive) => { receive = listener; return unsubscribe } } } as unknown as DesktopPluginHost
const machine = (): DesktopPluginSatellite => ({ peerId: 'p1', hostId: 'p1', displayName: 'Test PC', connected: true, capabilities: [], workspaces: [{ workspaceId: 'w1', path: 'fixture-workspace', busy: false }] })
const invite = { inviteId: 'i1', url: 'http://example.test/i/fixture', expiresAt: '2999-01-01T00:00:00Z' }
let stop: (() => void) | undefined
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>((done) => { resolve = done }); return { promise, resolve } }

beforeEach(() => { vi.clearAllMocks(); list.mockResolvedValue({ supported: true, satellites: [] }); createInvite.mockResolvedValue(invite); revoke.mockResolvedValue(undefined); activity.mockResolvedValue([]) })
afterEach(() => { stop?.(); stop = undefined })

describe('satellite plugin state', () => {
  it('ignores listing and invitation responses after a generation is disposed', async () => {
    const listing = deferred<{ supported: boolean; satellites: DesktopPluginSatellite[] }>()
    const minting = deferred<typeof invite>()
    list.mockReturnValueOnce(listing.promise)
    createInvite.mockReturnValueOnce(minting.promise)
    stop = startSatellites(host)
    const created = useSatellitesStore.getState().createInvite()
    stop()
    stop = startSatellites(host)
    await Promise.resolve()
    listing.resolve({ supported: true, satellites: [machine()] })
    minting.resolve(invite)
    await created
    expect(useSatellitesStore.getState()).toMatchObject({ satellites: [], invite: null, creatingInvite: false })
    expect(unsubscribe).toHaveBeenCalledTimes(1)
  })

  it('keeps only the latest invitation request and clears the one consumed by a joining machine', async () => {
    const old = deferred<typeof invite>()
    createInvite.mockReturnValueOnce(old.promise)
    stop = startSatellites(host)
    const first = useSatellitesStore.getState().createInvite()
    await useSatellitesStore.getState().createInvite()
    old.resolve({ ...invite, inviteId: 'stale' })
    await first
    expect(useSatellitesStore.getState().invite?.inviteId).toBe('i1')
    receive({ kind: 'joined', at: '2026-11-16T00:00:00Z', peerId: 'p1', inviteId: 'different', satellite: machine() })
    expect(useSatellitesStore.getState().invite?.inviteId).toBe('i1')
    receive({ kind: 'joined', at: '2026-11-16T00:00:00Z', peerId: 'p1', inviteId: 'i1', satellite: machine() })
    expect(useSatellitesStore.getState().invite).toBeNull()
  })

  it('follows event presence and withdraws subscriptions without accepting late events', async () => {
    const noticed = vi.fn()
    stop = startSatellites(host, noticed)
    await Promise.resolve()
    useSatellitesStore.setState({ satellites: [machine()] })
    receive({ kind: 'offline', at: '2026-11-16T00:00:00Z', peerId: 'p1', satellite: machine() })
    expect(useSatellitesStore.getState().satellites[0].connected).toBe(false)
    expect(noticed).toHaveBeenCalledTimes(1)
    stop()
    receive({ kind: 'online', at: '2026-11-16T00:00:01Z', peerId: 'p1', satellite: machine() })
    expect(useSatellitesStore.getState().satellites).toEqual([])
    expect(noticed).toHaveBeenCalledTimes(1)
  })

  it('preserves a machine after failed revocation and clears its detail after success', async () => {
    stop = startSatellites(host)
    await Promise.resolve()
    useSatellitesStore.setState({ satellites: [machine()], selectedPeerId: 'p1', activity: { p1: [] } })
    revoke.mockRejectedValueOnce(new Error('denied'))
    expect(await useSatellitesStore.getState().revoke('p1')).toBe(false)
    expect(useSatellitesStore.getState().satellites).toHaveLength(1)
    expect(await useSatellitesStore.getState().revoke('p1')).toBe(true)
    expect(useSatellitesStore.getState()).toMatchObject({ satellites: [], selectedPeerId: null, activity: {} })
  })

  it('overlays current leases without retaining a released active lease', () => {
    const satellite = machine()
    satellite.activeLease = { workspaceId: 'w1', owner: 'other' }
    satellite.workspaces[0].busy = true
    const free = withLeases([satellite], { p1: { w1: { busy: false } } })[0]
    expect(free.activeLease).toBeUndefined()
    expect(free.workspaces[0].busy).toBe(false)
  })
})

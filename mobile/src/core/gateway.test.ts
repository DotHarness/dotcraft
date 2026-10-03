import { describe, expect, it } from 'vitest'
import { GatewayClient, GatewayUnreachableError, tunnelUrl } from './gateway'
import type { PinnedNative, PinnedRequest, PinnedResponse } from './pinned'

const FP = 'a'.repeat(64)

type Behaviour = { status: number; body: unknown } | { error: string }

function nativeFor(behaviours: Record<string, Behaviour>): PinnedNative & { calls: PinnedRequest[] } {
  const calls: PinnedRequest[] = []
  return {
    calls,
    async request(request): Promise<PinnedResponse> {
      calls.push(request)
      const host = request.tunnel ?? /^https:\/\/([^:/]+):/.exec(request.url)?.[1] ?? ''
      const behaviour = behaviours[host] ?? { error: 'ERR_UNREACHABLE' }
      if ('error' in behaviour) throw Object.assign(new Error(behaviour.error), { code: behaviour.error })
      return { status: behaviour.status, body: JSON.stringify(behaviour.body) }
    },
    openSocket: () => 'unused',
    send: () => undefined,
    close: () => undefined,
    subscribe: () => () => undefined,
  }
}

const hello = { name: 'Studio PC', version: '0.8.1', port: 47610, fingerprint: FP, addresses: ['10.0.0.2', '10.0.0.3'] }

describe('gateway client', () => {
  it('tries advertised addresses in order, remembers the one that answered, and takes the list from /m/hello', async () => {
    const native = nativeFor({ '10.0.0.2': { status: 200, body: hello } })
    const gateway = new GatewayClient(native, { fingerprint: FP, port: 47610, addresses: ['10.0.0.1', '10.0.0.2'] }, 'cred')
    await gateway.hello()
    expect(native.calls.map((call) => call.url)).toEqual(['https://10.0.0.1:47610/m/hello', 'https://10.0.0.2:47610/m/hello'])
    expect(gateway.address).toBe('10.0.0.2')
    expect(gateway.candidates()).toEqual(['10.0.0.2', '10.0.0.3'])
  })

  it('sends the credential only in the Authorization header and pins every request and socket', async () => {
    const native = nativeFor({ '10.0.0.2': { status: 200, body: hello } })
    const gateway = new GatewayClient(native, { fingerprint: FP, port: 47610, addresses: ['10.0.0.2'] }, 'secret-credential')
    await gateway.hello()
    expect(native.calls[0]).toMatchObject({ url: 'https://10.0.0.2:47610/m/hello', fingerprint: FP })
    expect(native.calls[0].headers.Authorization).toBe('Bearer secret-credential')
    expect(gateway.socket('/m/events')).toEqual({
      url: 'wss://10.0.0.2:47610/m/events',
      headers: { Authorization: 'Bearer secret-credential' },
      fingerprint: FP,
    })

    const pairing = new GatewayClient(native, { fingerprint: FP, port: 47610, addresses: ['10.0.0.2'] }, null)
    await pairing.probe()
    expect(native.calls[1].headers.Authorization).toBeUndefined()
  })

  it('reports identity changed only when no address answers and one presented another certificate', async () => {
    const mismatch = nativeFor({ '10.0.0.1': { error: 'ERR_PINNING_MISMATCH' } })
    const gateway = new GatewayClient(mismatch, { fingerprint: FP, port: 47610, addresses: ['10.0.0.1', '10.0.0.2'] }, 'cred')
    await expect(gateway.hello()).rejects.toMatchObject({ mismatch: true })

    const recovered = nativeFor({ '10.0.0.1': { error: 'ERR_PINNING_MISMATCH' }, '10.0.0.2': { status: 200, body: hello } })
    const other = new GatewayClient(recovered, { fingerprint: FP, port: 47610, addresses: ['10.0.0.1', '10.0.0.2'] }, 'cred')
    await expect(other.hello()).resolves.toMatchObject({ name: 'Studio PC' })

    const offline = new GatewayClient(nativeFor({}), { fingerprint: FP, port: 47610, addresses: ['10.0.0.1'] }, 'cred')
    const error = await offline.hello().catch((caught: unknown) => caught)
    expect(error).toBeInstanceOf(GatewayUnreachableError)
    expect((error as GatewayUnreachableError).mismatch).toBe(false)
  })
})

describe('relay path', () => {
  const relay = { url: 'https://relay.example.com', hostId: 'host-1' }
  const tunnel = 'wss://relay.example.com/r/connect?host=host-1'

  it('tries the direct addresses before the relay, then keeps using the path that answered until the next hello', async () => {
    const native = nativeFor({ [tunnel]: { status: 200, body: { ...hello, relay } } })
    const gateway = new GatewayClient(native, { fingerprint: FP, port: 47610, addresses: ['10.0.0.1'], relay }, 'cred')
    await gateway.hello()
    expect(native.calls.map((call) => [call.url, call.tunnel, call.fingerprint])).toEqual([
      ['https://10.0.0.1:47610/m/hello', undefined, FP],
      ['https://127.0.0.1:47610/m/hello', tunnel, FP],
    ])
    expect(gateway.address).toBe('relay')
    expect(gateway.socket('/m/events')).toEqual({
      url: 'wss://127.0.0.1:47610/m/events',
      tunnel,
      headers: { Authorization: 'Bearer cred' },
      fingerprint: FP,
    })

    native.calls.length = 0
    await gateway.projects()
    expect(native.calls.map((call) => call.tunnel ?? call.url)).toEqual([tunnel])

    native.calls.length = 0
    await gateway.hello()
    expect(native.calls.map((call) => call.tunnel ?? call.url)).toEqual(['https://10.0.0.2:47610/m/hello', 'https://10.0.0.3:47610/m/hello', tunnel])
  })

  it('opens the tunnel with the WebSocket scheme that matches the relay URL', () => {
    expect(tunnelUrl({ url: 'https://relay.example.com/', hostId: 'h1' })).toBe('wss://relay.example.com/r/connect?host=h1')
    expect(tunnelUrl({ url: 'http://10.0.0.9:8080', hostId: 'h1' })).toBe('ws://10.0.0.9:8080/r/connect?host=h1')
    expect(tunnelUrl({ url: 'wss://example.com/relay', hostId: 'h 1' })).toBe('wss://example.com/relay/r/connect?host=h%201')
  })
})

import { EventEmitter } from 'events'
import { describe, expect, it, vi } from 'vitest'
import type { PinnedRequest, PinnedResponse } from '@dotcraft/sdk/gateway'
import { PIN_MISMATCH } from '@dotcraft/sdk/gateway'
import type { PairedComputerEvent } from '../../shared/pairedComputers'
import { PairedComputersManager } from '../pairedComputers/pairedComputersManager'
import type { CredentialVault, StoredComputer } from '../pairedComputers/pairedComputersStorage'

const FINGERPRINT = 'a'.repeat(64)
const LINK =
  `dotcraft://pair?v=1&id=computer-1&name=Studio&port=47610&fp=${FINGERPRINT}&addr=192.168.1.20&code=one-time`

type Responder = (request: PinnedRequest) => PinnedResponse | Promise<PinnedResponse>

function json(status: number, body: unknown): PinnedResponse {
  return { status, body: JSON.stringify(body) }
}

function error(status: number, code: string): PinnedResponse {
  return json(status, { error: { code, message: code } })
}

const HELLO = {
  computerId: 'computer-1',
  name: 'Studio',
  version: '0.10.4',
  port: 47610,
  fingerprint: FINGERPRINT,
  addresses: ['192.168.1.20'],
  relay: null
}

const PROJECT = { projectId: 'p1', displayName: 'alpha', path: 'D:/alpha', kind: 'project', running: true, lastActiveAt: null }

function gateway(overrides: Partial<Record<string, Responder>> = {}): Responder {
  return (request) => {
    const route = `${request.method} ${new URL(request.url).pathname}`
    const override = overrides[route]
    if (override) return override(request)
    switch (route) {
      case 'GET /m/hello':
        return json(200, HELLO)
      case 'GET /m/projects':
        return json(200, { projects: [PROJECT] })
      case 'POST /m/projects/p1/ensure':
        return json(200, PROJECT)
      case 'DELETE /m/device':
        return { status: 204, body: '' }
      default:
        return error(404, 'projectNotFound')
    }
  }
}

function stored(overrides: Partial<StoredComputer> = {}): StoredComputer {
  return {
    computerId: 'computer-1',
    name: 'Studio',
    deviceId: 'dev-1',
    port: 47610,
    fingerprint: FINGERPRINT,
    addresses: ['192.168.1.20'],
    relay: null,
    pairedAt: '2026-11-07T00:00:00.000Z',
    lastConnectedAt: null,
    version: null,
    credential: 'sealed:credential',
    projects: [],
    ...overrides
  }
}

class FakeSocket extends EventEmitter {
  terminate = vi.fn()
}

function setup(options: {
  responder?: Responder
  computers?: StoredComputer[]
  vault?: Partial<CredentialVault>
  ownComputerId?: string | null
  appVersion?: string
} = {}) {
  let saved = options.computers ?? []
  const events: PairedComputerEvent[] = []
  const removed: string[] = []
  const sockets: FakeSocket[] = []
  const requests: PinnedRequest[] = []
  const responder = options.responder ?? gateway()
  const manager = new PairedComputersManager({
    storage: { load: () => saved, save: (computers) => { saved = structuredClone(computers) } },
    vault: {
      available: () => true,
      seal: (credential) => `sealed:${credential}`,
      open: (value) => (value.startsWith('sealed:') ? value.slice(7) : null),
      ...options.vault
    },
    native: {
      request: async (request) => {
        requests.push(request)
        return responder(request)
      }
    },
    openSocket: () => {
      const socket = new FakeSocket()
      sockets.push(socket)
      return socket as never
    },
    ownComputerId: async () => options.ownComputerId ?? null,
    appVersion: options.appVersion ?? '0.10.4',
    enforceVersion: true,
    device: { displayName: 'Laptop', platform: 'windows', osVersion: '11' },
    onEvent: (event) => events.push(event),
    onRemoved: (computerId) => removed.push(computerId)
  })
  return { manager, events, removed, sockets, requests, saved: () => saved }
}

async function settle(): Promise<void> {
  for (let i = 0; i < 8; i += 1) await new Promise((resolve) => setImmediate(resolve))
}

describe('PairedComputersManager pairing', () => {
  it('waits for the computer, then stores only a sealed credential', async () => {
    const answers = [
      json(202, { status: 'pending', requestKey: 'key-1' }),
      json(200, {
        deviceId: 'dev-1',
        credential: 'plain-secret',
        computer: { computerId: 'computer-1', name: 'Studio', port: 47610, fingerprint: FINGERPRINT, addresses: ['192.168.1.20'] }
      })
    ]
    const { manager, events, saved, requests } = setup({
      responder: gateway({ 'POST /m/pair': () => answers.shift() as PinnedResponse })
    })

    const result = await manager.pair(LINK)

    expect(result.ok).toBe(true)
    expect(events).toContainEqual({ kind: 'pairingWaiting', name: 'Studio' })
    const second = JSON.parse(requests.filter((r) => r.url.endsWith('/m/pair'))[1].body as string)
    expect(second.requestKey).toBe('key-1')
    expect(second.platform).toBe('windows')
    expect(saved()[0].credential).toBe('sealed:plain-secret')
    expect(JSON.stringify(saved())).not.toContain('"plain-secret"')
    expect(JSON.stringify(events)).not.toContain('plain-secret')
  })

  it('refuses before contacting anyone when the link is invalid, its own, or nothing can protect the credential', async () => {
    const invalid = setup()
    expect(await invalid.manager.pair('https://example.com')).toEqual({ ok: false, code: 'invalidLink' })

    const own = setup({ ownComputerId: 'computer-1' })
    expect(await own.manager.pair(LINK)).toEqual({ ok: false, code: 'ownComputer' })

    const noVault = setup({ vault: { available: () => false } })
    expect(await noVault.manager.pair(LINK)).toEqual({ ok: false, code: 'storageUnavailable' })

    expect([...invalid.requests, ...own.requests, ...noVault.requests]).toEqual([])
    expect(noVault.saved()).toEqual([])
  })

  it.each([
    [error(403, 'pairingDenied'), 'pairingDenied'],
    [error(400, 'pairingCodeInvalid'), 'pairingCodeInvalid']
  ])('reports a refused pairing', async (response, code) => {
    const { manager, saved } = setup({ responder: gateway({ 'POST /m/pair': () => response }) })
    expect(await manager.pair(LINK)).toEqual({ ok: false, code })
    expect(saved()).toEqual([])
  })

  it('reports an unreachable computer', async () => {
    const { manager } = setup({
      responder: gateway({ 'POST /m/pair': () => { throw new Error('connect ECONNREFUSED') } })
    })
    expect(await manager.pair(LINK)).toEqual({ ok: false, code: 'unreachable' })
  })

  it('removes the old device when pairing again with a computer it already holds', async () => {
    const { manager, requests } = setup({
      computers: [stored({ projects: [{ projectId: 'p1', displayName: 'alpha', path: 'D:/alpha', kind: 'project' }] })],
      responder: gateway({
        'POST /m/pair': () =>
          json(200, {
            deviceId: 'dev-2',
            credential: 'new-secret',
            computer: { computerId: 'computer-1', name: 'Studio', port: 47610, fingerprint: FINGERPRINT, addresses: ['192.168.1.20'] }
          })
      })
    })
    manager.start()
    await settle()

    const result = await manager.pair(LINK)
    await settle()

    expect(result.ok && result.value.projects.some((project) => project.saved)).toBe(true)
    const removal = requests.find((r) => r.method === 'DELETE')
    expect(removal?.headers.Authorization).toBe('Bearer credential')
  })
})

describe('PairedComputersManager status', () => {
  async function statusOf(options: Parameters<typeof setup>[0]): Promise<string | undefined> {
    const { manager } = setup({ computers: [stored()], ...options })
    manager.start()
    await settle()
    return manager.list()[0]?.status
  }

  it('connects a stored computer and lists its projects', async () => {
    const { manager, sockets } = setup({ computers: [stored()] })
    manager.start()
    await settle()
    const [computer] = manager.list()
    expect(computer.status).toBe('connected')
    expect(computer.version).toBe('0.10.4')
    expect(computer.projects).toEqual([
      { projectId: 'p1', displayName: 'alpha', path: 'D:/alpha', kind: 'project', saved: false, unavailable: false }
    ])
    expect(sockets).toHaveLength(1)
  })

  it('reports a computer older than this Desktop, and accepts a newer one', async () => {
    expect(await statusOf({ appVersion: '0.10.5' })).toBe('updateRequired')
    expect(await statusOf({ appVersion: '0.10.3' })).toBe('connected')
  })

  it('is offline when no path answers, and identity changed when the certificate differs', async () => {
    expect(await statusOf({ responder: () => { throw new Error('unreachable') } })).toBe('offline')
    expect(
      await statusOf({ responder: () => { throw Object.assign(new Error('pin'), { code: PIN_MISMATCH }) } })
    ).toBe('identityChanged')
  })

  it('forgets a computer that revoked this device, keeping the rest of the app informed', async () => {
    const { manager, events, removed, saved } = setup({
      computers: [stored()],
      responder: () => error(401, 'unauthorized')
    })
    manager.start()
    await settle()
    expect(manager.list()).toEqual([])
    expect(saved()).toEqual([])
    expect(removed).toEqual(['computer-1'])
    expect(events).toContainEqual({ kind: 'revoked', name: 'Studio' })
  })

  it('reports access off after the gateway says so', async () => {
    const { manager, sockets } = setup({ computers: [stored()] })
    manager.start()
    await settle()
    sockets[0].emit('close', 1001, Buffer.from('gatewayOff'))
    expect(manager.list()[0].status).toBe('accessOff')
    manager.dispose()
  })

  it('keeps a pairing whose credential cannot be read while protected storage is unavailable', async () => {
    const { manager, saved } = setup({
      computers: [stored({ credential: 'unreadable' })],
      vault: { available: () => false }
    })
    manager.start()
    await settle()
    expect(manager.list()[0].status).toBe('offline')
    expect(saved()).toHaveLength(1)
  })
})

describe('PairedComputersManager projects', () => {
  it('marks a saved project missing from the computer as unavailable, and removes it locally', async () => {
    const { manager, saved } = setup({
      computers: [stored({ projects: [{ projectId: 'gone', displayName: 'gone', path: 'D:/gone', kind: 'project' }] })]
    })
    manager.start()
    await settle()
    expect(manager.list()[0].projects.find((project) => project.projectId === 'gone')).toMatchObject({
      saved: true,
      unavailable: true
    })
    manager.removeProject('computer-1', 'gone')
    expect(saved()[0].projects).toEqual([])
  })

  it('maps folder failures to stable codes', async () => {
    const responses = [error(400, 'invalidRequest'), error(404, 'workspaceNotFound')]
    const { manager } = setup({
      computers: [stored()],
      responder: gateway({ 'POST /m/projects': () => responses.shift() as PinnedResponse })
    })
    manager.start()
    await settle()
    expect(await manager.addFolder('computer-1', 'relative')).toEqual({ ok: false, code: 'relativePath' })
    expect(await manager.addFolder('computer-1', '/missing')).toEqual({ ok: false, code: 'workspaceNotFound' })
  })

  it('starts a stopped project before opening it, and refuses one no longer on the computer', async () => {
    let ensured = 0
    const { manager } = setup({
      computers: [stored()],
      responder: gateway({
        'GET /m/projects': () => json(200, { projects: [{ ...PROJECT, running: false }] }),
        'POST /m/projects/p1/ensure': () => {
          ensured += 1
          return json(200, PROJECT)
        }
      })
    })
    manager.start()
    await settle()
    const opened = await manager.openProject('computer-1', 'p1')
    opened.bridge.close()
    expect(ensured).toBe(1)
    await expect(manager.openProject('computer-1', 'gone')).rejects.toThrow(/no longer on the computer/)
  })

  it('opening saves the project and refuses a computer that needs an update', async () => {
    const connected = setup({ computers: [stored()] })
    connected.manager.start()
    await settle()
    const opened = await connected.manager.openProject('computer-1', 'p1')
    expect(opened.project.path).toBe('D:/alpha')
    expect(opened.bridge.wsUrl).toMatch(/^ws:\/\/127\.0\.0\.1:\d+\/ws\?token=/)
    opened.bridge.close()
    expect(connected.saved()[0].projects.map((project) => project.projectId)).toEqual(['p1'])

    const outdated = setup({ computers: [stored()], appVersion: '0.11.0' })
    outdated.manager.start()
    await settle()
    await expect(outdated.manager.openProject('computer-1', 'p1')).rejects.toThrow(/Update DotCraft/)
    expect(outdated.saved()[0].projects).toEqual([])
  })
})

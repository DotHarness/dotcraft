import { PIN_MISMATCH, type PinnedNative, type PinnedSocketOptions } from './pinned'

export interface RelayInfo {
  url: string
  hostId: string
}

const RELAY_ROUTE = 'relay'

interface GatewayTarget {
  fingerprint: string
  port: number
  addresses: string[]
  relay?: RelayInfo | null
  lastAddress?: string | null
}

interface GatewayHello {
  name: string
  version: string
  port: number
  fingerprint: string
  addresses: string[]
  relay: RelayInfo | null
}

export interface GatewayProject {
  projectId: string
  displayName: string
  running: boolean
  lastActiveAt: string | null
}

interface PairRequest {
  code: string
  displayName: string
  platform: 'ios' | 'android'
  osVersion: string
  appVersion: string
}

interface PairResult {
  deviceId: string
  credential: string
  computer: { name: string; port: number; fingerprint: string; addresses: string[] }
}

export class GatewayError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'GatewayError'
  }
}

export class GatewayUnreachableError extends Error {
  constructor(readonly mismatch: boolean) {
    super(mismatch ? 'The computer presented a different certificate.' : 'The computer could not be reached.')
    this.name = 'GatewayUnreachableError'
  }
}

export function isUnauthorized(error: unknown): boolean {
  return error instanceof GatewayError && error.status === 401
}

export function tunnelUrl(relay: RelayInfo): string {
  const base = relay.url.trim().replace(/\/+$/, '').replace(/^http(s?):\/\//, 'ws$1://')
  return `${base}/r/connect?host=${encodeURIComponent(relay.hostId)}`
}

export class GatewayClient {
  private readonly target: GatewayTarget

  constructor(
    private readonly native: PinnedNative,
    target: GatewayTarget,
    private readonly credential: string | null,
  ) {
    this.target = { ...target }
  }

  get address(): string | null {
    return this.target.lastAddress ?? null
  }

  candidates(directFirst = false): string[] {
    const last = this.target.lastAddress
    const direct = this.target.addresses
    const ordered = last && direct.includes(last) ? [last, ...direct.filter((address) => address !== last)] : direct
    if (!this.target.relay) return ordered
    return last === RELAY_ROUTE && !directFirst ? [RELAY_ROUTE, ...ordered] : [...ordered, RELAY_ROUTE]
  }

  async hello(): Promise<GatewayHello> {
    const hello = (await this.send('GET', '/m/hello', undefined, true)) as GatewayHello
    if (hello.addresses.length > 0) this.target.addresses = hello.addresses
    this.target.port = hello.port
    this.target.relay = hello.relay
    return hello
  }

  async projects(): Promise<GatewayProject[]> {
    return ((await this.send('GET', '/m/projects')) as { projects: GatewayProject[] }).projects
  }

  async ensure(projectId: string): Promise<GatewayProject> {
    return (await this.send('POST', `/m/projects/${projectId}/ensure`, {})) as GatewayProject
  }

  async pair(request: PairRequest): Promise<PairResult> {
    return (await this.send('POST', '/m/pair', request)) as PairResult
  }

  async probe(): Promise<void> {
    await this.send('GET', '/m/hello').catch((error: unknown) => {
      if (!(error instanceof GatewayError)) throw error
    })
  }

  async removeDevice(): Promise<void> {
    await this.send('DELETE', '/m/device')
  }

  socket(path: string): PinnedSocketOptions {
    return {
      ...this.endpoint(this.candidates()[0], 'wss', path),
      headers: { Authorization: `Bearer ${this.credential}` },
      fingerprint: this.target.fingerprint,
    }
  }

  private endpoint(route: string, scheme: 'https' | 'wss', path: string): { url: string; tunnel?: string } {
    if (route === RELAY_ROUTE && this.target.relay) {
      return { url: `${scheme}://127.0.0.1:${this.target.port}${path}`, tunnel: tunnelUrl(this.target.relay) }
    }
    return { url: `${scheme}://${route}:${this.target.port}${path}` }
  }

  private async send(method: 'GET' | 'POST' | 'DELETE', path: string, body?: unknown, directFirst = false): Promise<unknown> {
    const headers: Record<string, string> = { Accept: 'application/json' }
    if (this.credential) headers.Authorization = `Bearer ${this.credential}`
    if (body !== undefined) headers['Content-Type'] = 'application/json'
    let mismatch = false
    for (const route of this.candidates(directFirst)) {
      let response
      try {
        response = await this.native.request({
          ...this.endpoint(route, 'https', path),
          method,
          headers,
          body: body === undefined ? undefined : JSON.stringify(body),
          fingerprint: this.target.fingerprint,
        })
      } catch (error) {
        if ((error as { code?: string }).code === PIN_MISMATCH) mismatch = true
        continue
      }
      this.target.lastAddress = route
      const parsed = response.body ? JSON.parse(response.body) : null
      if (response.status >= 400) throw new GatewayError(response.status, parsed?.error?.message)
      return parsed
    }
    throw new GatewayUnreachableError(mismatch)
  }
}

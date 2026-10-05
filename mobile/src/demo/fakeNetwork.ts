import type { PinnedNative, PinnedRequest, PinnedResponse, PinnedSocketEvent, PinnedSocketOptions } from '../core/pinned'
import type { FakeComputer } from './fakeComputer'

function parse(url: string): { address: string; port: number; path: string } {
  const [, address, port, path] = /^\w+:\/\/([^:/]+):(\d+)(\/.*)$/.exec(url)!
  return { address, port: Number(port), path }
}

function failure(code: string, message: string): Error & { code: string } {
  return Object.assign(new Error(message), { code })
}

export class FakeRelay {
  readonly hosts = new Map<string, FakeComputer>()

  constructor(readonly address: string) {}

  host(computer: FakeComputer): void {
    computer.relay = { url: `https://${this.address}` }
    this.hosts.set(computer.id, computer)
  }

  route(tunnel: string): FakeComputer | undefined {
    const [, address, hostId] = /^wss:\/\/([^/]+)\/r\/connect\?host=([^&]+)$/.exec(tunnel) ?? []
    return address === this.address ? this.hosts.get(hostId) : undefined
  }
}

export class FakeNetwork implements PinnedNative {
  readonly requests: PinnedRequest[] = []
  readonly sockets: PinnedSocketOptions[] = []
  relays: FakeRelay[] = []
  private readonly listeners = new Set<(event: PinnedSocketEvent) => void>()
  private readonly owners = new Map<string, { computer: FakeComputer; serverId: string | null }>()
  private counter = 0

  constructor(
    readonly computers: FakeComputer[],
    public latencyMs = 0,
  ) {}

  private locate(url: string, tunnel: string | undefined): FakeComputer | undefined {
    const { address, port } = parse(url)
    const computer = tunnel
      ? this.relays.map((relay) => relay.route(tunnel)).find(Boolean)
      : this.computers.find((entry) => entry.reachable && entry.addresses.includes(address))
    return computer?.gatewayOn && computer.port === port ? computer : undefined
  }

  private later(callback: () => void): void {
    if (this.latencyMs > 0) setTimeout(callback, this.latencyMs)
    else void Promise.resolve().then(callback)
  }

  private emit(event: PinnedSocketEvent): void {
    this.later(() => {
      for (const listener of [...this.listeners]) listener(event)
    })
  }

  async request(request: PinnedRequest): Promise<PinnedResponse> {
    this.requests.push(request)
    await new Promise<void>((resolve) => this.later(resolve))
    const computer = this.locate(request.url, request.tunnel)
    if (!computer) throw failure('ERR_UNREACHABLE', 'The computer could not be reached.')
    if (computer.certificate !== request.fingerprint) throw failure('ERR_PINNING_MISMATCH', 'The certificate does not match.')
    const response = computer.http(request.method, parse(request.url).path, request.headers, request.body)
    return { status: response.status, body: response.body === null ? '' : JSON.stringify(response.body) }
  }

  openSocket(options: PinnedSocketOptions): string {
    this.sockets.push(options)
    this.counter += 1
    const id = `fake-socket-${this.counter}`
    this.later(() => {
      const computer = this.locate(options.url, options.tunnel)
      if (!computer) {
        this.emit({ type: 'error', id, code: 'ERR_UNREACHABLE', message: 'The computer could not be reached.' })
        return
      }
      if (computer.certificate !== options.fingerprint) {
        this.emit({ type: 'error', id, code: 'ERR_PINNING_MISMATCH', message: 'The certificate does not match.' })
        return
      }
      const owner = { computer, serverId: null as string | null }
      this.owners.set(id, owner)
      const finish = () => {
        this.owners.delete(id)
        if (owner.serverId) computer.socketClosed(owner.serverId)
      }
      owner.serverId = computer.socket(parse(options.url).path, options.headers, {
        open: () => this.emit({ type: 'open', id }),
        message: (text) => this.emit({ type: 'message', id, text }),
        close: (code, reason) => {
          finish()
          this.emit({ type: 'close', id, code, reason })
        },
        fail: (code, message, status) => {
          finish()
          this.emit({ type: 'error', id, code, message, status: status ?? null })
        },
      })
    })
    return id
  }

  send(id: string, text: string): void {
    const owner = this.owners.get(id)
    if (!owner?.serverId) return
    const serverId = owner.serverId
    this.later(() => owner.computer.receive(serverId, text))
  }

  close(id: string, code: number, reason: string): void {
    const owner = this.owners.get(id)
    this.owners.delete(id)
    if (owner?.serverId) owner.computer.socketClosed(owner.serverId)
    this.emit({ type: 'close', id, code, reason })
  }

  subscribe(listener: (event: PinnedSocketEvent) => void): () => void {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }
}

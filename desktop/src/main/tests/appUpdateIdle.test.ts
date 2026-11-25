import { EventEmitter } from 'events'
import { describe, expect, it, vi } from 'vitest'
import type { HubAppServerResponse } from '../desktopHub'

vi.mock('electron', () => ({ BrowserWindow: { getFocusedWindow: () => null }, powerMonitor: { getSystemIdleTime: () => 0 } }))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: class {} }))

const { isIdleForAutomaticUpdate } = await import('../appUpdateIdle')

function server(workspacePath: string, state = 'running'): HubAppServerResponse {
  return {
    workspacePath,
    canonicalWorkspacePath: workspacePath,
    state,
    endpoints: { appServerWebSocket: `ws://127.0.0.1/${workspacePath}` },
    serviceStatus: {},
    startedByHub: true
  }
}

class FakeClient extends EventEmitter {
  disposed = false
  constructor(private readonly threads: unknown[] | Error) {
    super()
    queueMicrotask(() => this.emit('ready'))
  }
  async sendRequest<T>(): Promise<T> {
    if (this.threads instanceof Error) throw this.threads
    return { data: this.threads } as T
  }
  dispose(): void { this.disposed = true }
}

function probe(threadsByEndpoint: Record<string, unknown[] | Error>, away = true) {
  const clients: FakeClient[] = []
  return {
    clients,
    run: (servers: HubAppServerResponse[]) => isIdleForAutomaticUpdate({
      listAppServers: async () => servers,
      isUserAway: () => away,
      createClient: (endpoint) => {
        const client = new FakeClient(threadsByEndpoint[endpoint] ?? [])
        clients.push(client)
        return client
      }
    })
  }
}

describe('isIdleForAutomaticUpdate', () => {
  it('is idle only when no local thread runs a turn, waits on the user, or has background work', async () => {
    const quiet = [{ id: 'a', runtime: { running: false } }, { id: 'b' }]
    expect(await probe({ 'ws://127.0.0.1/one': quiet, 'ws://127.0.0.1/two': quiet })
      .run([server('one'), server('two'), server('stopped', 'stopped')])).toBe(true)

    for (const runtime of [{ activeTurnId: 't' }, { waitingOnApproval: true }, { hasBackgroundWork: true }]) {
      const { run, clients } = probe({ 'ws://127.0.0.1/one': quiet, 'ws://127.0.0.1/two': [{ id: 'c', runtime }] })
      expect(await run([server('one'), server('two')])).toBe(false)
      expect(clients.every((client) => client.disposed)).toBe(true)
    }
  })

  it('treats an unreachable project or a present user as not idle', async () => {
    expect(await probe({ 'ws://127.0.0.1/one': new Error('closed') }).run([server('one')])).toBe(false)

    const present = probe({}, false)
    expect(await present.run([server('one')])).toBe(false)
    expect(present.clients).toHaveLength(0)
  })
})

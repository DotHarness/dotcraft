import { shell, type IpcMainInvokeEvent } from 'electron'
import {
  generateId,
  normalizeRemoteHosts,
  type RemoteHost,
  type RemoteStack,
  type RemoteStackAction
} from '../../shared/remoteServers'
import type { ConnectionSettingsDraft } from '../../shared/remoteConnection'
import type { RemoteServersManager } from './remoteServersManager'
import { inspectLocalSshConfig } from './localSshConfig'

type HandleSafe = (
  channel: string,
  listener: (event: IpcMainInvokeEvent, ...args: unknown[]) => unknown
) => void

export interface RemoteServersIpcDeps {
  handleSafe: HandleSafe
  getSettings: () => { remoteHosts?: RemoteHost[] }
  updateSettings: (partial: { remoteHosts?: RemoteHost[] }) => void | Promise<void>
  applyConnectionSettings?: (draft: ConnectionSettingsDraft) => Promise<void>
  manager: RemoteServersManager
}

/** All channels this module owns, used for teardown in `unregisterIpcHandlers`. */
export const REMOTE_SERVERS_CHANNELS = [
  'remoteHosts:list',
  'remoteHosts:ssh-config',
  'remoteHosts:create',
  'remoteHosts:update',
  'remoteHosts:delete',
  'remoteHosts:test',
  'remoteStacks:list',
  'remoteStacks:status',
  'remoteStacks:logs',
  'remoteStacks:action',
  'remoteStacks:open-app-server-tunnel',
  'remoteStacks:open-dashboard-tunnel',
  'remoteStacks:disconnect'
] as const

const VALID_ACTIONS: ReadonlySet<string> = new Set(['start', 'stop', 'restart', 'update'])

function asObject(value: unknown): Record<string, unknown> {
  return value != null && typeof value === 'object' ? (value as Record<string, unknown>) : {}
}

function findStack(host: RemoteHost, stackId: unknown): RemoteStack | undefined {
  return host.stacks.find((s) => s.id === stackId)
}

/**
 * Register the remote-server management handlers. The renderer can only choose a
 * saved host/stack and an allow-listed operation; it can never submit a command.
 */
export function registerRemoteServersHandlers(deps: RemoteServersIpcDeps): void {
  const { handleSafe, manager } = deps

  const loadHosts = (): RemoteHost[] => normalizeRemoteHosts(deps.getSettings().remoteHosts)
  const saveHosts = async (hosts: RemoteHost[]): Promise<void> => {
    await deps.updateSettings({ remoteHosts: hosts.length > 0 ? hosts : undefined })
  }
  const requireHost = (hosts: RemoteHost[], hostId: unknown): RemoteHost => {
    const host = hosts.find((h) => h.id === hostId)
    if (!host) throw new Error('Server not found.')
    return host
  }

  // ── Host CRUD ──────────────────────────────────────────────────────────────

  handleSafe('remoteHosts:list', () => loadHosts())

  handleSafe('remoteHosts:ssh-config', () => inspectLocalSshConfig())

  handleSafe('remoteHosts:create', async (_event, input) => {
    const raw = asObject(input)
    // Renderer-supplied ids are ignored; the main process assigns a fresh one.
    const [created] = normalizeRemoteHosts([{ ...raw, id: generateId('h') }])
    if (!created) throw new Error('Invalid server: a name and SSH target are required.')
    const hosts = loadHosts()
    hosts.push(created)
    await saveHosts(hosts)
    return created
  })

  handleSafe('remoteHosts:update', async (_event, input) => {
    const { id, patch } = asObject(input) as { id?: string; patch?: Record<string, unknown> }
    const hosts = loadHosts()
    const index = hosts.findIndex((h) => h.id === id)
    if (index < 0) throw new Error('Server not found.')
    const merged = { ...hosts[index], ...asObject(patch), id: hosts[index].id }
    const [normalized] = normalizeRemoteHosts([merged])
    if (!normalized) throw new Error('Invalid server update.')
    hosts[index] = normalized
    await saveHosts(hosts)
    return normalized
  })

  handleSafe('remoteHosts:delete', async (_event, input) => {
    const { id } = asObject(input) as { id?: string }
    if (typeof id === 'string') manager.closeHostTunnels(id)
    const hosts = loadHosts().filter((h) => h.id !== id)
    await saveHosts(hosts)
    return { ok: true }
  })

  handleSafe('remoteHosts:test', async (_event, input) => {
    const { id, draft } = asObject(input) as { id?: string; draft?: Record<string, unknown> }
    let host: RemoteHost | undefined
    if (draft) {
      ;[host] = normalizeRemoteHosts([{ ...draft, id: 'draft', stacks: [] }])
    } else {
      host = loadHosts().find((h) => h.id === id)
    }
    if (!host) return { reachable: false, errorCode: 'invalid', message: 'A valid SSH target is required.' }
    return manager.testHost(host)
  })

  // ── Stack operations ─────────────────────────────────────────────────────────

  handleSafe('remoteStacks:list', (_event, input) => {
    const { hostId } = asObject(input) as { hostId?: string }
    return requireHost(loadHosts(), hostId).stacks
  })

  handleSafe('remoteStacks:status', (_event, input) => {
    const { hostId, stackId } = asObject(input) as { hostId?: string; stackId?: string }
    const host = requireHost(loadHosts(), hostId)
    const stack = findStack(host, stackId)
    if (!stack) throw new Error('Stack not found.')
    return manager.status(host, stack)
  })

  handleSafe('remoteStacks:logs', (_event, input) => {
    const { hostId, stackId, service, tail } = asObject(input) as {
      hostId?: string
      stackId?: string
      service?: string
      tail?: number
    }
    const host = requireHost(loadHosts(), hostId)
    const stack = findStack(host, stackId)
    if (!stack) throw new Error('Stack not found.')
    return manager.logs(host, stack, service, typeof tail === 'number' ? tail : undefined)
  })

  handleSafe('remoteStacks:action', (_event, input) => {
    const { hostId, stackId, action } = asObject(input) as {
      hostId?: string
      stackId?: string
      action?: string
    }
    if (!action || !VALID_ACTIONS.has(action)) throw new Error('Unsupported operation.')
    const host = requireHost(loadHosts(), hostId)
    const stack = findStack(host, stackId)
    if (!stack) throw new Error('Stack not found.')
    return manager.action(host, stack, action as RemoteStackAction)
  })

  handleSafe('remoteStacks:open-app-server-tunnel', async (_event, input) => {
    const { hostId, stackId } = asObject(input) as { hostId?: string; stackId?: string }
    const host = requireHost(loadHosts(), hostId)
    const stack = findStack(host, stackId)
    if (!stack) throw new Error('Stack not found.')
    const result = await manager.openAppServerTunnel(host, stack)
    if (deps.applyConnectionSettings) {
      await deps.applyConnectionSettings({
        connectionMode: 'remote',
        remote: { url: `ws://127.0.0.1:${result.localPort}/ws`, token: result.token }
      })
    }
    // The token is never returned to the renderer.
    return { ok: true, hostId: host.id, stackId: stack.id, localPort: result.localPort }
  })

  handleSafe('remoteStacks:open-dashboard-tunnel', async (_event, input) => {
    const { hostId, stackId } = asObject(input) as { hostId?: string; stackId?: string }
    const host = requireHost(loadHosts(), hostId)
    const stack = findStack(host, stackId)
    if (!stack) throw new Error('Stack not found.')
    const result = await manager.openDashboardTunnel(host, stack)
    await shell.openExternal(result.url)
    return { ok: true, localPort: result.localPort }
  })

  handleSafe('remoteStacks:disconnect', (_event, input) => {
    const { hostId, stackId } = asObject(input) as { hostId?: string; stackId?: string }
    if (typeof hostId === 'string' && typeof stackId === 'string') {
      manager.closeStackTunnels(hostId, stackId)
    }
    return { ok: true }
  })
}

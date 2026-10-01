import { shell, type IpcMainInvokeEvent } from 'electron'
import { normalizeRemoteStacks, type RemoteStack, type RemoteStackAction } from '../../shared/dockerDeployments'
import type { SshMachine, SshMachineAddEntry, SshMachineEditPatch } from '../../shared/sshMachines'
import { generateId } from '../../shared/sshShell'
import { SshMachineInputError, type SshMachinesManager } from './sshMachinesManager'

type HandleSafe = (
  channel: string,
  listener: (event: IpcMainInvokeEvent, ...args: unknown[]) => unknown
) => void

export interface SshMachinesIpcDeps {
  handleSafe: HandleSafe
  manager: SshMachinesManager
  openRemoteProject?: (machineId: string, projectId: string) => Promise<void>
  leaveRemoteProject?: (machineId: string, projectId: string) => Promise<void>
  connectDockerDeployment?: (machine: SshMachine, stack: RemoteStack) => Promise<{ localPort?: number }>
  disconnectDockerDeployment?: (machineId: string, stackId: string) => Promise<void>
}

export const SSH_MACHINES_CHANGED_CHANNEL = 'sshMachines:changed'

export const SSH_MACHINES_CHANNELS = [
  'sshMachines:list',
  'sshMachines:discover-hosts',
  'sshMachines:add',
  'sshMachines:edit',
  'sshMachines:delete',
  'sshMachines:set-auto-connect',
  'sshMachines:reconnect',
  'sshMachines:install',
  'sshMachines:update-dotcraft',
  'sshMachines:recheck-model',
  'sshMachines:list-folders',
  'sshMachines:add-project',
  'sshMachines:remove-project',
  'sshMachines:open-project',
  'sshMachines:docker-discover',
  'sshMachines:docker-save',
  'sshMachines:docker-remove',
  'sshMachines:docker-status',
  'sshMachines:docker-logs',
  'sshMachines:docker-action',
  'sshMachines:docker-open',
  'sshMachines:docker-dashboard',
  'sshMachines:docker-disconnect'
] as const

const VALID_ACTIONS: ReadonlySet<string> = new Set(['start', 'stop', 'restart', 'update'])

function asObject(value: unknown): Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : {}
}

function optionalString(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined
}

function optionalPort(value: unknown): number | undefined {
  return typeof value === 'number' ? value : undefined
}

function readAddEntry(value: unknown): SshMachineAddEntry {
  const raw = asObject(value)
  if (raw.source === 'sshConfig') {
    return { source: 'sshConfig', alias: optionalString(raw.alias) ?? '', name: optionalString(raw.name) }
  }
  return {
    source: 'manual',
    name: optionalString(raw.name) ?? '',
    hostname: optionalString(raw.hostname) ?? '',
    port: optionalPort(raw.port),
    identityFile: optionalString(raw.identityFile)
  }
}

function readEditPatch(value: unknown): SshMachineEditPatch {
  const raw = asObject(value)
  const patch: SshMachineEditPatch = {}
  if (typeof raw.name === 'string') patch.name = raw.name
  if (typeof raw.hostname === 'string') patch.hostname = raw.hostname
  if (typeof raw.port === 'number' || raw.port === null) patch.port = raw.port as number | null
  if (typeof raw.identityFile === 'string' || raw.identityFile === null) patch.identityFile = raw.identityFile as string | null
  return patch
}

async function withValidation<T>(run: () => Promise<T>): Promise<{ ok: true; value: T } | { ok: false; errors: Record<string, string> }> {
  try {
    return { ok: true, value: await run() }
  } catch (error) {
    if (error instanceof SshMachineInputError) return { ok: false, errors: error.errors as Record<string, string> }
    throw error
  }
}

export function registerSshMachinesHandlers(deps: SshMachinesIpcDeps): void {
  const { handleSafe, manager } = deps

  const requireStack = (machineId: unknown, stackId: unknown): { machine: SshMachine; stack: RemoteStack } => {
    const machine = manager.getMachine(machineId)
    const stack = machine.stacks.find((s) => s.id === stackId)
    if (!stack) throw new Error('Docker deployment not found.')
    return { machine, stack }
  }

  handleSafe('sshMachines:list', () => manager.list())

  handleSafe('sshMachines:discover-hosts', () => manager.discoverHosts())

  handleSafe('sshMachines:add', (_event, input) => {
    const entries = Array.isArray(asObject(input).entries) ? (asObject(input).entries as unknown[]) : []
    return withValidation(() => manager.add(entries.map(readAddEntry)))
  })

  handleSafe('sshMachines:edit', (_event, input) => {
    const { id, patch } = asObject(input)
    return withValidation(() => manager.edit(String(id ?? ''), readEditPatch(patch)))
  })

  handleSafe('sshMachines:delete', async (_event, input) => {
    await manager.delete(String(asObject(input).id ?? ''))
    return { ok: true }
  })

  handleSafe('sshMachines:set-auto-connect', async (_event, input) => {
    const { id, autoConnect } = asObject(input)
    await manager.setAutoConnect(String(id ?? ''), autoConnect === true)
    return { ok: true }
  })

  handleSafe('sshMachines:reconnect', (_event, input) => manager.reconnect(String(asObject(input).id ?? '')))

  handleSafe('sshMachines:install', (_event, input) => manager.install(String(asObject(input).id ?? ''), 'install'))

  handleSafe('sshMachines:update-dotcraft', (_event, input) =>
    manager.install(String(asObject(input).id ?? ''), 'update'))

  handleSafe('sshMachines:recheck-model', (_event, input) => manager.recheckModel(String(asObject(input).id ?? '')))

  handleSafe('sshMachines:list-folders', (_event, input) => {
    const { id, path } = asObject(input)
    return manager.listFolders(String(id ?? ''), optionalString(path))
  })

  handleSafe('sshMachines:add-project', (_event, input) => {
    const { id, path } = asObject(input)
    return manager.addProject(String(id ?? ''), optionalString(path) ?? '')
  })

  handleSafe('sshMachines:remove-project', async (_event, input) => {
    const { id, projectId } = asObject(input)
    await deps.leaveRemoteProject?.(String(id ?? ''), String(projectId ?? ''))
    await manager.removeProject(String(id ?? ''), String(projectId ?? ''))
    return { ok: true }
  })

  handleSafe('sshMachines:open-project', async (_event, input) => {
    const { id, projectId } = asObject(input)
    const machine = manager.getMachine(id)
    const project = manager.getProject(machine, projectId)
    if (!deps.openRemoteProject) throw new Error('Remote projects cannot be opened in this window.')
    await deps.openRemoteProject(machine.id, project.id)
    return { ok: true }
  })

  handleSafe('sshMachines:docker-discover', (_event, input) =>
    manager.docker.discoverStacks(manager.getMachine(asObject(input).id)))

  handleSafe('sshMachines:docker-save', async (_event, input) => {
    const { id, stack } = asObject(input)
    const machine = manager.getMachine(id)
    const rawStack = asObject(stack)
    const existingId = machine.stacks.some((s) => s.id === rawStack.id) ? rawStack.id : generateId('s')
    const [normalized] = normalizeRemoteStacks([{ ...rawStack, id: existingId }], generateId)
    if (!normalized) throw new Error('Invalid Docker deployment: a name and compose directory are required.')
    const stacks = machine.stacks.some((s) => s.id === normalized.id)
      ? machine.stacks.map((s) => (s.id === normalized.id ? normalized : s))
      : [...machine.stacks, normalized]
    await manager.setStacks(machine.id, stacks)
    return normalized
  })

  handleSafe('sshMachines:docker-remove', async (_event, input) => {
    const { id, stackId } = asObject(input)
    const { machine, stack } = requireStack(id, stackId)
    await deps.disconnectDockerDeployment?.(machine.id, stack.id)
    manager.docker.closeStackTunnels(machine.id, stack.id)
    await manager.setStacks(machine.id, machine.stacks.filter((s) => s.id !== stack.id))
    return { ok: true }
  })

  handleSafe('sshMachines:docker-status', (_event, input) => {
    const { id, stackId } = asObject(input)
    const { machine, stack } = requireStack(id, stackId)
    return manager.docker.status(machine, stack)
  })

  handleSafe('sshMachines:docker-logs', (_event, input) => {
    const { id, stackId, service, tail } = asObject(input)
    const { machine, stack } = requireStack(id, stackId)
    return manager.docker.logs(machine, stack, optionalString(service), typeof tail === 'number' ? tail : undefined)
  })

  handleSafe('sshMachines:docker-action', (_event, input) => {
    const { id, stackId, action } = asObject(input)
    if (typeof action !== 'string' || !VALID_ACTIONS.has(action)) throw new Error('Unsupported operation.')
    const { machine, stack } = requireStack(id, stackId)
    return manager.docker.action(machine, stack, action as RemoteStackAction)
  })

  handleSafe('sshMachines:docker-open', async (_event, input) => {
    const { id, stackId } = asObject(input)
    const { machine, stack } = requireStack(id, stackId)
    const result = deps.connectDockerDeployment
      ? await deps.connectDockerDeployment(machine, stack)
      : await manager.docker.openAppServerTunnel(machine, stack)
    return { ok: true, machineId: machine.id, stackId: stack.id, localPort: result.localPort ?? 0 }
  })

  handleSafe('sshMachines:docker-dashboard', async (_event, input) => {
    const { id, stackId } = asObject(input)
    const { machine, stack } = requireStack(id, stackId)
    const result = await manager.docker.openDashboardTunnel(machine, stack)
    await shell.openExternal(result.url)
    return { ok: true, localPort: result.localPort }
  })

  handleSafe('sshMachines:docker-disconnect', async (_event, input) => {
    const { id, stackId } = asObject(input)
    if (typeof id === 'string' && typeof stackId === 'string') {
      if (deps.disconnectDockerDeployment) await deps.disconnectDockerDeployment(id, stackId)
      else manager.docker.closeStackTunnels(id, stackId)
    }
    return { ok: true }
  })
}

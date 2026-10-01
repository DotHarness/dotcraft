import { describe, expect, it, vi } from 'vitest'
import type { HubEvent } from '@dotcraft/sdk/hub'
import type { SshMachine, SshMachinesPayload, SshTarget } from '../../shared/sshMachines'
import type { TunnelInfo } from '../../shared/dockerDeployments'
import { SshMachinesManager } from '../sshMachines/sshMachinesManager'
import type { RemoteHubClient } from '../sshMachines/remoteHubClient'
import type { SshRunOptions, SshRunResult } from '../sshMachines/sshExecutor'
import type { TunnelOpenOptions, Tunnels } from '../sshMachines/tunnelManager'

interface RemoteState {
  os: string
  arch: string
  version: string | null
  tools: Record<string, boolean>
  hubAlive: boolean
  hubPort: number
  hubStartsOnRequest: boolean
  hubOut: string
  providerId: string
  providers: Array<Record<string, unknown>>
  appServerPort: number
  sshFailuresLeft: number
  calls: string[]
  installInputs: string[]
}

function remote(overrides: Partial<RemoteState> = {}): RemoteState {
  return {
    os: 'Linux',
    arch: 'x86_64',
    version: '0.7.10',
    tools: { docker: true, bash: true, curl: true, tar: true, setsid: true },
    hubAlive: true,
    hubPort: 49152,
    hubStartsOnRequest: true,
    hubOut: '',
    providerId: 'anthropic',
    providers: [{ id: 'anthropic', hasApiKey: true }],
    appServerPort: 41000,
    sshFailuresLeft: 0,
    calls: [],
    installInputs: [],
    ...overrides
  }
}

function ok(stdout: string): SshRunResult {
  return { code: 0, stdout, stderr: '', timedOut: false }
}

function base64(text: string): string {
  return Buffer.from(text, 'utf8').toString('base64')
}

function respond(state: RemoteState, command: string, opts?: SshRunOptions): SshRunResult {
  state.calls.push(command)
  if (state.sshFailuresLeft > 0) {
    state.sshFailuresLeft -= 1
    return { code: 255, stdout: '', stderr: 'ssh: connect to host box port 22: Connection refused\n', timedOut: false }
  }
  if (command.includes('DOTCRAFT_PROBE_BEGIN')) {
    const tools = Object.entries(state.tools).map(([tool, has]) => `tool_${tool}=${has ? 1 : 0}`).join('\n')
    return ok(
      `DOTCRAFT_PROBE_BEGIN\nos=${state.os}\narch=${state.arch}\nhome=/home/dev\nosName=Ubuntu\nosVersion=24.04\n` +
      `dotcraftPresent=${state.version ? 1 : 0}\n${state.version ? `dotcraft=${state.version}+sha\n` : ''}${tools}\nDOTCRAFT_PROBE_END\n`
    )
  }
  if (command.includes('HUB_LOCK_BEGIN')) {
    const lock = state.hubAlive
      ? JSON.stringify({ pid: 7, apiBaseUrl: `http://127.0.0.1:${state.hubPort}`, token: 'hub-token-secret', version: '0.7.10' })
      : ''
    return ok(`HUB_LOCK_BEGIN\n${lock}\nHUB_LOCK_END\n`)
  }
  if (command.includes('HUB_STARTED')) {
    if (state.hubStartsOnRequest) {
      state.hubAlive = true
      state.hubPort += 1
    }
    return ok('HUB_STARTED\n')
  }
  if (command.includes('hub.out')) return ok(state.hubOut)
  if (command.includes('bash -s')) {
    state.installInputs.push(opts?.input ?? '')
    state.version = '0.7.10'
    return ok('DotCraft v0.7.10 installed\n')
  }
  if (command.includes('CONFIG_BEGIN')) {
    return ok(`CONFIG_BEGIN\nworkspace=\nuserDefaults=${base64(JSON.stringify({ ProviderId: state.providerId }))}\nCONFIG_END\n`)
  }
  if (command.includes('workspaces/chats')) return ok('path=/home/dev/.craft/workspaces/chats\n')
  if (command.includes('WORKSPACE_READY')) return ok('WORKSPACE_READY\n')
  if (command.includes('FOLDERS_BEGIN')) {
    return ok('cwd=/home/dev\nhome=/home/dev\nFOLDERS_BEGIN\nsrc/\nFOLDERS_END\n')
  }
  return ok('')
}

class FakeTunnels implements Tunnels {
  readonly open = vi.fn(async (_target: SshTarget, ownerId: string, slot: string, remotePort: number, options?: TunnelOpenOptions): Promise<TunnelInfo> => {
    this.nextPort += 1
    const info = { localPort: this.nextPort, localUrl: `127.0.0.1:${this.nextPort}` }
    this.active.set(`${ownerId}::${slot}`, { info, remotePort, onClose: options?.onUnexpectedClose })
    return info
  })
  readonly active = new Map<string, { info: TunnelInfo; remotePort: number; onClose?: () => void }>()
  private nextPort = 60000

  get(ownerId: string, slot: string): TunnelInfo | undefined {
    return this.active.get(`${ownerId}::${slot}`)?.info
  }
  closeOne(ownerId: string, slot: string): void {
    this.active.delete(`${ownerId}::${slot}`)
  }
  closeMatching(ownerId: string, prefix: string): void {
    for (const key of [...this.active.keys()]) if (key.startsWith(`${ownerId}::${prefix}`)) this.active.delete(key)
  }
  closeForOwner(ownerId: string): void {
    this.closeMatching(ownerId, '')
  }
  closeAll(): void {
    this.active.clear()
  }
  drop(ownerId: string, slot: string): void {
    const tunnel = this.active.get(`${ownerId}::${slot}`)
    this.active.delete(`${ownerId}::${slot}`)
    tunnel?.onClose?.()
  }
}

function setup(state: RemoteState, initial: Partial<SshMachine>[] = [{}]) {
  let machines: SshMachine[] = initial.map((overrides, index) => ({
    id: `m_${index + 1}`,
    name: `box-${index + 1}`,
    source: 'manual',
    hostname: 'dev@box',
    autoConnect: true,
    projects: [{ id: 'p_1', path: '/home/dev/src', label: 'src' }],
    stacks: [],
    ...overrides
  }))
  const tunnels = new FakeTunnels()
  const eventHandlers: Array<(event: HubEvent) => void> = []
  const hubClients: Array<{ baseUrl: string; token: string }> = []
  let clock = 0
  let foreground: { machineId: string; projectId: string } | null = null
  const changes: SshMachinesPayload[] = []
  const disconnected: string[] = []
  const endpointChanges: string[] = []
  const shutdown = vi.fn(async () => {
    state.hubAlive = false
  })

  const createHubClient = (options: { baseUrl: string; token: string }): RemoteHubClient => {
    hubClients.push(options)
    const port = Number(new URL(options.baseUrl).port)
    const remotePort = [...tunnels.active.values()].find((t) => t.info.localPort === port)?.remotePort
    return {
      getStatus: async () => {
        if (!state.hubAlive || remotePort !== state.hubPort) throw new Error('connection refused')
        return {} as never
      },
      ensureAppServer: async (workspacePath: string) => ({
        workspacePath,
        canonicalWorkspacePath: workspacePath,
        state: 'running',
        endpoints: { appServerWebSocket: `ws://127.0.0.1:${state.appServerPort}/ws?token=app-token-secret` },
        serviceStatus: {},
        startedByHub: true
      }),
      shutdown,
      subscribeEvents: (onEvent, signal) => {
        eventHandlers.push(onEvent)
        return new Promise<void>((resolve) => signal.addEventListener('abort', () => resolve()))
      }
    }
  }

  const checkProviders = vi.fn(async () => ({ providers: state.providers }))
  const manager = new SshMachinesManager({
    loadMachines: () => structuredClone(machines),
    saveMachines: (next) => {
      machines = structuredClone(next)
    },
    installScript: () => '#!/usr/bin/env bash\r\necho install\r\n',
    appVersion: '0.7.10',
    packaged: true,
    runner: async (_target, command, opts) => respond(state, command, opts),
    runProcess: async () => ok('user dev\nhostname 10.0.0.12\n'),
    tunnels,
    createHubClient,
    checkProviders,
    sleep: async (ms) => {
      clock += ms
    },
    now: () => clock,
    retryDelaysMs: [10, 20],
    hubStartTimeoutMs: 2_000,
    onChanged: (payload) => changes.push(payload),
    onMachineDisconnected: (id) => disconnected.push(id),
    onProjectEndpointChanged: (machineId, projectId) => endpointChanges.push(`${machineId}/${projectId}`),
    getForegroundProject: () => foreground
  })

  return {
    manager,
    tunnels,
    eventHandlers,
    hubClients,
    changes,
    disconnected,
    endpointChanges,
    shutdown,
    checkProviders,
    machines: () => machines,
    setForeground: (value: typeof foreground) => {
      foreground = value
    }
  }
}

function statusOf(manager: SshMachinesManager, id = 'm_1') {
  return manager.list().machines.find((m) => m.id === id)?.status
}

describe('SshMachinesManager connect pipeline', () => {
  it('connects a machine with a running Hub and a usable model', async () => {
    const state = remote()
    const env = setup(state, [{ source: 'sshConfig', alias: 'build-box', hostname: undefined }])

    await expect(env.manager.connect('m_1')).resolves.toEqual({ kind: 'connected' })

    const view = env.manager.list().machines[0]
    expect(view.system).toMatchObject({ os: 'Linux', arch: 'x86_64', osName: 'Ubuntu', osVersion: '24.04', dotcraftVersion: '0.7.10', hasDocker: true })
    expect(view.resolved).toMatchObject({ user: 'dev', hostname: '10.0.0.12' })
    expect(state.calls.some((c) => c.includes('HUB_STARTED'))).toBe(false)
    expect(env.tunnels.open).toHaveBeenCalledWith({ destination: 'build-box' }, 'm_1', 'hub', 49152, expect.anything())
    expect(env.hubClients[0].token).toBe('hub-token-secret')
    expect(env.tunnels.get('m_1', 'model')).toBeUndefined()
    expect(env.checkProviders).toHaveBeenCalledWith(expect.stringMatching(/^ws:\/\/127\.0\.0\.1:\d+\/ws\?token=app-token-secret$/))
    expect(JSON.stringify(env.changes)).not.toContain('hub-token-secret')
    expect(JSON.stringify(env.changes)).not.toContain('app-token-secret')
  })

  it('reports DotCraft not installed, then installs and finishes connecting', async () => {
    const state = remote({ version: null, hubAlive: false })
    const env = setup(state)

    await expect(env.manager.connect('m_1')).resolves.toEqual({ kind: 'notInstalled' })
    expect(state.calls.some((c) => c.includes('HUB_LOCK_BEGIN'))).toBe(false)

    await expect(env.manager.install('m_1', 'install')).resolves.toEqual({ kind: 'connected' })
    const installCall = state.calls.find((c) => c.includes('bash -s'))
    expect(installCall).toBe("DOTCRAFT_VERSION='v0.7.10' bash -s")
    expect(state.installInputs).toEqual(['#!/usr/bin/env bash\necho install\n'])
    expect(state.calls.some((c) => c.includes('HUB_STARTED'))).toBe(true)
    expect(env.shutdown).not.toHaveBeenCalled()
  })

  it('requires an update for an older DotCraft and stops the running Hub before updating', async () => {
    const state = remote({ version: '0.7.9' })
    const env = setup(state)

    await expect(env.manager.connect('m_1')).resolves.toEqual({ kind: 'updateRequired', installedVersion: '0.7.9' })
    await expect(env.manager.install('m_1', 'update')).resolves.toEqual({ kind: 'connected' })

    expect(env.shutdown).toHaveBeenCalledTimes(1)
    const shutdownIndex = state.calls.findIndex((c) => c.includes('bash -s'))
    expect(shutdownIndex).toBeGreaterThan(-1)
    expect(state.calls.slice(shutdownIndex).some((c) => c.includes('HUB_STARTED'))).toBe(true)
  })

  it('fails install before downloading when a required tool is missing', async () => {
    const state = remote({ version: null, tools: { bash: true, curl: false, tar: true } })
    const env = setup(state)

    await expect(env.manager.install('m_1', 'install')).resolves.toEqual({
      kind: 'failed',
      message: 'Missing required tool on the machine: curl'
    })
    expect(state.calls.some((c) => c.includes('bash -s'))).toBe(false)
  })

  it('marks Linux on ARM and Windows as unsupported and turns the switch off', async () => {
    const arm = setup(remote({ arch: 'aarch64' }))
    await expect(arm.manager.connect('m_1')).resolves.toMatchObject({ kind: 'unsupported', unsupported: 'linuxArm' })
    expect(arm.machines()[0].autoConnect).toBe(false)
    await expect(arm.manager.setAutoConnect('m_1', true)).rejects.toThrow('not supported')

    const windows = setup(remote({ os: 'MSYS_NT-10.0' }))
    await expect(windows.manager.connect('m_1')).resolves.toMatchObject({ kind: 'unsupported', unsupported: 'windows' })
    expect(windows.tunnels.open).not.toHaveBeenCalled()
  })

  it('retries SSH failures with backoff and then connects', async () => {
    const state = remote({ sshFailuresLeft: 2 })
    const env = setup(state)

    await expect(env.manager.connect('m_1')).resolves.toEqual({ kind: 'connected' })
    const kinds = env.changes.map((payload) => payload.machines[0].status.kind)
    expect(kinds.filter((kind) => kind === 'connecting').length).toBeGreaterThanOrEqual(3)
  })

  it('reports Connection failed with the ssh stderr line when the retry budget is spent', async () => {
    const state = remote({ sshFailuresLeft: 10 })
    const env = setup(state)

    await expect(env.manager.connect('m_1')).resolves.toEqual({
      kind: 'failed',
      message: 'ssh: connect to host box port 22: Connection refused'
    })
    expect(state.calls.filter((c) => c.includes('DOTCRAFT_PROBE_BEGIN'))).toHaveLength(3)
  })

  it('starts a missing Hub detached and reports hub.out when it never comes up', async () => {
    const started = remote({ hubAlive: false })
    const env = setup(started)
    await expect(env.manager.connect('m_1')).resolves.toEqual({ kind: 'connected' })
    expect(started.calls.filter((c) => c.includes('HUB_STARTED'))).toHaveLength(1)

    const stuck = remote({ hubAlive: false, hubStartsOnRequest: false, hubOut: 'starting\nerror: port in use\n' })
    const failed = setup(stuck)
    await expect(failed.manager.connect('m_1')).resolves.toEqual({
      kind: 'failed',
      message: 'DotCraft Hub did not start: error: port in use'
    })
  })

  it('asks to set up a model when the selected provider has no credential', async () => {
    const env = setup(remote({ providers: [{ id: 'anthropic', hasApiKey: false }] }))
    await expect(env.manager.connect('m_1')).resolves.toEqual({ kind: 'setupModel' })
  })

  it('reconnects after a dropped Hub forward while the switch is on', async () => {
    const env = setup(remote())
    await env.manager.connect('m_1')
    const probesBefore = env.changes.length

    env.tunnels.drop('m_1', 'hub')
    await vi.waitFor(() => expect(statusOf(env.manager)).toEqual({ kind: 'connected' }))
    expect(env.changes.slice(probesBefore).some((p) => p.machines[0].status.kind === 'connecting')).toBe(true)
    expect(env.disconnected).toEqual([])
  })

  it('auto-connects only switched-on machines', async () => {
    const state = remote()
    const env = setup(state, [{}, { id: 'm_2', name: 'off', autoConnect: false }])
    env.manager.connectAutoMachines()
    await vi.waitFor(() => expect(statusOf(env.manager)).toEqual({ kind: 'connected' }))
    expect(statusOf(env.manager, 'm_2')).toEqual({ kind: 'notConnected' })
  })
})

describe('SshMachinesManager machines and projects', () => {
  it('adds machines from ssh config and manual entry, rejecting invalid input', async () => {
    const env = setup(remote(), [])
    await expect(env.manager.add([{ source: 'manual', name: '', hostname: 'bad host' }])).rejects.toMatchObject({
      errors: { name: 'required', hostname: 'invalid' }
    })

    const created = await env.manager.add([
      { source: 'sshConfig', alias: 'build-box' },
      { source: 'manual', name: 'Lab', hostname: 'dev@lab', port: 2222 }
    ])
    expect(created.map((m) => [m.name, m.autoConnect])).toEqual([
      ['build-box', true],
      ['Lab', true]
    ])
    expect(env.machines()).toHaveLength(2)
    await vi.waitFor(() => expect(env.manager.list().machines.every((m) => m.status.kind === 'connected')).toBe(true))
  })

  it('switching off and deleting close forwards but never touch the Hub', async () => {
    const env = setup(remote())
    await env.manager.connect('m_1')
    await env.manager.openProject('m_1', 'p_1')

    await env.manager.setAutoConnect('m_1', false)
    expect(statusOf(env.manager)).toEqual({ kind: 'notConnected' })
    expect(env.tunnels.active.size).toBe(0)
    expect(env.disconnected).toEqual(['m_1'])
    expect(env.shutdown).not.toHaveBeenCalled()

    await env.manager.delete('m_1')
    expect(env.machines()).toEqual([])
    expect(env.disconnected).toEqual(['m_1', 'm_1'])
  })

  it('opens a project through the remote Hub and a forward of the AppServer port', async () => {
    const state = remote()
    const env = setup(state, [{ autoConnect: false }])

    const opened = await env.manager.openProject('m_1', 'p_1')

    expect(opened.remotePort).toBe(41000)
    expect(opened.tokenPresent).toBe(true)
    expect(opened.wsUrl).toBe(`ws://127.0.0.1:${opened.localPort}/ws?token=app-token-secret`)
    expect(env.tunnels.active.get('m_1::project:p_1')?.remotePort).toBe(41000)
    expect(env.machines()[0].autoConnect).toBe(false)
  })

  it('follows AppServer port changes for the foreground project only', async () => {
    const env = setup(remote())
    env.setForeground({ machineId: 'm_1', projectId: 'p_1' })
    await env.manager.openProject('m_1', 'p_1')
    const emit = env.eventHandlers[env.eventHandlers.length - 1]

    emit({ kind: 'appserver.running', at: '', workspacePath: '/home/dev/src', data: { endpoints: { appServerWebSocket: 'ws://127.0.0.1:41000/ws?token=x' } } })
    emit({ kind: 'port.allocated', at: '', workspacePath: '/home/dev/other', data: { service: 'appServerWebSocket', port: 1 } })
    expect(env.endpointChanges).toEqual([])

    emit({ kind: 'port.allocated', at: '', workspacePath: '/home/dev/src', data: { service: 'appServerWebSocket', port: 41001 } })
    expect(env.endpointChanges).toEqual(['m_1/p_1'])
  })

  it('refuses to open projects on machines without DotCraft', async () => {
    const env = setup(remote({ version: null }), [{ name: 'lab' }])
    await expect(env.manager.openProject('m_1', 'p_1')).rejects.toThrow('DotCraft is not installed on lab.')
  })

  it('lists folders, adds a project once, and removes it', async () => {
    const env = setup(remote(), [{ projects: [] }])
    const listing = await env.manager.listFolders('m_1')
    expect(listing.folders).toEqual([{ name: 'src', path: '/home/dev/src', saved: false }])

    const project = await env.manager.addProject('m_1', '/home/dev/src/')
    expect(project).toMatchObject({ path: '/home/dev/src', label: 'src' })
    await expect(env.manager.addProject('m_1', '/home/dev/src')).rejects.toThrow('already a project')
    await expect(env.manager.addProject('m_1', '~/src')).rejects.toThrow('absolute')

    await env.manager.removeProject('m_1', project.id)
    expect(env.machines()[0].projects).toEqual([])
  })
})

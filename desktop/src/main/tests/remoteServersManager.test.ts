import { describe, it, expect, vi } from 'vitest'
import { RemoteServersManager } from '../remoteServers/remoteServersManager'
import type { SshRunner, SshRunResult } from '../remoteServers/sshExecutor'
import type { RemoteHost, RemoteStack } from '../../shared/remoteServers'
import type { TunnelManager } from '../remoteServers/tunnelManager'

const host: RemoteHost = { id: 'h1', name: 'Cloud', sshTarget: 'user@cloud', stacks: [] }
const stack: RemoteStack = {
  id: 's1',
  name: 'prod',
  composeDir: '~/sample-stack/docker',
  appServerPort: 9100,
  dashboardPort: 8080,
  sandboxProfile: false
}

const STATUS_OK =
  'STATUS_BEGIN\ndocker=ok\ncompose=ok\nenv=ok\nconfig=ok\ntoken=present\n' +
  'PS_BEGIN\n{"Service":"dotcraft","State":"running"}\nPS_END\nSTATUS_END'

const DISCOVER_OK =
  'DISCOVER_BEGIN\n' +
  JSON.stringify({
    Config: {
      Image: 'ghcr.io/dotharness/dotcraft:latest',
        Labels: {
          'com.docker.compose.project': 'deploy',
          'com.docker.compose.service': 'dotcraft',
          'com.docker.compose.project.working_dir': '/srv/sample/demo-stack/deploy'
        }
      },
    Mounts: [{ Source: '/srv/sample/demo-stack/deploy/workspace', Destination: '/workspace' }],
    NetworkSettings: {
      Ports: {
        '9100/tcp': [{ HostIp: '127.0.0.1', HostPort: '9100' }],
        '8080/tcp': [{ HostIp: '127.0.0.1', HostPort: '8080' }]
      }
    }
  }) +
  '\nDISCOVER_END'

function ok(stdout: string): SshRunResult {
  return { code: 0, stdout, stderr: '', timedOut: false }
}
function fail(stderr: string, code = 1): SshRunResult {
  return { code, stdout: '', stderr, timedOut: false }
}

function makeRunner(
  handler: (cmd: string) => SshRunResult
): { runner: SshRunner; calls: string[]; opts: Array<{ timeoutMs?: number; connectTimeoutSec?: number }> } {
  const calls: string[] = []
  const opts: Array<{ timeoutMs?: number; connectTimeoutSec?: number }> = []
  const runner: SshRunner = async (_host, cmd, runOpts) => {
    calls.push(cmd)
    opts.push({
      timeoutMs: runOpts?.timeoutMs,
      connectTimeoutSec: runOpts?.connectTimeoutSec
    })
    return handler(cmd)
  }
  return { runner, calls, opts }
}

/** Route a remote command to a canned result by recognizing its allow-listed shape. */
function route(cmd: string): SshRunResult {
  if (cmd.includes('SSH_OK')) return ok('SSH_OK\ndocker=ok\ncompose=ok')
  if (cmd.includes('DISCOVER_BEGIN')) return ok(DISCOVER_OK)
  if (cmd.includes('STATUS_BEGIN')) return ok(STATUS_OK)
  if (cmd.includes('CONFIG_BEGIN')) {
    return ok(
      'CONFIG_BEGIN\n' +
      `workspace=${Buffer.from('{"ProviderId":"anthropic-main","CustomSettings":{"theme":"dark"}}').toString('base64')}\n` +
      `userDefaults=${Buffer.from('{"ProviderId":"openai","CustomSettings":{"theme":"light"}}').toString('base64')}\n` +
      'CONFIG_END'
    )
  }
  if (cmd.includes('BACKUP_OK')) return ok('BACKUP_OK .dotcraft-backups/20260601-120000')
  if (cmd.includes('--remove-orphans')) return ok('Recreating dotcraft ... done')
  if (cmd.includes(' pull')) return ok('dotcraft Pulling\ndownloaded newer image')
  if (cmd.includes(' restart')) return ok('Restarting')
  if (cmd.includes(' stop')) return ok('Stopping')
  if (cmd.includes('logs --no-color')) return ok('log line one\nlog line two')
  if (cmd.startsWith('cat ')) return ok('fixture-appserver-token\n')
  return ok('')
}

describe('RemoteServersManager', () => {
  it('parses status and stamps checkedAt from the injected clock', async () => {
    const { runner } = makeRunner(route)
    const mgr = new RemoteServersManager({ runner, now: () => 12345 })
    const status = await mgr.status(host, stack)
    expect(status.health).toBe('running')
    expect(status.tokenPresent).toBe(true)
    expect(status.checkedAt).toBe(12345)
  })

  it('redacts secrets from logs (known secret + secret-like assignment)', async () => {
    const { runner } = makeRunner(() =>
      ok('APPSERVER_TOKEN=fixture-appserver-token\nthe explicit value is fixture-redaction-value here')
    )
    const mgr = new RemoteServersManager({ runner })
    const result = await mgr.logs(host, stack, undefined, 200, ['fixture-redaction-value'])
    expect(result.text).toContain('[redacted]')
    expect(result.text).not.toContain('fixture-appserver-token')
    expect(result.text).not.toContain('fixture-redaction-value')
  })

  it('restart runs the lifecycle command then refreshes status', async () => {
    const { runner, calls } = makeRunner(route)
    const mgr = new RemoteServersManager({ runner })
    const result = await mgr.action(host, stack, 'restart')
    expect(result.ok).toBe(true)
    expect(result.status?.health).toBe('running')
    expect(calls[0]).toContain('restart')
    expect(calls[1]).toContain('STATUS_BEGIN')
  })

  it('reports a redacted failure and skips status when an action fails', async () => {
    const { runner, calls } = makeRunner(() => fail('docker compose stop: APPSERVER_TOKEN=fixture-failure-token failed'))
    const mgr = new RemoteServersManager({ runner })
    const result = await mgr.action(host, stack, 'stop')
    expect(result.ok).toBe(false)
    expect(result.status).toBeUndefined()
    expect(result.message).toContain('[redacted]')
    expect(result.message).not.toContain('fixture-failure-token')
    expect(calls).toHaveLength(1) // no status refresh on failure
  })

  it('update runs backup → pull → up → status in order and reports changed', async () => {
    const { runner, calls } = makeRunner(route)
    const mgr = new RemoteServersManager({ runner })
    const result = await mgr.action(host, stack, 'update')
    expect(result.ok).toBe(true)
    expect(result.changed).toBe(true)
    expect(result.status?.health).toBe('running')
    expect(calls).toHaveLength(4)
    expect(calls[0]).toContain('BACKUP_OK')
    expect(calls[1]).toContain(' pull')
    expect(calls[2]).toContain('--remove-orphans')
    expect(calls[3]).toContain('STATUS_BEGIN')
  })

  it('update stops at the failing step and does not recreate or refresh', async () => {
    const { runner, calls } = makeRunner((cmd) => {
      if (cmd.includes('BACKUP_OK')) return ok('BACKUP_OK x')
      if (cmd.includes(' pull')) return fail('pull failed: network')
      return ok('')
    })
    const mgr = new RemoteServersManager({ runner })
    const result = await mgr.action(host, stack, 'update')
    expect(result.ok).toBe(false)
    expect(result.message).toContain('pull')
    expect(calls).toHaveLength(2) // backup + pull only
  })

  it('testHost reports reachable with docker/compose detected, and unreachable on error', async () => {
    const reachable = makeRunner(route)
    const mgrA = new RemoteServersManager({ runner: reachable.runner, now: () => 0 })
    const okRes = await mgrA.testHost(host)
    expect(okRes.reachable).toBe(true)
    expect(okRes.dockerOk).toBe(true)

    const down = makeRunner(() => fail('ssh: connect to host cloud port 22: Connection timed out'))
    const mgrB = new RemoteServersManager({ runner: down.runner })
    const badRes = await mgrB.testHost(host)
    expect(badRes.reachable).toBe(false)
    expect(badRes.errorCode).toBe('unreachable')
  })

  it('discovers remote DotCraft compose stacks', async () => {
    const { runner, calls } = makeRunner(route)
    const mgr = new RemoteServersManager({ runner })
    const stacks = await mgr.discoverStacks(host)
    expect(stacks).toHaveLength(1)
    expect(stacks[0]).toMatchObject({
      name: 'demo-stack',
      composeDir: '/srv/sample/demo-stack/deploy',
      workspaceDir: '/srv/sample/demo-stack/deploy/workspace',
      projectName: 'deploy',
      appServerPort: 9100,
      dashboardPort: 8080
    })
    expect(calls[0]).toContain('DISCOVER_BEGIN')
  })

  it('reads the remote token (trimmed) for connect', async () => {
    const { runner, opts } = makeRunner(route)
    const mgr = new RemoteServersManager({ runner })
    expect(await mgr.readToken(host, stack)).toBe('fixture-appserver-token')
    expect(opts[0]).toMatchObject({ timeoutMs: 30_000, connectTimeoutSec: 8 })
  })

  it('reads remote workspace core config snapshots without exposing arbitrary commands', async () => {
    const { runner, calls, opts } = makeRunner(route)
    const mgr = new RemoteServersManager({ runner })

    const result = await mgr.readCoreConfig(host, stack)

    expect(result.workspaceRaw).toBe('{"ProviderId":"anthropic-main","CustomSettings":{"theme":"dark"}}')
    expect(result.userDefaultsRaw).toBe('{"ProviderId":"openai","CustomSettings":{"theme":"light"}}')
    expect(calls[0]).toContain('CONFIG_BEGIN')
    expect(calls[0]).toContain('.craft/config.json')
    expect(opts[0]).toMatchObject({ timeoutMs: 20_000, connectTimeoutSec: 8 })
  })

  it('fails token reads with a redacted command error', async () => {
    const { runner } = makeRunner((cmd) =>
      cmd.startsWith('cat ')
        ? fail('cat failed: APPSERVER_TOKEN=fixture-secret-token')
        : route(cmd)
    )
    const mgr = new RemoteServersManager({ runner })

    await expect(mgr.readToken(host, stack)).rejects.toThrow('[redacted]')
    try {
      await mgr.readToken(host, stack)
    } catch (error) {
      expect(error instanceof Error ? error.message : String(error)).not.toContain('fixture-secret-token')
    }
  })

  it('fails token reads when the remote token file is missing or empty', async () => {
    const { runner } = makeRunner((cmd) => cmd.startsWith('cat ') ? ok('\n') : route(cmd))
    const mgr = new RemoteServersManager({ runner })

    await expect(mgr.readToken(host, stack)).rejects.toThrow('Remote AppServer token was not found')
  })

  it('does not open an AppServer tunnel when the token read fails', async () => {
    const { runner } = makeRunner((cmd) => cmd.startsWith('cat ') ? fail('cat: token missing') : route(cmd))
    const tunnels = {
      closeOne: vi.fn(),
      open: vi.fn()
    } as unknown as TunnelManager
    const mgr = new RemoteServersManager({ runner, tunnels })

    await expect(mgr.openAppServerTunnel(host, stack)).rejects.toThrow('cat: token missing')
    expect(tunnels.open).not.toHaveBeenCalled()
  })

  it('can force a fresh AppServer tunnel before opening', async () => {
    const { runner } = makeRunner(route)
    const tunnels = {
      closeOne: vi.fn(),
      open: vi.fn().mockResolvedValue({ localPort: 49123, localUrl: '127.0.0.1:49123' })
    } as unknown as TunnelManager
    const mgr = new RemoteServersManager({ runner, tunnels })

    const result = await mgr.openAppServerTunnel(host, stack, { forceNew: true })

    expect(tunnels.closeOne).toHaveBeenCalledWith(host.id, stack.id, 'appserver')
    expect(tunnels.open).toHaveBeenCalledWith(host, stack.id, stack.appServerPort, 'appserver')
    expect(result.localPort).toBe(49123)
    expect(result.tokenPresent).toBe(true)
    expect(result.wsUrl).toContain('127.0.0.1:49123')
    expect(result.wsUrl).toContain('token=fixture-appserver-token')
  })
})

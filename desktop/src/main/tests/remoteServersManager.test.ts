import { describe, it, expect } from 'vitest'
import { RemoteServersManager } from '../remoteServers/remoteServersManager'
import type { SshRunner, SshRunResult } from '../remoteServers/sshExecutor'
import type { RemoteHost, RemoteStack } from '../../shared/remoteServers'

const host: RemoteHost = { id: 'h1', name: 'Cloud', sshTarget: 'user@cloud', stacks: [] }
const stack: RemoteStack = {
  id: 's1',
  name: 'prod',
  composeDir: '~/dotcraft/deploy/docker',
  appServerPort: 9100,
  dashboardPort: 8080,
  sandboxProfile: false
}

const STATUS_OK =
  'STATUS_BEGIN\ndocker=ok\ncompose=ok\nenv=ok\nconfig=ok\ntoken=present\n' +
  'PS_BEGIN\n{"Service":"dotcraft","State":"running"}\nPS_END\nSTATUS_END'

function ok(stdout: string): SshRunResult {
  return { code: 0, stdout, stderr: '', timedOut: false }
}
function fail(stderr: string, code = 1): SshRunResult {
  return { code, stdout: '', stderr, timedOut: false }
}

function makeRunner(handler: (cmd: string) => SshRunResult): { runner: SshRunner; calls: string[] } {
  const calls: string[] = []
  const runner: SshRunner = async (_host, cmd) => {
    calls.push(cmd)
    return handler(cmd)
  }
  return { runner, calls }
}

/** Route a remote command to a canned result by recognizing its allow-listed shape. */
function route(cmd: string): SshRunResult {
  if (cmd.includes('SSH_OK')) return ok('SSH_OK\ndocker=ok\ncompose=ok')
  if (cmd.includes('STATUS_BEGIN')) return ok(STATUS_OK)
  if (cmd.includes('BACKUP_OK')) return ok('BACKUP_OK .dotcraft-backups/20260601-120000')
  if (cmd.includes('--remove-orphans')) return ok('Recreating dotcraft ... done')
  if (cmd.includes(' pull')) return ok('dotcraft Pulling\ndownloaded newer image')
  if (cmd.includes(' restart')) return ok('Restarting')
  if (cmd.includes(' stop')) return ok('Stopping')
  if (cmd.includes('logs --no-color')) return ok('log line one\nlog line two')
  if (cmd.startsWith('cat ')) return ok('the-secret-token\n')
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
      ok('APPSERVER_TOKEN=abcdef123456\nthe secret is mysecretvalue here')
    )
    const mgr = new RemoteServersManager({ runner })
    const result = await mgr.logs(host, stack, undefined, 200, ['mysecretvalue'])
    expect(result.text).toContain('[redacted]')
    expect(result.text).not.toContain('abcdef123456')
    expect(result.text).not.toContain('mysecretvalue')
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
    const { runner, calls } = makeRunner(() => fail('docker compose stop: APPSERVER_TOKEN=leak failed'))
    const mgr = new RemoteServersManager({ runner })
    const result = await mgr.action(host, stack, 'stop')
    expect(result.ok).toBe(false)
    expect(result.status).toBeUndefined()
    expect(result.message).toContain('[redacted]')
    expect(result.message).not.toContain('leak')
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

  it('reads the remote token (trimmed) for connect', async () => {
    const { runner } = makeRunner(route)
    const mgr = new RemoteServersManager({ runner })
    expect(await mgr.readToken(host, stack)).toBe('the-secret-token')
  })
})

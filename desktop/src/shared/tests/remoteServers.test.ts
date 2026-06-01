import { describe, it, expect } from 'vitest'
import {
  DEFAULT_APP_SERVER_PORT,
  DEFAULT_DASHBOARD_PORT,
  MAX_LOG_TAIL,
  REDACTION_MASK,
  normalizeRemoteHosts,
  isValidSshTarget,
  isValidRemotePath,
  isValidIdentityFile,
  isValidServiceName,
  shellSingleQuote,
  quoteRemotePath,
  effectiveWorkspaceDir,
  buildSshArgs,
  composePrefix,
  buildLogsCommand,
  buildStatusCommand,
  buildUpCommand,
  parseStatusOutput,
  parseSshTestOutput,
  updateChangedFromOutput,
  buildTunnelWsUrl,
  buildDashboardUrl,
  redactSecrets,
  type RemoteStack
} from '../remoteServers'

function counterIds(): (prefix: 'h' | 's') => string {
  let n = 0
  return (prefix) => `${prefix}_${++n}`
}

const stack: RemoteStack = {
  id: 's_1',
  name: 'prod',
  composeDir: '~/dotcraft/deploy/docker',
  appServerPort: 9100,
  dashboardPort: 8080,
  sandboxProfile: false
}

describe('validation', () => {
  it('accepts valid ssh targets and rejects option/whitespace injection', () => {
    expect(isValidSshTarget('user@cloud')).toBe(true)
    expect(isValidSshTarget('cloud')).toBe(true)
    expect(isValidSshTarget('deploy@staging.internal')).toBe(true)
    expect(isValidSshTarget('-oProxyCommand=evil')).toBe(false)
    expect(isValidSshTarget('user@host extra')).toBe(false)
    expect(isValidSshTarget('')).toBe(false)
    expect(isValidSshTarget(42 as unknown)).toBe(false)
  })

  it('accepts absolute and home-relative paths, rejects traversal/relative', () => {
    expect(isValidRemotePath('/srv/dotcraft')).toBe(true)
    expect(isValidRemotePath('~')).toBe(true)
    expect(isValidRemotePath('~/dotcraft/deploy')).toBe(true)
    expect(isValidRemotePath('relative/path')).toBe(false)
    expect(isValidRemotePath('~/a/../../etc')).toBe(false)
    expect(isValidRemotePath('/srv/has space')).toBe(false)
    expect(isValidRemotePath('')).toBe(false)
  })

  it('validates identity files and service names', () => {
    expect(isValidIdentityFile('~/.ssh/id_ed25519')).toBe(true)
    expect(isValidIdentityFile('-i')).toBe(false)
    expect(isValidServiceName('opensandbox')).toBe(true)
    expect(isValidServiceName('app server')).toBe(false)
  })
})

describe('shell quoting', () => {
  it('single-quotes literals and escapes embedded quotes', () => {
    expect(shellSingleQuote('plain')).toBe("'plain'")
    expect(shellSingleQuote("a'b")).toBe("'a'\\''b'")
  })

  it('quotes remote paths, leaving ~/ for shell expansion', () => {
    expect(quoteRemotePath('/srv/dotcraft')).toBe("'/srv/dotcraft'")
    expect(quoteRemotePath('~')).toBe('~')
    expect(quoteRemotePath('~/dotcraft/deploy')).toBe("~/'dotcraft/deploy'")
  })

  it('derives the workspace dir from composeDir by default', () => {
    expect(effectiveWorkspaceDir(stack)).toBe('~/dotcraft/deploy/docker/workspace')
    expect(effectiveWorkspaceDir({ ...stack, workspaceDir: '/data/ws' })).toBe('/data/ws')
  })
})

describe('normalizeRemoteHosts', () => {
  it('drops invalid hosts/stacks, defaults ports, fills missing ids, dedups', () => {
    const hosts = normalizeRemoteHosts(
      [
        {
          name: 'Cloud',
          sshTarget: 'user@cloud',
          stacks: [
            { name: 'prod', composeDir: '~/dotcraft/deploy/docker' },
            { name: 'bad', composeDir: 'relative' }, // dropped: invalid path
            { id: 's_x', name: 'sandbox', composeDir: '/srv/sb', sandboxProfile: true, appServerPort: 70000 }
          ]
        },
        { name: 'NoTarget', sshTarget: '-bad' }, // dropped: invalid target
        'garbage'
      ],
      counterIds()
    )

    expect(hosts).toHaveLength(1)
    const host = hosts[0]
    expect(host.id).toBe('h_1')
    expect(host.sshTarget).toBe('user@cloud')
    expect(host.stacks).toHaveLength(2)

    const prod = host.stacks[0]
    expect(prod.id).toBe('s_2') // generated
    expect(prod.appServerPort).toBe(DEFAULT_APP_SERVER_PORT)
    expect(prod.dashboardPort).toBe(DEFAULT_DASHBOARD_PORT)
    expect(prod.sandboxProfile).toBe(false)

    const sb = host.stacks[1]
    expect(sb.id).toBe('s_x') // preserved
    expect(sb.sandboxProfile).toBe(true)
    expect(sb.appServerPort).toBe(DEFAULT_APP_SERVER_PORT) // 70000 invalid → default
  })

  it('returns [] for non-array input', () => {
    expect(normalizeRemoteHosts(undefined)).toEqual([])
    expect(normalizeRemoteHosts({})).toEqual([])
  })
})

describe('ssh argv', () => {
  it('uses BatchMode, bounded timeout, and -- before the target', () => {
    const args = buildSshArgs({ id: 'h', name: 'C', sshTarget: 'user@cloud', stacks: [] }, 'echo hi')
    expect(args).toContain('BatchMode=yes')
    expect(args.join(' ')).toContain('ConnectTimeout=')
    const dd = args.indexOf('--')
    expect(dd).toBeGreaterThan(-1)
    expect(args[dd + 1]).toBe('user@cloud') // target can never be read as an option
    expect(args[dd + 2]).toBe('echo hi')
  })

  it('adds -i with IdentitiesOnly when an identity file is set', () => {
    const args = buildSshArgs(
      { id: 'h', name: 'C', sshTarget: 'cloud', identityFile: '~/.ssh/id_ed25519', stacks: [] },
      'true'
    )
    expect(args).toContain('-i')
    expect(args).toContain('~/.ssh/id_ed25519')
    expect(args).toContain('IdentitiesOnly=yes')
  })
})

describe('compose command builders', () => {
  it('builds the compose prefix with project + sandbox profile', () => {
    expect(composePrefix(stack)).toBe('docker compose')
    expect(composePrefix({ ...stack, projectName: 'dotcraft', sandboxProfile: true })).toBe(
      "docker compose -p 'dotcraft' --profile sandbox"
    )
  })

  it('clamps log tail and only includes a valid service filter', () => {
    expect(buildLogsCommand(stack, undefined, 99999)).toContain(`--tail ${MAX_LOG_TAIL}`)
    expect(buildLogsCommand(stack, 'app server')).not.toContain('app server') // invalid → dropped
    expect(buildLogsCommand(stack, 'opensandbox')).toContain("'opensandbox'")
  })

  it('status command carries markers and the quoted compose dir', () => {
    const cmd = buildStatusCommand(stack)
    expect(cmd).toContain('STATUS_BEGIN')
    expect(cmd).toContain('ps -a --format json')
    expect(cmd).toContain("~/'dotcraft/deploy/docker'")
  })

  it('up command recreates while preserving volumes', () => {
    expect(buildUpCommand(stack)).toContain('up -d --remove-orphans')
  })
})

describe('parseStatusOutput', () => {
  const wrap = (ps: string) =>
    `STATUS_BEGIN\ndocker=ok\ncompose=ok\nenv=ok\nconfig=ok\ntoken=present\nPS_BEGIN\n${ps}\nPS_END\nSTATUS_END`

  it('parses a healthy single-service stack (NDJSON)', () => {
    const out = parseStatusOutput(
      wrap('{"Service":"dotcraft","State":"running","Health":"healthy","Image":"ghcr.io/dotharness/dotcraft:1.4.2"}'),
      's_1'
    )
    expect(out.health).toBe('running')
    expect(out.dockerOk && out.composeOk && out.envOk && out.configOk).toBe(true)
    expect(out.tokenPresent).toBe(true)
    expect(out.servicesUp).toBe(1)
    expect(out.servicesTotal).toBe(1)
    expect(out.imageTag).toBe('1.4.2')
  })

  it('derives partial when not all services are up (JSON array)', () => {
    const out = parseStatusOutput(
      wrap('[{"Service":"dotcraft","State":"running"},{"Service":"opensandbox","State":"exited"}]'),
      's_1'
    )
    expect(out.health).toBe('partial')
    expect(out.servicesUp).toBe(1)
    expect(out.servicesTotal).toBe(2)
  })

  it('derives unhealthy when a service reports unhealthy', () => {
    const out = parseStatusOutput(wrap('{"Service":"dotcraft","State":"running","Health":"unhealthy"}'), 's_1')
    expect(out.health).toBe('unhealthy')
  })

  it('derives stopped when nothing is running', () => {
    const out = parseStatusOutput(wrap('{"Service":"dotcraft","State":"exited"}'), 's_1')
    expect(out.health).toBe('stopped')
  })

  it('returns unknown + error on DIR_MISSING', () => {
    const out = parseStatusOutput('DIR_MISSING', 's_1')
    expect(out.health).toBe('unknown')
    expect(out.error).toBeTruthy()
  })

  it('returns unknown when docker is missing', () => {
    const out = parseStatusOutput(
      'STATUS_BEGIN\ndocker=missing\ncompose=missing\nenv=missing\nconfig=missing\ntoken=missing\nPS_BEGIN\nPS_END\nSTATUS_END',
      's_1'
    )
    expect(out.health).toBe('unknown')
    expect(out.tokenPresent).toBe(false)
  })
})

describe('ssh test + update parsing', () => {
  it('parses a reachable test with docker/compose detected', () => {
    const r = parseSshTestOutput('SSH_OK\ndocker=ok\ncompose=ok', 38)
    expect(r.reachable).toBe(true)
    expect(r.dockerOk).toBe(true)
    expect(r.composeOk).toBe(true)
    expect(r.latencyMs).toBe(38)
  })

  it('parses an unreachable test', () => {
    const r = parseSshTestOutput('ssh: connect to host cloud port 22: Connection timed out')
    expect(r.reachable).toBe(false)
    expect(r.errorCode).toBe('unreachable')
  })

  it('detects changed vs up-to-date updates', () => {
    expect(updateChangedFromOutput('Pulling dotcraft ... downloaded newer image', 'Recreating dotcraft')).toBe(true)
    expect(updateChangedFromOutput('dotcraft Pulled', 'Container dotcraft Running')).toBe(false)
  })
})

describe('tunnel urls', () => {
  it('builds ws and dashboard local urls', () => {
    expect(buildTunnelWsUrl(51823)).toBe('ws://127.0.0.1:51823/ws')
    expect(buildTunnelWsUrl(51823, 'abc')).toBe('ws://127.0.0.1:51823/ws?token=abc')
    expect(buildDashboardUrl(52001)).toBe('http://127.0.0.1:52001/dashboard')
  })
})

describe('redactSecrets', () => {
  it('masks explicit secrets, secret-like assignments, and token query params', () => {
    expect(redactSecrets('the token is supersecretvalue here', ['supersecretvalue'])).toBe(
      `the token is ${REDACTION_MASK} here`
    )
    expect(redactSecrets('APPSERVER_TOKEN=abc123def')).toBe(`APPSERVER_TOKEN=${REDACTION_MASK}`)
    expect(redactSecrets('FEISHU_APP_SECRET: shhhh')).toBe(`FEISHU_APP_SECRET: ${REDACTION_MASK}`)
    expect(redactSecrets('ws://127.0.0.1:9100/ws?token=abc123')).toBe(
      `ws://127.0.0.1:9100/ws?token=${REDACTION_MASK}`
    )
  })

  it('leaves ordinary text and short values untouched', () => {
    expect(redactSecrets('Linked to this Desktop · Dashboard ready')).toBe(
      'Linked to this Desktop · Dashboard ready'
    )
    expect(redactSecrets('docker=ok\ncompose=ok')).toBe('docker=ok\ncompose=ok')
  })
})

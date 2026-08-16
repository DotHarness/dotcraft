import { describe, it, expect } from 'vitest'
import {
  DEFAULT_APP_SERVER_PORT,
  DEFAULT_DASHBOARD_PORT,
  DEFAULT_ORATORIO_PORT,
  MAX_LOG_TAIL,
  REDACTION_MASK,
  normalizeRemoteHosts,
  isValidSshTarget,
  isValidRemotePath,
  isValidIdentityFile,
  isValidServiceName,
  isValidComposeProjectName,
  shellSingleQuote,
  quoteRemotePath,
  effectiveAppServerWorkspacePath,
  effectiveWorkspaceDir,
  buildSshArgs,
  composePrefix,
  buildDiscoverStacksCommand,
  buildLogsCommand,
  buildReadCoreConfigCommand,
  buildReadOratorioTokenCommand,
  buildStatusCommand,
  buildUpCommand,
  parseStatusOutput,
  parseSshTestOutput,
  parseDiscoverStacksOutput,
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
  composeDir: '~/sample-stack/docker',
  appServerPort: 9100,
  oratorioPort: 5087,
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
    expect(isValidRemotePath('/srv/sample')).toBe(true)
    expect(isValidRemotePath('~')).toBe(true)
    expect(isValidRemotePath('~/sample-stack/deploy')).toBe(true)
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

  it('accepts only Docker Compose project identifiers', () => {
    expect(isValidComposeProjectName('dotcraft-stack')).toBe(true)
    expect(isValidComposeProjectName('deploy_2')).toBe(true)
    expect(isValidComposeProjectName('Oratorio Cloud')).toBe(false)
    expect(isValidComposeProjectName('-deploy')).toBe(false)
  })
})

describe('shell quoting', () => {
  it('single-quotes literals and escapes embedded quotes', () => {
    expect(shellSingleQuote('plain')).toBe("'plain'")
    expect(shellSingleQuote("a'b")).toBe("'a'\\''b'")
  })

  it('quotes remote paths, leaving ~/ for shell expansion', () => {
    expect(quoteRemotePath('/srv/sample')).toBe("'/srv/sample'")
    expect(quoteRemotePath('~')).toBe('~')
    expect(quoteRemotePath('~/sample-stack/deploy')).toBe("~/'sample-stack/deploy'")
  })

  it('derives the workspace dir from composeDir by default', () => {
    expect(effectiveWorkspaceDir(stack)).toBe('~/sample-stack/docker/workspace')
    expect(effectiveWorkspaceDir({ ...stack, workspaceDir: '/data/ws' })).toBe('/data/ws')
  })

  it('uses /workspace as the default AppServer protocol workspace path', () => {
    expect(effectiveAppServerWorkspacePath(stack)).toBe('/workspace')
    expect(effectiveAppServerWorkspacePath({ ...stack, appServerWorkspacePath: '/app/workspace' })).toBe('/app/workspace')
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
            { name: 'prod', composeDir: '~/sample-stack/docker' },
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
    expect(prod.oratorioPort).toBe(DEFAULT_ORATORIO_PORT)
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
    expect(args).not.toContain('-i')
    expect(args).not.toContain('IdentitiesOnly=yes')
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
    expect(composePrefix({ ...stack, composeProjectName: 'sample-project', sandboxProfile: true })).toBe(
      "docker compose -p 'sample-project' --profile sandbox"
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
    expect(cmd).toContain('LOCK_BEGIN')
    expect(cmd).toContain('.craft/appserver.lock')
    expect(cmd).toContain('ps -a --format json')
    expect(cmd).toContain("~/'sample-stack/docker'")
  })

  it('core config read command only reads stack workspace and user config paths', () => {
    const cmd = buildReadCoreConfigCommand({ ...stack, workspaceDir: '/srv/sample/workspace' })
    expect(cmd).toContain('CONFIG_BEGIN')
    expect(cmd).toContain("'/srv/sample/workspace/.craft/config.json'")
    expect(cmd).toContain("~/'.craft/config.json'")
    expect(cmd).toContain('base64')
  })

  it('reads only the Oratorio token from the stack environment', () => {
    const cmd = buildReadOratorioTokenCommand(stack)
    expect(cmd).toContain("$1==\"ORATORIO_SERVICE_TOKEN\"")
    expect(cmd).toContain('.env')
    expect(cmd).not.toContain('APPSERVER_TOKEN')
  })

  it('discovery command uses Docker labels and inspect JSON only', () => {
    const cmd = buildDiscoverStacksCommand()
    expect(cmd).toContain('DISCOVER_BEGIN')
    expect(cmd).toContain("label=com.docker.compose.project")
    expect(cmd).toContain('{{json .}}')
  })

  it('up command recreates while preserving volumes', () => {
    expect(buildUpCommand(stack)).toContain('up -d --remove-orphans')
  })
})

describe('parseStatusOutput', () => {
  const wrap = (ps: string, lock = '') =>
    `STATUS_BEGIN\ndocker=ok\ncompose=ok\nenv=ok\nconfig=ok\ntoken=present\nLOCK_BEGIN\n${lock}\nLOCK_END\nPS_BEGIN\n${ps}\nPS_END\nSTATUS_END`

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

  it('reads the AppServer runtime version from the lock file block', () => {
    const out = parseStatusOutput(
      wrap(
        '{"Service":"dotcraft","State":"running","Image":"ghcr.io/dotharness/dotcraft:latest"}',
        '{"Version":"0.2.3+abc","Endpoints":{"appServerWebSocket":"ws://127.0.0.1:9100/ws?token=secret"}}'
      ),
      's_1'
    )
    expect(out.appVersion).toBe('0.2.3+abc')
    expect(out.imageTag).toBe('latest')
  })

  it('leaves appVersion empty when the lock file is missing or invalid', () => {
    expect(parseStatusOutput(wrap('{"Service":"dotcraft","State":"running"}'), 's_1').appVersion).toBeUndefined()
    expect(parseStatusOutput(wrap('{"Service":"dotcraft","State":"running"}', '{not-json'), 's_1').appVersion).toBeUndefined()
    expect(parseStatusOutput(wrap('{"Service":"dotcraft","State":"running"}', '{"pid":123}'), 's_1').appVersion).toBeUndefined()
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

describe('parseDiscoverStacksOutput', () => {
  it('discovers DotCraft compose projects from docker inspect output', () => {
    const dotcraft = {
      Config: {
        Image: 'ghcr.io/dotharness/dotcraft:latest',
        Labels: {
          'com.docker.compose.project': 'deploy',
          'com.docker.compose.service': 'dotcraft',
          'com.docker.compose.project.working_dir': '/srv/sample/demo-stack/docker',
          'com.docker.compose.project.config_files': '/srv/sample/demo-stack/docker/docker-compose.yml'
        },
        Env: ['DOTCRAFT_PROVIDER=openai']
      },
      Mounts: [{ Source: '/srv/sample/demo-stack/docker/workspace', Destination: '/workspace' }],
      NetworkSettings: {
        Ports: {
          '9100/tcp': [{ HostIp: '127.0.0.1', HostPort: '9100' }],
          '8080/tcp': [{ HostIp: '127.0.0.1', HostPort: '18080' }]
        }
      }
    }
    const sandbox = {
      Config: {
        Image: 'ghcr.io/open-webui/open-webui:latest',
        Labels: {
          'com.docker.compose.project': 'deploy',
          'com.docker.compose.service': 'opensandbox',
          'com.docker.compose.project.working_dir': '/srv/sample/demo-stack/docker'
        }
      }
    }

    const stacks = parseDiscoverStacksOutput(
      `DISCOVER_BEGIN\n${JSON.stringify(dotcraft)}\n${JSON.stringify(sandbox)}\nDISCOVER_END`
    )

    expect(stacks).toHaveLength(1)
    expect(stacks[0]).toMatchObject({
      name: 'demo-stack',
      composeDir: '/srv/sample/demo-stack/docker',
      workspaceDir: '/srv/sample/demo-stack/docker/workspace',
      appServerWorkspacePath: '/workspace',
      composeProjectName: 'deploy',
      appServerPort: 9100,
      dashboardPort: 18080,
      sandboxProfile: true,
      hasSandbox: true,
      image: 'ghcr.io/dotharness/dotcraft:latest'
    })
    expect(stacks[0].services).toEqual(['dotcraft', 'opensandbox'])
  })

  it('ignores non-DotCraft compose projects and malformed JSON', () => {
    const other = {
      Config: {
        Image: 'postgres:16',
        Labels: {
          'com.docker.compose.project': 'db',
          'com.docker.compose.service': 'postgres',
          'com.docker.compose.project.working_dir': '/srv/db'
        }
      }
    }

    expect(parseDiscoverStacksOutput(`DISCOVER_BEGIN\nnot-json\n${JSON.stringify(other)}\nDISCOVER_END`)).toEqual([])
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
    expect(redactSecrets('the token is fixture-explicit-value here', ['fixture-explicit-value'])).toBe(
      `the token is ${REDACTION_MASK} here`
    )
    expect(redactSecrets('APPSERVER_TOKEN=fixture-appserver-token')).toBe(`APPSERVER_TOKEN=${REDACTION_MASK}`)
    expect(redactSecrets('FEISHU_APP_SECRET: fixture-secret')).toBe(`FEISHU_APP_SECRET: ${REDACTION_MASK}`)
    expect(redactSecrets('ws://127.0.0.1:9100/ws?token=fixture-query-token')).toBe(
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

import { describe, it, expect } from 'vitest'
import {
  DEFAULT_APP_SERVER_PORT,
  DEFAULT_DASHBOARD_PORT,
  DEFAULT_ORATORIO_PORT,
  MAX_LOG_TAIL,
  normalizeRemoteStacks,
  isValidServiceName,
  isValidComposeProjectName,
  effectiveAppServerWorkspacePath,
  effectiveWorkspaceDir,
  composePrefix,
  buildLogsCommand,
  buildReadOratorioTokenCommand,
  buildStatusCommand,
  parseStatusOutput,
  parseDiscoverStacksOutput,
  updateChangedFromOutput,
  buildTunnelWsUrl,
  buildDashboardUrl,
  type RemoteStack
} from '../dockerDeployments'

function counterIds(): (prefix: string) => string {
  let n = 0
  return (prefix) => `${prefix}_${++n}`
}

const stack: RemoteStack = {
  id: 's_1',
  name: 'prod',
  composeDir: '~/sample-stack/docker',
  appServerPort: 9100,
  oratorioPort: 5087,
  dashboardPort: 8080
}

describe('validation and paths', () => {
  it('accepts only valid service names and Docker Compose project identifiers', () => {
    expect(isValidServiceName('oratorio')).toBe(true)
    expect(isValidServiceName('app server')).toBe(false)
    expect(isValidComposeProjectName('dotcraft-stack')).toBe(true)
    expect(isValidComposeProjectName('deploy_2')).toBe(true)
    expect(isValidComposeProjectName('Oratorio Cloud')).toBe(false)
    expect(isValidComposeProjectName('-deploy')).toBe(false)
  })

  it('derives workspace dir from composeDir and defaults the AppServer path to /workspace', () => {
    expect(effectiveWorkspaceDir(stack)).toBe('~/sample-stack/docker/workspace')
    expect(effectiveWorkspaceDir({ ...stack, workspaceDir: '/data/ws' })).toBe('/data/ws')
    expect(effectiveAppServerWorkspacePath(stack)).toBe('/workspace')
    expect(effectiveAppServerWorkspacePath({ ...stack, appServerWorkspacePath: '/app/workspace' })).toBe('/app/workspace')
  })
})

describe('normalizeRemoteStacks', () => {
  it('drops invalid stacks, defaults ports, fills missing ids, dedups', () => {
    const stacks = normalizeRemoteStacks(
      [
        { name: 'prod', composeDir: '~/sample-stack/docker' },
        { name: 'bad', composeDir: 'relative' },
        { id: 's_x', name: 'secondary', composeDir: '/srv/secondary', appServerPort: 70000 },
        { id: 's_x', name: 'duplicate', composeDir: '/srv/dup' },
        'garbage'
      ],
      counterIds()
    )

    expect(stacks.map((s) => s.id)).toEqual(['s_1', 's_x'])
    expect(stacks[0].appServerPort).toBe(DEFAULT_APP_SERVER_PORT)
    expect(stacks[0].oratorioPort).toBe(DEFAULT_ORATORIO_PORT)
    expect(stacks[0].dashboardPort).toBe(DEFAULT_DASHBOARD_PORT)
    expect(stacks[1].appServerPort).toBe(DEFAULT_APP_SERVER_PORT)
    expect(normalizeRemoteStacks(undefined, counterIds())).toEqual([])
    expect(normalizeRemoteStacks({}, counterIds())).toEqual([])
  })
})

describe('compose command builders', () => {
  it('quotes the compose project and clamps logs to a valid service filter', () => {
    expect(composePrefix(stack)).toBe('docker compose')
    expect(composePrefix({ ...stack, composeProjectName: 'sample-project' })).toBe(
      "docker compose -p 'sample-project'"
    )
    expect(buildLogsCommand(stack, undefined, 99999)).toContain(`--tail ${MAX_LOG_TAIL}`)
    expect(buildLogsCommand(stack, 'app server')).not.toContain('app server')
    expect(buildLogsCommand(stack, 'oratorio')).toContain("'oratorio'")
  })

  it('status command carries markers and the quoted compose dir', () => {
    const cmd = buildStatusCommand(stack)
    expect(cmd).toContain('STATUS_BEGIN')
    expect(cmd).toContain('LOCK_BEGIN')
    expect(cmd).toContain('.craft/appserver.lock')
    expect(cmd).toContain('ps -a --format json')
    expect(cmd).toContain("~/'sample-stack/docker'")
  })

  it('reads only the Oratorio token from the stack environment', () => {
    const cmd = buildReadOratorioTokenCommand(stack)
    expect(cmd).toContain("$1==\"ORATORIO_SERVICE_TOKEN\"")
    expect(cmd).toContain('.env')
    expect(cmd).not.toContain('APPSERVER_TOKEN')
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

  it('reads the AppServer runtime version from a valid lock file block only', () => {
    const out = parseStatusOutput(
      wrap(
        '{"Service":"dotcraft","State":"running","Image":"ghcr.io/dotharness/dotcraft:latest"}',
        '{"Version":"0.2.3+abc","Endpoints":{"appServerWebSocket":"ws://127.0.0.1:9100/ws?token=secret"}}'
      ),
      's_1'
    )
    expect(out.appVersion).toBe('0.2.3+abc')
    expect(out.imageTag).toBe('latest')
    for (const lock of ['', '{not-json', '{"pid":123}']) {
      expect(parseStatusOutput(wrap('{"Service":"dotcraft","State":"running"}', lock), 's_1').appVersion).toBeUndefined()
    }
  })

  it('derives partial, unhealthy, and stopped health from service states', () => {
    const partial = parseStatusOutput(
      wrap('[{"Service":"dotcraft","State":"running"},{"Service":"oratorio","State":"exited"}]'),
      's_1'
    )
    expect(partial).toMatchObject({ health: 'partial', servicesUp: 1, servicesTotal: 2 })
    expect(parseStatusOutput(wrap('{"Service":"dotcraft","State":"running","Health":"unhealthy"}'), 's_1').health).toBe('unhealthy')
    expect(parseStatusOutput(wrap('{"Service":"dotcraft","State":"exited"}'), 's_1').health).toBe('stopped')
  })

  it('returns unknown when the compose dir or docker is missing', () => {
    const dirMissing = parseStatusOutput('DIR_MISSING', 's_1')
    expect(dirMissing.health).toBe('unknown')
    expect(dirMissing.error).toBeTruthy()

    const out = parseStatusOutput(
      'STATUS_BEGIN\ndocker=missing\ncompose=missing\nenv=missing\nconfig=missing\ntoken=missing\nPS_BEGIN\nPS_END\nSTATUS_END',
      's_1'
    )
    expect(out.health).toBe('unknown')
    expect(out.tokenPresent).toBe(false)
  })
})

describe('update parsing', () => {
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

    const stacks = parseDiscoverStacksOutput(
      `DISCOVER_BEGIN\n${JSON.stringify(dotcraft)}\nDISCOVER_END`
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
      image: 'ghcr.io/dotharness/dotcraft:latest'
    })
    expect(stacks[0].services).toEqual(['dotcraft'])
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

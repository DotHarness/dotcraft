import { describe, expect, it } from 'vitest'
import {
  buildEnsureProjectWorkspaceCommand,
  buildInstallCommand,
  buildListFoldersCommand,
  buildProbeCommand,
  buildReadConfigFilesCommand,
  buildStartHubCommand,
  classifyProbe,
  compareVersions,
  eventEndpointPort,
  forwardedEndpointUrl,
  hasUsableModel,
  isEndpointChangeEvent,
  minimumDotCraftVersion,
  missingInstallTools,
  parseConfigFilesOutput,
  parseDotCraftVersion,
  parseFolderListing,
  parseHubLock,
  parseLoopbackEndpoint,
  parseProbeOutput,
  resolveEffectiveProviderId,
  resolveInstallVersion,
  type MachineProbe
} from '../sshMachineRemote'

function probe(overrides: Partial<MachineProbe> = {}): MachineProbe {
  return {
    os: 'Linux',
    arch: 'x86_64',
    dotcraftPresent: true,
    dotcraftVersion: '0.7.10',
    tools: { docker: false, bash: true, curl: true, tar: true },
    ...overrides
  }
}

function base64(text: string): string {
  return Buffer.from(text, 'utf8').toString('base64')
}

describe('probe', () => {
  it('uses the absolute DotCraft path and reports tools in one round trip', () => {
    const cmd = buildProbeCommand()
    expect(cmd).toContain('"$HOME/.craft/bin/dotcraft" --version')
    expect(cmd).toContain('uname -s')
    expect(cmd).toContain('/etc/os-release')
    expect(cmd).toContain('sw_vers')
    expect(cmd).toContain('for t in docker bash curl tar setsid')
  })

  it('parses a Linux probe', () => {
    const result = parseProbeOutput(
      'DOTCRAFT_PROBE_BEGIN\nos=Linux\narch=x86_64\nhome=/home/dev\nosName=Ubuntu\nosVersion=24.04\ndotcraftPresent=1\ndotcraft=0.7.10+3f2a1b\ntool_docker=1\ntool_bash=1\ntool_curl=0\ntool_tar=1\ntool_setsid=1\nDOTCRAFT_PROBE_END\n'
    )
    expect(result).toEqual({
      kind: 'probe',
      probe: {
        os: 'Linux',
        arch: 'x86_64',
        home: '/home/dev',
        osName: 'Ubuntu',
        osVersion: '24.04',
        dotcraftPresent: true,
        dotcraftVersion: '0.7.10',
        tools: { docker: true, bash: true, curl: false, tar: true, setsid: true }
      }
    })
  })

  it('detects Windows from uname or from a Windows command shell rejecting the probe', () => {
    expect(parseProbeOutput('DOTCRAFT_PROBE_BEGIN\nos=MINGW64_NT-10.0\narch=x86_64\nDOTCRAFT_PROBE_END').kind).toBe('windows')
    expect(parseProbeOutput('DOTCRAFT_PROBE_BEGIN;', "'printf' is not recognized as an internal or external command,").kind).toBe('windows')
    expect(parseProbeOutput('', "printf : The term 'printf' is not recognized as the name of a cmdlet").kind).toBe('windows')
    expect(parseProbeOutput('', 'ssh: Could not resolve hostname box').kind).toBe('invalid')
  })
})

describe('classifyProbe', () => {
  it('reports unsupported systems', () => {
    expect(classifyProbe(probe({ os: 'Windows_NT' }))).toMatchObject({ kind: 'unsupported', unsupported: 'windows' })
    expect(classifyProbe(probe({ arch: 'aarch64' }))).toMatchObject({ kind: 'unsupported', unsupported: 'linuxArm' })
    expect(classifyProbe(probe({ arch: 'armv7l' }))).toMatchObject({ kind: 'unsupported', unsupported: 'linuxArm' })
    expect(classifyProbe(probe({ os: 'FreeBSD', arch: 'amd64' }))).toEqual({
      kind: 'unsupported',
      unsupported: 'other',
      system: 'FreeBSD amd64'
    })
  })

  it('accepts Linux x64 and macOS x64 or arm64', () => {
    expect(classifyProbe(probe())).toBeNull()
    expect(classifyProbe(probe({ os: 'Darwin', arch: 'arm64' }))).toBeNull()
    expect(classifyProbe(probe({ os: 'Darwin', arch: 'x86_64' }))).toBeNull()
  })

  it('reports a missing or outdated DotCraft and accepts newer versions', () => {
    expect(classifyProbe(probe({ dotcraftPresent: false, dotcraftVersion: undefined }))).toEqual({ kind: 'notInstalled' })
    expect(classifyProbe(probe({ dotcraftVersion: '0.7.9' }), '0.7.10')).toEqual({ kind: 'updateRequired', installedVersion: '0.7.9' })
    expect(classifyProbe(probe({ dotcraftVersion: undefined }), '0.7.10')).toEqual({ kind: 'updateRequired', installedVersion: '' })
    expect(classifyProbe(probe({ dotcraftVersion: '0.8.0' }), '0.7.10')).toBeNull()
  })

  it('lists missing installer tools', () => {
    expect(missingInstallTools(probe({ tools: { bash: true, curl: false } }))).toEqual(['curl', 'tar'])
  })
})

describe('versions', () => {
  it('parses and compares release versions', () => {
    expect(parseDotCraftVersion('DotCraft 0.7.10+abc')).toBe('0.7.10')
    expect(parseDotCraftVersion('0.8.0-beta.1')).toBe('0.8.0-beta.1')
    expect(parseDotCraftVersion('nothing')).toBeUndefined()
    expect(compareVersions('0.7.9', '0.7.10')).toBe(-1)
    expect(compareVersions('0.7.10', '0.7.10-beta')).toBe(1)
    expect(compareVersions('1.0.0', '0.9.9')).toBe(1)
  })

  it('installs the Desktop release tag, or latest for development builds', () => {
    expect(resolveInstallVersion('0.7.10', true)).toBe('v0.7.10')
    expect(resolveInstallVersion('0.7.10', false)).toBe('latest')
    expect(minimumDotCraftVersion('0.7.10', true)).toBe('0.7.10')
    expect(minimumDotCraftVersion('0.7.10', false)).toBeUndefined()
    expect(buildInstallCommand("v0.7.10")).toBe("DOTCRAFT_VERSION='v0.7.10' bash -s")
  })
})

describe('remote hub', () => {
  it('starts the Hub detached with setsid or nohup and appends output to hub.out', () => {
    const cmd = buildStartHubCommand()
    expect(cmd).toContain('setsid "$HOME/.craft/bin/dotcraft" hub </dev/null >>"$HOME/.craft/hub"/hub.out 2>&1 &')
    expect(cmd).toContain('nohup "$HOME/.craft/bin/dotcraft" hub </dev/null')
  })

  it('parses hub.lock and accepts only loopback http URLs', () => {
    const lock = { pid: 4242, apiBaseUrl: 'http://127.0.0.1:49152', token: 'hub-secret-token', startedAt: 'x', version: '0.7.10' }
    expect(parseHubLock(`HUB_LOCK_BEGIN\n${JSON.stringify(lock)}\nHUB_LOCK_END`)).toEqual({
      pid: 4242,
      apiBaseUrl: 'http://127.0.0.1:49152',
      port: 49152,
      token: 'hub-secret-token',
      version: '0.7.10'
    })
    expect(parseHubLock('HUB_LOCK_BEGIN\n\nHUB_LOCK_END')).toBeNull()
    expect(parseHubLock(JSON.stringify({ ...lock, apiBaseUrl: 'http://0.0.0.0:49152' }))).toBeNull()
    expect(parseHubLock(JSON.stringify({ ...lock, token: '' }))).toBeNull()
    expect(parseHubLock(JSON.stringify({ Pid: 1, ApiBaseUrl: 'http://localhost:5000', Token: 't' }))?.port).toBe(5000)
  })

  it('parses AppServer endpoints and rewrites them onto the forward', () => {
    expect(parseLoopbackEndpoint('ws://127.0.0.1:41234/ws?token=abc')).toEqual({ port: 41234, token: 'abc' })
    expect(parseLoopbackEndpoint('ws://10.0.0.1:41234/ws')).toBeNull()
    expect(parseLoopbackEndpoint('http://127.0.0.1:41234/ws')).toBeNull()
    expect(forwardedEndpointUrl('ws://127.0.0.1:41234/ws?token=abc', 55000)).toBe('ws://127.0.0.1:55000/ws?token=abc')
  })

  it('follows AppServer port changes for the project path only', () => {
    const running = { kind: 'appserver.running', workspacePath: '/home/dev/src', data: { endpoints: { appServerWebSocket: 'ws://127.0.0.1:41000/ws?token=t' } } }
    expect(isEndpointChangeEvent(running, '/home/dev/src/')).toBe(true)
    expect(eventEndpointPort(running)).toBe(41000)
    const allocated = { kind: 'port.allocated', workspacePath: '/home/dev/src', data: { service: 'appServerWebSocket', port: 41001 } }
    expect(isEndpointChangeEvent(allocated, '/home/dev/src')).toBe(true)
    expect(eventEndpointPort(allocated)).toBe(41001)
    expect(isEndpointChangeEvent({ ...allocated, data: { service: 'dashboard', port: 1 } }, '/home/dev/src')).toBe(false)
    expect(isEndpointChangeEvent({ ...running, workspacePath: '/other' }, '/home/dev/src')).toBe(false)
    expect(isEndpointChangeEvent({ kind: 'appserver.stopped', workspacePath: '/home/dev/src' }, '/home/dev/src')).toBe(false)
  })
})

describe('remote folders', () => {
  it('lists folders with a quoted path and resolves ~ on the machine', () => {
    expect(buildListFoldersCommand("/srv/it's")).toContain("cd -- '/srv/it'\\''s'")
    expect(buildListFoldersCommand('~')).toContain('cd -- ~ ')
    expect(() => buildListFoldersCommand('/srv/../etc')).toThrow()
  })

  it('parses directories only, excludes hidden ones, and marks saved projects', () => {
    const listing = parseFolderListing(
      'cwd=/home/dev\nhome=/home/dev\nFOLDERS_BEGIN\nsrc/\n.cache/\nmy notes/\nFOLDERS_END\n',
      ['/home/dev/src']
    )
    expect(listing).toEqual({
      path: '/home/dev',
      home: '/home/dev',
      folders: [
        { name: 'my notes', path: '/home/dev/my notes', saved: false },
        { name: 'src', path: '/home/dev/src', saved: true }
      ]
    })
    expect(parseFolderListing('FOLDERS_MISSING\n')).toBeNull()
    expect(parseFolderListing('cwd=/\nhome=/root\nFOLDERS_BEGIN\nusr/\nFOLDERS_END')?.folders[0].path).toBe('/usr')
  })

  it('creates .craft only inside an existing absolute folder', () => {
    expect(buildEnsureProjectWorkspaceCommand('/srv/my app')).toBe(
      "if [ -d '/srv/my app' ]; then mkdir -p -- '/srv/my app/.craft' && echo WORKSPACE_READY; else echo WORKSPACE_MISSING; fi"
    )
    expect(() => buildEnsureProjectWorkspaceCommand('~/src')).toThrow()
  })
})

describe('model readiness', () => {
  it('reads workspace and user config over ssh', () => {
    const cmd = buildReadConfigFilesCommand('/home/dev/src/.craft/config.json')
    expect(cmd).toContain("'/home/dev/src/.craft/config.json'")
    expect(cmd).toContain("~/'.craft/config.json'")
    const parsed = parseConfigFilesOutput(
      `CONFIG_BEGIN\nworkspace=${base64('{"ProviderId":"anthropic"}')}\nuserDefaults=\nCONFIG_END\n`
    )
    expect(parsed).toEqual({ workspaceRaw: '{"ProviderId":"anthropic"}', userDefaultsRaw: '' })
    expect(parseConfigFilesOutput('ssh error')).toBeNull()
  })

  it('resolves the effective provider from workspace then user config', () => {
    expect(resolveEffectiveProviderId('{"providerId":"ws"}', '{"ProviderId":"user"}')).toBe('ws')
    expect(resolveEffectiveProviderId('{}', '{"ProviderId":"user"}')).toBe('user')
    expect(resolveEffectiveProviderId('', 'not json')).toBe('')
  })

  it('requires the selected provider to be listed with an available credential', () => {
    const list = {
      providers: [
        { id: 'anthropic', hasApiKey: true },
        { id: 'empty', hasApiKey: false },
        { id: 'oauth', authMethod: 'chatgptOAuth', chatGptAccountId: 'acct' }
      ]
    }
    expect(hasUsableModel(list, 'Anthropic')).toBe(true)
    expect(hasUsableModel(list, 'empty')).toBe(false)
    expect(hasUsableModel(list, 'oauth')).toBe(true)
    expect(hasUsableModel(list, 'missing')).toBe(false)
    expect(hasUsableModel(list, '')).toBe(false)
    expect(hasUsableModel({ managedBy: 'modelService', providers: [{ id: 'svc', hasApiKey: true, isAuthenticated: false }] }, 'svc')).toBe(false)
    expect(hasUsableModel({ managedBy: 'modelService', providers: [{ id: 'svc', isAuthenticated: true }] }, 'svc')).toBe(true)
  })
})

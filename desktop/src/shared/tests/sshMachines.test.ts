import { describe, expect, it } from 'vitest'
import {
  applyMachineEdit,
  buildSshArgs,
  buildSshResolveArgs,
  buildSshTunnelArgs,
  createMachineFromEntry,
  formatResolvedHost,
  isValidIdentityFile,
  isValidSshTarget,
  machineSshTarget,
  normalizeSshMachines,
  parseSshResolveOutput,
  validateMachineEntry,
  type SshMachine
} from '../sshMachines'
import {
  REDACTION_MASK,
  isValidRemoteFolderPath,
  isValidRemotePath,
  quoteRemotePath,
  redactSecrets,
  shellSingleQuote
} from '../sshShell'

function counterIds(): (prefix: string) => string {
  let n = 0
  return (prefix) => `${prefix}_${++n}`
}

function machine(overrides: Partial<SshMachine> = {}): SshMachine {
  return {
    id: 'm_1',
    name: 'build-box',
    source: 'manual',
    hostname: 'dev@10.0.0.12',
    autoConnect: true,
    projects: [],
    stacks: [],
    ...overrides
  }
}

describe('normalizeSshMachines', () => {
  it('reads legacy sshTarget entries as manual machines that keep identity and stacks', () => {
    const [migrated] = normalizeSshMachines(
      [
        {
          id: 'h_legacy',
          name: 'Cloud',
          sshTarget: 'cloud-alias',
          identityFile: '~/.ssh/id_lab',
          stacks: [{ id: 's_1', name: 'prod', composeDir: '/srv/prod' }]
        }
      ],
      counterIds()
    )

    expect(migrated).toMatchObject({
      id: 'h_legacy',
      name: 'Cloud',
      source: 'manual',
      hostname: 'cloud-alias',
      identityFile: '~/.ssh/id_lab',
      autoConnect: false,
      projects: []
    })
    expect(migrated.stacks.map((s) => s.id)).toEqual(['s_1'])
    expect(migrated).not.toHaveProperty('sshTarget')
  })

  it('keeps only the alias for ssh config machines and drops unknown fields', () => {
    const [m] = normalizeSshMachines(
      [{ id: 'm_a', source: 'sshConfig', alias: 'build-box', hostname: 'leak', user: 'x', token: 'secret', autoConnect: true }],
      counterIds()
    )
    expect(m).toEqual({ id: 'm_a', name: 'build-box', source: 'sshConfig', alias: 'build-box', autoConnect: true, projects: [], stacks: [] })
  })

  it('validates projects: absolute paths only, unique per machine, label from base name', () => {
    const [m] = normalizeSshMachines(
      [
        {
          id: 'm_1',
          source: 'manual',
          name: 'box',
          hostname: 'box',
          port: 2222,
          projects: [
            { id: 'p_1', path: '/home/dev/src/dotcraft/' },
            { id: 'p_2', path: '/home/dev/src/dotcraft' },
            { id: 'p_3', path: '~/relative' },
            { id: 'p_4', path: '/srv/my project', label: 'Mine' }
          ]
        }
      ],
      counterIds()
    )
    expect(m.port).toBe(2222)
    expect(m.projects).toEqual([
      { id: 'p_1', path: '/home/dev/src/dotcraft', label: 'dotcraft' },
      { id: 'p_4', path: '/srv/my project', label: 'Mine' }
    ])
  })

  it('drops invalid entries, fills ids, and keeps names unique ignoring case', () => {
    const machines = normalizeSshMachines(
      [
        { source: 'manual', name: 'Box', hostname: 'a.example' },
        { source: 'manual', name: 'box', hostname: 'b.example' },
        { source: 'manual', name: 'Bad', hostname: '-oProxyCommand=evil' },
        { source: 'sshConfig', alias: 'has space' },
        'garbage'
      ],
      counterIds()
    )
    expect(machines.map((m) => [m.id, m.name])).toEqual([
      ['m_1', 'Box'],
      ['m_2', 'box (2)']
    ])
    expect(normalizeSshMachines(undefined)).toEqual([])
  })
})

describe('machine validation', () => {
  const existing = [machine(), machine({ id: 'm_2', name: 'alias-box', source: 'sshConfig', alias: 'alias-box', hostname: undefined })]

  it('requires a unique name and a safe hostname for manual machines', () => {
    expect(validateMachineEntry({ source: 'manual', name: '', hostname: '' }, existing)).toEqual({
      name: 'required',
      hostname: 'required'
    })
    expect(validateMachineEntry({ source: 'manual', name: 'BUILD-BOX', hostname: 'host;rm -rf /' }, existing)).toEqual({
      name: 'duplicate',
      hostname: 'invalid'
    })
    expect(validateMachineEntry({ source: 'manual', name: 'new', hostname: 'host', port: 70000, identityFile: '-i' }, existing)).toEqual({
      port: 'invalid',
      identityFile: 'invalid'
    })
    expect(validateMachineEntry({ source: 'manual', name: 'build-box', hostname: 'h' }, existing, 'm_1')).toEqual({})
  })

  it('rejects ssh config aliases that are already added', () => {
    expect(validateMachineEntry({ source: 'sshConfig', alias: 'alias-box' }, existing)).toEqual({
      alias: 'duplicate',
      name: 'duplicate'
    })
    expect(validateMachineEntry({ source: 'sshConfig', alias: 'other', name: 'Other' }, existing)).toEqual({})
  })

  it('creates switched-on machines and applies manual edits', () => {
    const created = createMachineFromEntry({ source: 'manual', name: ' Lab ', hostname: 'dev@lab', port: 2200 }, counterIds())
    expect(created).toEqual({
      id: 'm_1',
      name: 'Lab',
      source: 'manual',
      hostname: 'dev@lab',
      port: 2200,
      autoConnect: true,
      projects: [],
      stacks: []
    })
    const edited = applyMachineEdit(created, { port: null, identityFile: '~/.ssh/id_lab', name: 'Lab 2' })
    expect(edited).toMatchObject({ name: 'Lab 2', identityFile: '~/.ssh/id_lab' })
    expect(edited).not.toHaveProperty('port')
  })
})

describe('ssh argv', () => {
  it('passes only the alias for ssh config machines, after --', () => {
    const args = buildSshArgs(machineSshTarget(machine({ source: 'sshConfig', alias: 'build-box', hostname: undefined })), 'echo hi')
    expect(args).toContain('BatchMode=yes')
    expect(args).toContain('StrictHostKeyChecking=accept-new')
    expect(args).not.toContain('-p')
    expect(args).not.toContain('-i')
    const dd = args.indexOf('--')
    expect(args.slice(dd)).toEqual(['--', 'build-box', 'echo hi'])
  })

  it('adds port and identity with IdentitiesOnly for manual machines', () => {
    const target = machineSshTarget(machine({ port: 2222, identityFile: '~/.ssh/id_lab' }))
    const args = buildSshArgs(target, 'true')
    expect(args.join(' ')).toContain('-p 2222 -i ~/.ssh/id_lab -o IdentitiesOnly=yes -- dev@10.0.0.12 true')
  })

  it('builds loopback-only forwards that exit on forward failure', () => {
    const args = buildSshTunnelArgs({ destination: 'box' }, 51000, 9100)
    expect(args[0]).toBe('-N')
    expect(args).toContain('ExitOnForwardFailure=yes')
    expect(args).toContain('127.0.0.1:51000:127.0.0.1:9100')
    expect(args.slice(-2)).toEqual(['--', 'box'])
  })

  it('resolves ssh config aliases through ssh -G', () => {
    expect(buildSshResolveArgs('build-box')).toEqual(['-G', '--', 'build-box'])
    const resolved = parseSshResolveOutput(
      'host build-box\nuser dev\nhostname 10.0.0.12\nport 2222\nidentityfile ~/.ssh/id_lab\nidentityfile ~/.ssh/id_rsa\nproxyjump none\n'
    )
    expect(resolved).toEqual({ user: 'dev', hostname: '10.0.0.12', port: 2222, identityFiles: ['~/.ssh/id_lab', '~/.ssh/id_rsa'] })
    expect(formatResolvedHost(resolved)).toBe('dev@10.0.0.12:2222')
    expect(formatResolvedHost({ hostname: 'box', port: 22, identityFiles: [] })).toBe('box')
    expect(parseSshResolveOutput('proxyjump bastion\r\nhostname h\r\n').proxyJump).toBe('bastion')
  })
})

describe('validation helpers', () => {
  it('accepts ssh targets and rejects option or whitespace injection', () => {
    expect(isValidSshTarget('user@cloud')).toBe(true)
    expect(isValidSshTarget('-oProxyCommand=evil')).toBe(false)
    expect(isValidSshTarget('user@host extra')).toBe(false)
    expect(isValidIdentityFile('~/.ssh/id_ed25519')).toBe(true)
    expect(isValidIdentityFile('-i')).toBe(false)
  })

  it('distinguishes compose paths from folder paths that may contain spaces', () => {
    expect(isValidRemotePath('~/sample-stack/deploy')).toBe(true)
    expect(isValidRemotePath('/srv/has space')).toBe(false)
    expect(isValidRemoteFolderPath('/srv/has space')).toBe(true)
    expect(isValidRemoteFolderPath('~')).toBe(true)
    expect(isValidRemoteFolderPath('/srv/../etc')).toBe(false)
    expect(isValidRemoteFolderPath('/srv/a\nb')).toBe(false)
    expect(isValidRemoteFolderPath('relative')).toBe(false)
  })

  it('quotes values for the remote shell', () => {
    expect(shellSingleQuote("a'b")).toBe("'a'\\''b'")
    expect(quoteRemotePath('~')).toBe('~')
    expect(quoteRemotePath("~/it's here")).toBe("~/'it'\\''s here'")
    expect(quoteRemotePath('/srv/x')).toBe("'/srv/x'")
  })
})

describe('redactSecrets', () => {
  it('masks explicit secrets, secret assignments, bearer values, and token query params', () => {
    expect(redactSecrets('value fixture-explicit-value here', ['fixture-explicit-value'])).toBe(`value ${REDACTION_MASK} here`)
    expect(redactSecrets('APPSERVER_TOKEN=fixture-appserver-token')).toBe(`APPSERVER_TOKEN=${REDACTION_MASK}`)
    expect(redactSecrets('Authorization: Bearer abcdef123')).toBe(`Authorization: Bearer ${REDACTION_MASK}`)
    expect(redactSecrets('ws://127.0.0.1:9100/ws?token=fixture-query-token')).toBe(`ws://127.0.0.1:9100/ws?token=${REDACTION_MASK}`)
    expect(redactSecrets('docker=ok\ncompose=ok')).toBe('docker=ok\ncompose=ok')
  })
})

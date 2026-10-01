import { mkdtemp, mkdir, rm, writeFile } from 'fs/promises'
import { join } from 'path'
import { tmpdir } from 'os'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { discoverSshHosts, inspectLocalSshConfig, parseSshConfig } from '../sshMachines/localSshConfig'

const tempDirs: string[] = []

async function makeHome(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), 'dotcraft-ssh-home-'))
  tempDirs.push(dir)
  return dir
}

afterEach(async () => {
  while (tempDirs.length > 0) {
    const dir = tempDirs.pop()
    if (dir) await rm(dir, { recursive: true, force: true })
  }
})

describe('local SSH config inspection', () => {
  it('parses concrete Host aliases and ignores wildcard patterns', () => {
    const aliases = parseSshConfig(`
Host prod prod-short
  HostName prod.example.com
  User deploy
  Port 2222
  IdentityFile ~/.ssh/prod_ed25519

Host *
  ForwardAgent yes
`)

    expect(aliases.map((alias) => alias.alias)).toEqual(['prod', 'prod-short'])
    expect(aliases[0]).toMatchObject({
      hostName: 'prod.example.com',
      user: 'deploy',
      port: '2222',
      identityFiles: ['~/.ssh/prod_ed25519']
    })
  })

  it('reports existing default keys, config identity files, and aliases from the user SSH directory', async () => {
    const home = await makeHome()
    const sshDir = join(home, '.ssh')
    await mkdir(sshDir)
    await writeFile(join(sshDir, 'id_ed25519'), 'key')
    await writeFile(join(sshDir, 'id_ed25519.pub'), 'pub')
    await writeFile(join(sshDir, 'prod_key'), 'key')
    await writeFile(
      join(sshDir, 'config'),
      `
Host prod
  HostName prod.example.com
  User deploy
  IdentityFile ~/.ssh/prod_key
`
    )

    const info = await inspectLocalSshConfig(home)

    expect(info.configExists).toBe(true)
    expect(info.aliases).toHaveLength(1)
    expect(info.aliases[0].alias).toBe('prod')
    expect(info.identities.map((identity) => identity.path)).toContain('~/.ssh/id_ed25519')
    expect(info.identities.map((identity) => identity.path)).toContain('~/.ssh/prod_key')
    expect(info.identities.find((identity) => identity.path === '~/.ssh/prod_key')?.hostAliases).toEqual(['prod'])
  })

  it('discovers aliases with their ssh -G resolution and marks added ones', async () => {
    const home = await makeHome()
    await mkdir(join(home, '.ssh'))
    await writeFile(join(home, '.ssh', 'config'), 'Host build-box lab\n  HostName 10.0.0.12\nHost *.wild\n  User x\n')
    const run = vi.fn(async (args: string[]) => args[2] === 'build-box'
      ? { code: 0, stdout: 'user dev\nhostname 10.0.0.12\nport 2222\n', stderr: '', timedOut: false }
      : { code: 255, stdout: '', stderr: 'ssh: bad config line\n', timedOut: false })

    const discovery = await discoverSshHosts(
      [{ id: 'm_1', name: 'lab', source: 'sshConfig', alias: 'lab', autoConnect: true, projects: [], stacks: [] }],
      { homeDir: home, run }
    )

    expect(run).toHaveBeenCalledWith(['-G', '--', 'build-box'], { timeoutMs: 5_000 })
    expect(discovery.hosts).toEqual([
      { alias: 'build-box', added: false, resolvedHost: 'dev@10.0.0.12:2222' },
      { alias: 'lab', added: true, error: 'ssh: bad config line' }
    ])
  })
})

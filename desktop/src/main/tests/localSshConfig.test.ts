import { mkdtemp, mkdir, rm, writeFile } from 'fs/promises'
import { join } from 'path'
import { tmpdir } from 'os'
import { afterEach, describe, expect, it } from 'vitest'
import { inspectLocalSshConfig, parseSshConfig } from '../remoteServers/localSshConfig'

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
})

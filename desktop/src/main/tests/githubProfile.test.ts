import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { rm } from 'fs/promises'
import { tmpdir } from 'os'
import { join } from 'path'
import { getGitHubIdentity } from '../githubProfile'

const TEST_USERDATA = join(tmpdir(), 'dotcraft-githubprofile-test')

vi.mock('electron', () => ({
  app: { getPath: () => TEST_USERDATA },
  net: { fetch: vi.fn() }
}))

function makeFetch(): ReturnType<typeof vi.fn> {
  const png = Buffer.from('89504e470d0a1a0a', 'hex')
  return vi.fn(async (url: string) => {
    if (url.includes('api.github.com')) {
      return new Response(
        JSON.stringify({ login: 'Octocat', name: 'The Octocat', avatar_url: 'https://avatars.example/u/1' }),
        { status: 200, headers: { 'content-type': 'application/json' } }
      )
    }
    return new Response(png, { status: 200, headers: { 'content-type': 'image/png' } })
  })
}

describe('getGitHubIdentity', () => {
  beforeEach(async () => {
    await rm(TEST_USERDATA, { recursive: true, force: true })
  })
  afterEach(async () => {
    await rm(TEST_USERDATA, { recursive: true, force: true })
  })

  it('fetches name and avatar data URL once, then serves the fresh cache without the network', async () => {
    const fetchImpl = makeFetch()
    const identity = await getGitHubIdentity('octocat', fetchImpl)

    expect(identity).toMatchObject({ login: 'Octocat', name: 'The Octocat' })
    expect(identity!.avatarDataUrl).toMatch(/^data:image\/png;base64,/)
    expect(fetchImpl).toHaveBeenCalledTimes(2)

    const secondFetch = makeFetch()
    const cached = await getGitHubIdentity('octocat', secondFetch)
    expect(cached!.name).toBe('The Octocat')
    expect(cached!.avatarDataUrl).toMatch(/^data:image\/png;base64,/)
    expect(secondFetch).not.toHaveBeenCalled()
  })

  it('returns null for invalid usernames without a network call and for failed lookups without cache', async () => {
    const fetchImpl = makeFetch()
    expect(await getGitHubIdentity('has space', fetchImpl)).toBeNull()
    expect(await getGitHubIdentity('', fetchImpl)).toBeNull()
    expect(fetchImpl).not.toHaveBeenCalled()

    const failing = vi.fn(async () => {
      throw new Error('offline')
    })
    expect(await getGitHubIdentity('octocat', failing)).toBeNull()
  })
})

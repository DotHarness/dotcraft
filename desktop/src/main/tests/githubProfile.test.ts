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

/** A fake main-process fetch: GitHub user JSON + a 1x1 PNG avatar. */
function makeFetch(): ReturnType<typeof vi.fn> {
  const png = Buffer.from('89504e470d0a1a0a', 'hex') // PNG signature; enough for the bytes check
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

  it('fetches, caches, and returns name + avatar data URL', async () => {
    const fetchImpl = makeFetch()
    const identity = await getGitHubIdentity('octocat', fetchImpl)

    expect(identity).not.toBeNull()
    expect(identity!.login).toBe('Octocat')
    expect(identity!.name).toBe('The Octocat')
    expect(identity!.avatarDataUrl).toMatch(/^data:image\/png;base64,/)
    // One call for the user lookup, one for the avatar download.
    expect(fetchImpl).toHaveBeenCalledTimes(2)
  })

  it('serves a fresh cache without hitting the network again', async () => {
    await getGitHubIdentity('octocat', makeFetch())

    const secondFetch = makeFetch()
    const identity = await getGitHubIdentity('octocat', secondFetch)

    expect(identity!.name).toBe('The Octocat')
    expect(identity!.avatarDataUrl).toMatch(/^data:image\/png;base64,/)
    expect(secondFetch).not.toHaveBeenCalled()
  })

  it('returns null for an invalid username without any network call', async () => {
    const fetchImpl = makeFetch()
    expect(await getGitHubIdentity('has space', fetchImpl)).toBeNull()
    expect(await getGitHubIdentity('', fetchImpl)).toBeNull()
    expect(fetchImpl).not.toHaveBeenCalled()
  })

  it('returns null when the lookup fails and there is no cache', async () => {
    const failing = vi.fn(async () => {
      throw new Error('offline')
    })
    expect(await getGitHubIdentity('octocat', failing)).toBeNull()
  })
})

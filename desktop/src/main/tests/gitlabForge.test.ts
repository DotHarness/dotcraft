import { describe, expect, it, vi } from 'vitest'
import { createGitlabForge, gitlabAvatarUrl, type GlabRun } from '../gitlabForge'

vi.mock('electron', () => ({ net: { fetch: vi.fn() } }))

const HOST = 'gitlab.example.net'
const repo = { provider: 'gitlab' as const, host: HOST, path: 'tools/perf/insight-dashboard' }
const mrTarget = { ...repo, kind: 'pull' as const, number: 21 }
const MR_ENDPOINT = 'projects/tools%2Fperf%2Finsight-dashboard/merge_requests/21'

function mergeRequest(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    iid: 21,
    title: 'Serve frame-time charts from the warehouse',
    state: 'opened',
    draft: false,
    work_in_progress: false,
    has_conflicts: false,
    created_at: '2026-10-30T10:01:34.000Z',
    changes_count: '223+',
    author: { name: 'Sample Dev', username: 'sample-dev', avatar_url: `https://${HOST}/uploads/-/system/user/avatar/5/avatar.png` },
    head_pipeline: { status: 'success' },
    source_project_id: 7,
    target_project_id: 7,
    ...overrides
  }
}

function ok(body: unknown): GlabRun {
  return { code: 0, stdout: JSON.stringify(body), stderr: '' }
}

function httpError(status: number): GlabRun {
  return { code: 1, stdout: '', stderr: `glab: 404 Not Found (HTTP ${status})` }
}

function setup(options: {
  hostToken?: string
  globalToken?: string
  api?: Record<string, GlabRun>
  missing?: boolean
  env?: NodeJS.ProcessEnv
} = {}) {
  const runGlab = vi.fn(async (args: string[], _env: NodeJS.ProcessEnv): Promise<GlabRun> => {
    if (options.missing) throw Object.assign(new Error('spawn glab ENOENT'), { code: 'ENOENT' })
    if (args[0] === 'config') {
      const token = args.includes('--global') ? options.globalToken ?? '' : options.hostToken ?? 'glpat-host'
      return { code: 0, stdout: `${token}\n`, stderr: '' }
    }
    const endpoint = args[3]
    const response = options.api?.[endpoint]
    if (!response) throw new Error(`unexpected request ${endpoint}`)
    return response
  })
  const fetchAvatar = vi.fn(async (url: string) => `data:image/png;base64,${Buffer.from(url).toString('base64')}`)
  const forge = createGitlabForge({ runGlab, fetchAvatar, env: options.env ?? {}, now: () => 1_000_000 })
  const apiCalls = () => runGlab.mock.calls.filter(([args]) => args[0] === 'api').map(([args]) => args[3])
  return { forge, runGlab, fetchAvatar, apiCalls }
}

async function pullState(overrides: Record<string, unknown>): Promise<unknown> {
  const { forge } = setup({ api: { [MR_ENDPOINT]: ok(mergeRequest(overrides)) } })
  const result = await forge.readRef(mrTarget)
  return result.status === 'ready' ? result.ref.state : result
}

describe('GitLab merge requests', () => {
  it('maps a merge request through glab api', async () => {
    const { forge, runGlab } = setup({ api: { [MR_ENDPOINT]: ok(mergeRequest()) } })
    expect(await forge.readRef(mrTarget)).toEqual({
      status: 'ready',
      ref: {
        ...mrTarget,
        title: 'Serve frame-time charts from the warehouse',
        author: 'Sample Dev',
        authorAvatarUrl: `https://${HOST}/uploads/-/system/user/avatar/5/avatar.png`,
        createdAt: '2026-10-30T10:01:34.000Z',
        state: 'open',
        additions: null,
        deletions: null,
        changedFiles: 223,
        checks: { state: 'passing', counts: null }
      }
    })
    expect(runGlab).toHaveBeenCalledWith(['api', '--hostname', HOST, MR_ENDPOINT], expect.objectContaining({ NO_COLOR: '1' }))
  })

  it('derives states and pipeline status', async () => {
    expect(await pullState({ state: 'merged', draft: true })).toBe('merged')
    expect(await pullState({ state: 'closed' })).toBe('closed')
    expect(await pullState({ draft: true, has_conflicts: true })).toBe('draft')
    expect(await pullState({ work_in_progress: true })).toBe('draft')
    expect(await pullState({ has_conflicts: true })).toBe('conflicting')
    expect(await pullState({ state: 'locked' })).toBe('open')

    const checks = async (status: unknown) => {
      const { forge } = setup({ api: { [MR_ENDPOINT]: ok(mergeRequest({ head_pipeline: status === undefined ? null : { status } })) } })
      const result = await forge.readRef(mrTarget)
      return result.status === 'ready' && result.ref.kind === 'pull' ? result.ref.checks.state : null
    }
    expect(await checks('failed')).toBe('failing')
    expect(await checks('canceled')).toBe('failing')
    expect(await checks('running')).toBe('pending')
    expect(await checks('manual')).toBe('pending')
    expect(await checks('skipped')).toBe('none')
    expect(await checks(undefined)).toBe('none')
  })

  it('reads issues with GitLab issue numbering', async () => {
    const { forge } = setup({
      api: { 'projects/tools%2Fperf%2Finsight-dashboard/issues/3': ok({ iid: 3, title: 'Slow chart', state: 'closed', author: { username: 'sample-dev' } }) }
    })
    expect(await forge.readRef({ ...repo, kind: 'issue', number: 3 }))
      .toMatchObject({ status: 'ready', ref: { kind: 'issue', number: 3, state: 'completed', author: 'sample-dev' } })
  })

  it('needs glab to hold a token for the host before it asks the host anything', async () => {
    const missing = setup({ missing: true })
    expect(await missing.forge.readRef(mrTarget)).toEqual({ status: 'error', error: 'notInstalled' })

    const unknown = setup({ hostToken: '' })
    expect(await unknown.forge.readRef(mrTarget)).toEqual({ status: 'error', error: 'auth' })
    expect(unknown.apiCalls()).toEqual([])

    const onlyGlobal = setup({ hostToken: 'glpat-global', globalToken: 'glpat-global' })
    expect(await onlyGlobal.forge.readRef(mrTarget)).toEqual({ status: 'error', error: 'auth' })
    expect(onlyGlobal.apiCalls()).toEqual([])
  })

  it('keeps tokens set for the default host away from other hosts', async () => {
    const env = { GITLAB_TOKEN: 'glpat-env', PATH: '/bin' }
    const other = setup({ env, api: { [MR_ENDPOINT]: ok(mergeRequest()) } })
    await other.forge.readRef(mrTarget)
    for (const [, passed] of other.runGlab.mock.calls) {
      expect(passed.GITLAB_TOKEN).toBeUndefined()
      expect(passed.PATH).toBe('/bin')
    }

    const own = setup({ env: { ...env, GITLAB_HOST: `https://${HOST}` }, api: { [MR_ENDPOINT]: ok(mergeRequest()) } })
    await own.forge.readRef(mrTarget)
    expect(own.runGlab.mock.calls.every(([, passed]) => passed.GITLAB_TOKEN === 'glpat-env')).toBe(true)
  })

  it('maps API failures', async () => {
    const read = async (response: GlabRun) => setup({ api: { [MR_ENDPOINT]: response } }).forge.readRef(mrTarget)
    expect(await read(httpError(404))).toEqual({ status: 'error', error: 'unavailable' })
    expect(await read(httpError(401))).toEqual({ status: 'error', error: 'auth' })
    expect(await read(httpError(429))).toEqual({ status: 'error', error: 'rateLimit' })
    expect(await read({ code: 0, stdout: 'not json', stderr: '' })).toEqual({ status: 'error', error: 'unavailable' })
  })
})

describe('GitLab branch merge requests', () => {
  const LIST_ENDPOINT =
    'projects/tools%2Fperf%2Finsight-dashboard/merge_requests?source_branch=refactor%2Fbi&order_by=updated_at&sort=desc&per_page=20'

  it('prefers an open merge request from the same project, then reads it in full', async () => {
    const { forge, apiCalls } = setup({
      api: {
        [LIST_ENDPOINT]: ok([
          mergeRequest({ iid: 30, state: 'opened', source_project_id: 99 }),
          mergeRequest({ iid: 25, state: 'merged' }),
          mergeRequest({ iid: 21, state: 'opened' })
        ]),
        [MR_ENDPOINT]: ok(mergeRequest())
      }
    })
    expect(await forge.readBranchPull(repo, 'refactor/bi')).toMatchObject({ status: 'ready', ref: { number: 21, changedFiles: 223 } })
    expect(apiCalls()).toEqual([LIST_ENDPOINT, MR_ENDPOINT])
  })

  it('finds nothing on hosts glab does not know or when no merge request matches', async () => {
    const unknown = setup({ hostToken: '' })
    expect(await unknown.forge.readBranchPull(repo, 'refactor/bi')).toEqual({ status: 'none' })
    expect(unknown.apiCalls()).toEqual([])

    const missing = setup({ missing: true })
    expect(await missing.forge.readBranchPull(repo, 'refactor/bi')).toEqual({ status: 'none' })

    const empty = setup({ api: { [LIST_ENDPOINT]: ok([mergeRequest({ source_project_id: 99 })]) } })
    expect(await empty.forge.readBranchPull(repo, 'refactor/bi')).toEqual({ status: 'none' })
  })
})

describe('GitLab author avatars', () => {
  it('accepts only the instance itself or Gravatar over https', () => {
    expect(gitlabAvatarUrl('/uploads/-/system/user/avatar/5/avatar.png', HOST))
      .toBe(`https://${HOST}/uploads/-/system/user/avatar/5/avatar.png`)
    expect(gitlabAvatarUrl('https://secure.gravatar.com/avatar/abc?s=80', HOST)).toBe('https://secure.gravatar.com/avatar/abc?s=80')
    expect(gitlabAvatarUrl('https://tracker.example.com/a.png', HOST)).toBeNull()
    expect(gitlabAvatarUrl(`http://${HOST}/a.png`, HOST)).toBeNull()
    expect(gitlabAvatarUrl(`https://user:pw@${HOST}/a.png`, HOST)).toBeNull()
  })

  it('downloads an avatar once, and only for a host glab knows', async () => {
    const url = `https://${HOST}/uploads/-/system/user/avatar/5/avatar.png`
    const known = setup()
    const [first, second] = await Promise.all([known.forge.readAvatar(HOST, url), known.forge.readAvatar(HOST, url)])
    expect(first).toMatch(/^data:image\/png;base64,/)
    expect(second).toBe(first)
    expect(known.fetchAvatar).toHaveBeenCalledTimes(1)
    expect(await known.forge.readAvatar(HOST, 'https://tracker.example.com/a.png')).toBeNull()

    const unknown = setup({ hostToken: '' })
    expect(await unknown.forge.readAvatar(HOST, url)).toBeNull()
    expect(unknown.fetchAvatar).not.toHaveBeenCalled()
  })
})

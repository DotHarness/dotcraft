import { describe, expect, it, vi } from 'vitest'
import { createForgeReferenceService, parseForgeRemoteUrl } from '../forgeReferences'
import { createGithubForge } from '../githubForge'
import type { GitlabForge } from '../gitlabForge'

vi.mock('electron', () => ({ net: { fetch: vi.fn() } }))

const target = { provider: 'github' as const, host: 'github.com', path: 'OctoOrg/sample-app', kind: 'pull' as const, number: 42 }

function pullNode(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    __typename: 'PullRequest',
    number: 42,
    title: 'Add a settings page',
    state: 'OPEN',
    isDraft: false,
    createdAt: '2026-10-17T08:43:00Z',
    url: 'https://github.com/OctoOrg/sample-app/pull/42',
    author: { login: 'octo-dev' },
    additions: 3575,
    deletions: 3716,
    changedFiles: 64,
    reviewDecision: null,
    mergeable: 'MERGEABLE',
    headRepositoryOwner: { login: 'OctoOrg' },
    commits: { nodes: [] },
    ...overrides
  }
}

function rollup(checkRuns: Array<[string, number]>, statuses: Array<[string, number]> = []): Record<string, unknown> {
  return {
    commits: {
      nodes: [{
        commit: {
          statusCheckRollup: {
            state: 'PENDING',
            contexts: {
              checkRunCountsByState: checkRuns.map(([state, count]) => ({ state, count })),
              statusContextCountsByState: statuses.map(([state, count]) => ({ state, count }))
            }
          }
        }
      }]
    }
  }
}

function json(body: unknown, init: { status?: number; headers?: Record<string, string> } = {}): Response {
  return new Response(JSON.stringify(body), {
    status: init.status ?? 200,
    headers: { 'content-type': 'application/json', ...init.headers }
  })
}

function summaryResponse(node: Record<string, unknown> | null): Response {
  return json({ data: { repository: { issueOrPullRequest: node } } })
}

function setup(options: {
  responses?: Array<Response | Error>
  runGh?: (args: string[]) => Promise<string>
  origin?: string | null
} = {}) {
  let clock = 1_000_000
  const queue = [...(options.responses ?? [])]
  const fetch = vi.fn(async (_url: string, _init?: RequestInit) => {
    const next = queue.shift()
    if (!next) throw new Error('unexpected request')
    if (next instanceof Error) throw next
    return next
  })
  const runGh = vi.fn(options.runGh ?? (async () => 'gho_token\n'))
  const readOrigin = vi.fn(async () => (options.origin === undefined ? 'git@github.com:OctoOrg/sample-app.git' : options.origin))
  const gitlab: GitlabForge = {
    readRef: vi.fn(async () => ({ status: 'error' as const, error: 'unavailable' as const })),
    readBranchPull: vi.fn(async () => ({ status: 'none' as const })),
    readAvatar: vi.fn(async () => null)
  }
  const now = (): number => clock
  const service = createForgeReferenceService({ github: createGithubForge({ fetch, runGh, now }), gitlab, readOrigin, now })
  return {
    service,
    gitlab,
    fetch,
    runGh,
    readOrigin,
    advance(ms: number) {
      clock += ms
    }
  }
}

async function pullState(overrides: Record<string, unknown>): Promise<unknown> {
  const { service } = setup({ responses: [summaryResponse(pullNode(overrides))] })
  const result = await service.getReferenceSummary(target)
  return result.status === 'ready' ? result.ref.state : result
}

describe('GitHub reference summaries', () => {
  it('derives pull request states', async () => {
    expect(await pullState({ state: 'MERGED', isDraft: true })).toBe('merged')
    expect(await pullState({ state: 'CLOSED' })).toBe('closed')
    expect(await pullState({ isDraft: true, reviewDecision: 'APPROVED' })).toBe('draft')
    expect(await pullState({ reviewDecision: 'CHANGES_REQUESTED', mergeable: 'CONFLICTING' })).toBe('changesRequested')
    expect(await pullState({ mergeable: 'CONFLICTING', reviewDecision: 'APPROVED' })).toBe('conflicting')
    expect(await pullState({ reviewDecision: 'APPROVED' })).toBe('approved')
    expect(await pullState({})).toBe('open')
  })

  it('derives issue states from state and reason', async () => {
    const issue = (state: string, stateReason: string | null) => ({
      __typename: 'Issue', number: 42, title: 'Issue', state, stateReason, createdAt: '2026-10-01T00:00:00Z', author: null
    })
    const { service } = setup({
      responses: [summaryResponse(issue('OPEN', null))]
    })
    const open = await service.getReferenceSummary({ ...target, kind: 'issue' })
    expect(open).toMatchObject({ status: 'ready', ref: { kind: 'issue', state: 'open', author: 'ghost' } })

    const notPlanned = setup({ responses: [summaryResponse(issue('CLOSED', 'NOT_PLANNED'))] })
    expect(await notPlanned.service.getReferenceSummary(target)).toMatchObject({ ref: { kind: 'issue', state: 'notPlanned' } })

    const completed = setup({ responses: [summaryResponse(issue('CLOSED', 'COMPLETED'))] })
    expect(await completed.service.getReferenceSummary(target)).toMatchObject({ ref: { kind: 'issue', state: 'completed' } })
  })

  it('aggregates check runs and status contexts', async () => {
    const checks = async (runs: Array<[string, number]>, statuses: Array<[string, number]> = []) => {
      const { service } = setup({ responses: [summaryResponse(pullNode(rollup(runs, statuses)))] })
      const result = await service.getReferenceSummary(target)
      return result.status === 'ready' && result.ref.kind === 'pull' ? result.ref.checks : null
    }
    expect(await checks([['SUCCESS', 10], ['SKIPPED', 2], ['NEUTRAL', 1], ['CANCELLED', 1]]))
      .toEqual({ state: 'failing', counts: { passed: 13, failed: 1, total: 14 } })
    expect(await checks([['SUCCESS', 5], ['IN_PROGRESS', 2]], [['EXPECTED', 1]]))
      .toEqual({ state: 'pending', counts: { passed: 5, failed: 0, total: 8 } })
    expect(await checks([['SUCCESS', 4]], [['SUCCESS', 2]]))
      .toEqual({ state: 'passing', counts: { passed: 6, failed: 0, total: 6 } })
    expect(await checks([['STALE', 1]]))
      .toEqual({ state: 'failing', counts: { passed: 0, failed: 1, total: 1 } })
    expect(await checks([]))
      .toEqual({ state: 'none', counts: { passed: 0, failed: 0, total: 0 } })
  })

  it('reports a missing CLI and a signed-out CLI', async () => {
    const missing = setup({ runGh: async () => { throw Object.assign(new Error('spawn gh ENOENT'), { code: 'ENOENT' }) } })
    expect(await missing.service.getReferenceSummary(target)).toEqual({ status: 'error', error: 'notInstalled' })
    expect(missing.fetch).not.toHaveBeenCalled()

    const signedOut = setup({ runGh: async () => { throw Object.assign(new Error('not logged in'), { code: 1 }) } })
    expect(await signedOut.service.getReferenceSummary(target)).toEqual({ status: 'error', error: 'auth' })

    const empty = setup({ runGh: async () => '  \n' })
    expect(await empty.service.getReferenceSummary(target)).toEqual({ status: 'error', error: 'auth' })
  })

  it('maps 401 to auth and asks the CLI for a new token next time', async () => {
    const env = setup({ responses: [json({ message: 'Bad credentials' }, { status: 401 }), summaryResponse(pullNode())] })
    expect(await env.service.getReferenceSummary(target)).toEqual({ status: 'error', error: 'auth' })
    env.advance(6_000)
    expect(await env.service.getReferenceSummary(target)).toMatchObject({ status: 'ready' })
    expect(env.runGh).toHaveBeenCalledTimes(2)
    expect(env.fetch.mock.calls[1][1]?.headers).toMatchObject({ Authorization: 'bearer gho_token' })
  })

  it('maps rate limits with their reset time', async () => {
    const reset = 1_800_000_000
    const exhausted = setup({
      responses: [json({}, { status: 403, headers: { 'x-ratelimit-remaining': '0', 'x-ratelimit-reset': String(reset) } })]
    })
    expect(await exhausted.service.getReferenceSummary(target))
      .toEqual({ status: 'error', error: 'rateLimit', resetsAt: reset * 1000 })

    const graphqlLimited = setup({
      responses: [json({ errors: [{ type: 'RATE_LIMITED' }] }, { headers: { 'x-ratelimit-reset': String(reset) } })]
    })
    expect(await graphqlLimited.service.getReferenceSummary(target))
      .toEqual({ status: 'error', error: 'rateLimit', resetsAt: reset * 1000 })

    const forbidden = setup({ responses: [json({}, { status: 403, headers: { 'x-ratelimit-remaining': '12' } })] })
    expect(await forbidden.service.getReferenceSummary(target)).toEqual({ status: 'error', error: 'unavailable' })
  })

  it('maps missing items, network failures, and malformed targets to unavailable', async () => {
    const notFound = setup({ responses: [json({ data: { repository: null }, errors: [{ type: 'NOT_FOUND' }] })] })
    expect(await notFound.service.getReferenceSummary(target)).toEqual({ status: 'error', error: 'unavailable' })

    const offline = setup({ responses: [new Error('net::ERR_INTERNET_DISCONNECTED')] })
    expect(await offline.service.getReferenceSummary(target)).toEqual({ status: 'error', error: 'unavailable' })

    const env = setup()
    expect(await env.service.getReferenceSummary({ ...target, path: '../sample-app' })).toEqual({ status: 'error', error: 'unavailable' })
    expect(await env.service.getReferenceSummary({ ...target, host: 'github.example.com' })).toEqual({ status: 'error', error: 'unavailable' })
    expect(await env.service.getReferenceSummary({ ...target, number: 0 })).toEqual({ status: 'error', error: 'unavailable' })
    expect(await env.service.getReferenceSummary({ ...target, kind: 'commit' })).toEqual({ status: 'error', error: 'unavailable' })
    expect(env.runGh).not.toHaveBeenCalled()
    expect(env.fetch).not.toHaveBeenCalled()
  })

  it('caches successes for a minute and failures for a few seconds, sharing in-flight requests', async () => {
    const env = setup({
      responses: [summaryResponse(pullNode()), new Error('offline'), summaryResponse(pullNode({ state: 'MERGED' }))]
    })
    const [first, second] = await Promise.all([
      env.service.getReferenceSummary(target),
      env.service.getReferenceSummary({ ...target, path: 'octoorg/sample-app' })
    ])
    expect(first).toEqual(second)
    expect(env.fetch).toHaveBeenCalledTimes(1)

    env.advance(59_000)
    await env.service.getReferenceSummary(target)
    expect(env.fetch).toHaveBeenCalledTimes(1)

    env.advance(2_000)
    expect(await env.service.getReferenceSummary(target)).toEqual({ status: 'error', error: 'unavailable' })
    env.advance(4_000)
    expect(await env.service.getReferenceSummary(target)).toEqual({ status: 'error', error: 'unavailable' })
    expect(env.fetch).toHaveBeenCalledTimes(2)

    env.advance(2_000)
    expect(await env.service.getReferenceSummary(target)).toMatchObject({ status: 'ready', ref: { state: 'merged' } })
  })
})

describe('GitHub branch pull requests', () => {
  function branchResponse(nodes: Array<Record<string, unknown>>): Response {
    return json({ data: { repository: { pullRequests: { nodes } } } })
  }

  it('prefers an open pull request over a more recent closed one, else the most recent', async () => {
    const env = setup({
      responses: [branchResponse([
        pullNode({ number: 48, state: 'MERGED' }),
        pullNode({ number: 42, state: 'OPEN' })
      ])]
    })
    expect(await env.service.getBranchPullRequest('F:/work/sample-app', 'feat/settings-page'))
      .toMatchObject({ status: 'ready', ref: { number: 42, state: 'open', provider: 'github', path: 'OctoOrg/sample-app' } })
    const body = JSON.parse(String(env.fetch.mock.calls[0][1]?.body))
    expect(body.variables).toEqual({ owner: 'OctoOrg', name: 'sample-app', branch: 'feat/settings-page' })

    const closed = setup({
      responses: [branchResponse([pullNode({ number: 48, state: 'CLOSED' }), pullNode({ number: 42, state: 'MERGED' })])]
    })
    expect(await closed.service.getBranchPullRequest('F:/work/sample-app', 'main'))
      .toMatchObject({ status: 'ready', ref: { number: 48, state: 'closed' } })
  })

  it('ignores pull requests whose head lives in another owner’s fork', async () => {
    const env = setup({
      responses: [branchResponse([pullNode({ number: 411, headRepositoryOwner: { login: 'someone-else' } })])]
    })
    expect(await env.service.getBranchPullRequest('F:/work/sample-app', 'main')).toEqual({ status: 'none' })
  })

  it('looks nothing up on GitHub without a GitHub origin or a branch and shares one cached lookup per origin and branch', async () => {
    const elsewhere = setup({ origin: 'https://gitlab.com/OctoOrg/sample-app.git' })
    expect(await elsewhere.service.getBranchPullRequest('F:/work/sample-app', 'main')).toEqual({ status: 'none' })
    expect(elsewhere.gitlab.readBranchPull).toHaveBeenCalledWith({ provider: 'gitlab', host: 'gitlab.com', path: 'OctoOrg/sample-app' }, 'main')
    const noOrigin = setup({ origin: null })
    expect(await noOrigin.service.getBranchPullRequest('F:/work/sample-app', 'main')).toEqual({ status: 'none' })
    const noBranch = setup()
    expect(await noBranch.service.getBranchPullRequest('F:/work/sample-app', '  ')).toEqual({ status: 'none' })
    for (const env of [elsewhere, noOrigin, noBranch]) expect(env.fetch).not.toHaveBeenCalled()

    const env = setup({ responses: [branchResponse([pullNode()])] })
    await env.service.getBranchPullRequest('F:/work/sample-app', 'main')
    await env.service.getBranchPullRequest('F:/work/sample-app/.craft/worktrees/a', 'main')
    expect(env.fetch).toHaveBeenCalledTimes(1)
  })

  it('looks a branch up in the repository named by the origin, without a checkout', async () => {
    const env = setup({ origin: null, responses: [branchResponse([pullNode({ number: 42, state: 'OPEN' })])] })
    expect(await env.service.getOriginBranchPullRequest('github.com:OctoOrg/sample-app.git', 'feat/settings-page'))
      .toMatchObject({ status: 'ready', ref: { number: 42, provider: 'github', path: 'OctoOrg/sample-app' } })
    const body = JSON.parse(String(env.fetch.mock.calls[0][1]?.body))
    expect(body.variables).toEqual({ owner: 'OctoOrg', name: 'sample-app', branch: 'feat/settings-page' })
  })
})

describe('parseForgeRemoteUrl', () => {
  it('reads https and ssh GitHub remotes', () => {
    const expected = { provider: 'github', host: 'github.com', path: 'OctoOrg/sample-app' }
    expect(parseForgeRemoteUrl('https://github.com/OctoOrg/sample-app.git')).toEqual(expected)
    expect(parseForgeRemoteUrl('https://user:token@github.com/OctoOrg/sample-app')).toEqual(expected)
    expect(parseForgeRemoteUrl('git@github.com:OctoOrg/sample-app.git')).toEqual(expected)
    expect(parseForgeRemoteUrl('ssh://git@github.com/OctoOrg/sample-app.git')).toEqual(expected)
    expect(parseForgeRemoteUrl('ssh://git@github.com:22/OctoOrg/sample-app/')).toEqual(expected)
  })

  it('treats any other host as a GitLab candidate, keeping nested groups', () => {
    const expected = { provider: 'gitlab', host: 'gitlab.example.net', path: 'tools/perf/insight-dashboard' }
    expect(parseForgeRemoteUrl('git@gitlab.example.net:tools/perf/insight-dashboard.git')).toEqual(expected)
    expect(parseForgeRemoteUrl('https://GitLab.example.net/tools/perf/insight-dashboard.git')).toEqual(expected)
    expect(parseForgeRemoteUrl('ssh://git@gitlab.example.net:2222/tools/perf/insight-dashboard.git')).toEqual(expected)
  })

  it('rejects malformed remotes and local paths', () => {
    expect(parseForgeRemoteUrl('https://github.com/OctoOrg')).toBeNull()
    expect(parseForgeRemoteUrl('https://github.com/OctoOrg/sample-app/extra')).toBeNull()
    expect(parseForgeRemoteUrl('git@github.com:OctoOrg/../x.git')).toBeNull()
    expect(parseForgeRemoteUrl('https://gitlab.example.net/dashboard.git')).toBeNull()
    expect(parseForgeRemoteUrl('C:/work/sample-app')).toBeNull()
    expect(parseForgeRemoteUrl('')).toBeNull()
  })
})

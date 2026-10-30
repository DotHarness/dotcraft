import { execFile } from 'child_process'
import { net } from 'electron'
import {
  GITHUB_HOST,
  isForgeRefNumber,
  type ForgeBranchPullResult,
  type ForgeChecks,
  type ForgeIssueState,
  type ForgeIssueSummary,
  type ForgePullState,
  type ForgePullSummary,
  type ForgeRefError,
  type ForgeRefFailure,
  type ForgeRefResult,
  type ForgeRefTarget,
  type ForgeRepo
} from '../shared/forgeReferences'
import type { ForgeReader } from './forgeReader'

type FetchImpl = (url: string, init?: RequestInit) => Promise<Response>

export interface GithubForgeDeps {
  fetch?: FetchImpl
  runGh?: (args: string[]) => Promise<string>
  now?: () => number
}

const GRAPHQL_URL = 'https://api.github.com/graphql'
const REQUEST_TIMEOUT_MS = 10_000
const TOKEN_TTL_MS = 5 * 60_000

const PULL_FIELDS = `
  number title state isDraft createdAt url author { login }
  additions deletions changedFiles reviewDecision mergeable
  commits(last: 1) { nodes { commit { statusCheckRollup { state contexts(first: 1) {
    checkRunCountsByState { state count }
    statusContextCountsByState { state count }
  } } } } }`

const SUMMARY_QUERY = `query($owner: String!, $name: String!, $number: Int!) {
  repository(owner: $owner, name: $name) {
    issueOrPullRequest(number: $number) {
      __typename
      ... on Issue { number title state stateReason createdAt url author { login } }
      ... on PullRequest { ${PULL_FIELDS} }
    }
  }
}`

const BRANCH_QUERY = `query($owner: String!, $name: String!, $branch: String!) {
  repository(owner: $owner, name: $name) {
    pullRequests(headRefName: $branch, first: 5, orderBy: { field: UPDATED_AT, direction: DESC }) {
      nodes { ${PULL_FIELDS} headRepositoryOwner { login } }
    }
  }
}`

const FAILING_CHECK_STATES = new Set(['FAILURE', 'ERROR', 'TIMED_OUT', 'ACTION_REQUIRED', 'STARTUP_FAILURE', 'CANCELLED', 'STALE'])
const PENDING_CHECK_STATES = new Set(['QUEUED', 'IN_PROGRESS', 'PENDING', 'WAITING', 'REQUESTED', 'EXPECTED'])
const PASSED_CHECK_STATES = new Set(['SUCCESS', 'SKIPPED', 'NEUTRAL', 'COMPLETED'])

interface CountByState {
  state?: unknown
  count?: unknown
}

interface PullNode {
  __typename?: string
  number?: unknown
  title?: unknown
  state?: unknown
  isDraft?: unknown
  createdAt?: unknown
  author?: { login?: unknown } | null
  additions?: unknown
  deletions?: unknown
  changedFiles?: unknown
  reviewDecision?: unknown
  mergeable?: unknown
  stateReason?: unknown
  headRepositoryOwner?: { login?: unknown } | null
  commits?: {
    nodes?: Array<{
      commit?: {
        statusCheckRollup?: {
          contexts?: { checkRunCountsByState?: CountByState[]; statusContextCountsByState?: CountByState[] }
        } | null
      }
    } | null>
  }
}

type GraphqlOutcome<T> = { ok: true; data: T } | { ok: false; failure: ForgeRefFailure }

class GhTokenError extends Error {
  constructor(readonly reason: 'notInstalled' | 'auth') {
    super(reason)
  }
}

function pullStateFrom(node: PullNode): ForgePullState {
  if (node.state === 'MERGED') return 'merged'
  if (node.state === 'CLOSED') return 'closed'
  if (node.isDraft === true) return 'draft'
  if (node.reviewDecision === 'CHANGES_REQUESTED') return 'changesRequested'
  if (node.mergeable === 'CONFLICTING') return 'conflicting'
  if (node.reviewDecision === 'APPROVED') return 'approved'
  return 'open'
}

function issueStateFrom(node: PullNode): ForgeIssueState {
  if (node.state === 'OPEN') return 'open'
  return node.stateReason === 'NOT_PLANNED' ? 'notPlanned' : 'completed'
}

function checksFrom(counts: CountByState[]): ForgeChecks {
  let total = 0
  let passed = 0
  let failed = 0
  let pending = 0
  for (const entry of counts) {
    const count = typeof entry.count === 'number' && entry.count > 0 ? entry.count : 0
    const state = typeof entry.state === 'string' ? entry.state : ''
    total += count
    if (FAILING_CHECK_STATES.has(state)) failed += count
    else if (PENDING_CHECK_STATES.has(state)) pending += count
    else if (PASSED_CHECK_STATES.has(state)) passed += count
  }
  const state = failed > 0 ? 'failing' : pending > 0 ? 'pending' : total > 0 ? 'passing' : 'none'
  return { state, counts: { passed, failed, total } }
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function count(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : 0
}

function toPullSummary(node: PullNode, repo: ForgeRepo): ForgePullSummary | null {
  if (!isForgeRefNumber(node.number)) return null
  const contexts = node.commits?.nodes?.[0]?.commit?.statusCheckRollup?.contexts
  return {
    kind: 'pull',
    ...repo,
    number: node.number,
    title: text(node.title),
    author: text(node.author?.login) || 'ghost',
    authorAvatarUrl: null,
    createdAt: text(node.createdAt),
    state: pullStateFrom(node),
    additions: count(node.additions),
    deletions: count(node.deletions),
    changedFiles: count(node.changedFiles),
    checks: checksFrom([...(contexts?.checkRunCountsByState ?? []), ...(contexts?.statusContextCountsByState ?? [])])
  }
}

function toIssueSummary(node: PullNode, repo: ForgeRepo): ForgeIssueSummary | null {
  if (!isForgeRefNumber(node.number)) return null
  return {
    kind: 'issue',
    ...repo,
    number: node.number,
    title: text(node.title),
    author: text(node.author?.login) || 'ghost',
    authorAvatarUrl: null,
    createdAt: text(node.createdAt),
    state: issueStateFrom(node)
  }
}

function failure(error: ForgeRefError, resetsAt?: number): ForgeRefFailure {
  return resetsAt === undefined ? { status: 'error', error } : { status: 'error', error, resetsAt }
}

function resetsAtFrom(headers: Headers): number | undefined {
  const reset = Number(headers.get('x-ratelimit-reset'))
  return Number.isFinite(reset) && reset > 0 ? reset * 1000 : undefined
}

function ownerAndName(repo: ForgeRepo): { owner: string; name: string } {
  const [owner, name] = repo.path.split('/')
  return { owner, name }
}

function defaultRunGh(args: string[]): Promise<string> {
  return new Promise((resolve, reject) => {
    execFile('gh', args, { timeout: REQUEST_TIMEOUT_MS, windowsHide: true }, (error, stdout) => {
      if (error) reject(error)
      else resolve(String(stdout))
    })
  })
}

export function createGithubForge(deps: GithubForgeDeps = {}): ForgeReader {
  const fetchImpl = deps.fetch ?? ((url, init) => net.fetch(url, init))
  const runGh = deps.runGh ?? defaultRunGh
  const now = deps.now ?? Date.now

  let token: { value: string; expiresAt: number } | null = null
  let tokenRequest: Promise<string> | null = null

  function readToken(): Promise<string> {
    if (token && token.expiresAt > now()) return Promise.resolve(token.value)
    tokenRequest ??= runGh(['auth', 'token', '--hostname', GITHUB_HOST])
      .then(
        (stdout) => {
          const value = stdout.trim()
          if (!value) throw new GhTokenError('auth')
          token = { value, expiresAt: now() + TOKEN_TTL_MS }
          return value
        },
        (error: { code?: unknown }) => {
          throw new GhTokenError(error?.code === 'ENOENT' ? 'notInstalled' : 'auth')
        }
      )
      .finally(() => {
        tokenRequest = null
      })
    return tokenRequest
  }

  async function graphql<T>(query: string, variables: Record<string, unknown>): Promise<GraphqlOutcome<T>> {
    let bearer: string
    try {
      bearer = await readToken()
    } catch (error) {
      return { ok: false, failure: failure(error instanceof GhTokenError ? error.reason : 'auth') }
    }
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
    try {
      const response = await fetchImpl(GRAPHQL_URL, {
        method: 'POST',
        headers: {
          Authorization: `bearer ${bearer}`,
          'Content-Type': 'application/json',
          'User-Agent': 'DotCraft'
        },
        body: JSON.stringify({ query, variables }),
        signal: controller.signal
      })
      if (response.status === 401) {
        token = null
        return { ok: false, failure: failure('auth') }
      }
      const exhausted = response.headers.get('x-ratelimit-remaining') === '0'
      if ((response.status === 403 || response.status === 429) && exhausted) {
        return { ok: false, failure: failure('rateLimit', resetsAtFrom(response.headers)) }
      }
      if (!response.ok) return { ok: false, failure: failure('unavailable') }
      const body = (await response.json()) as { data?: T; errors?: Array<{ type?: unknown }> }
      if (body.errors?.some((entry) => entry?.type === 'RATE_LIMITED')) {
        return { ok: false, failure: failure('rateLimit', resetsAtFrom(response.headers)) }
      }
      if ((body.errors?.length ?? 0) > 0 || !body.data) return { ok: false, failure: failure('unavailable') }
      return { ok: true, data: body.data }
    } catch {
      return { ok: false, failure: failure('unavailable') }
    } finally {
      clearTimeout(timer)
    }
  }

  async function readRef(target: ForgeRefTarget): Promise<ForgeRefResult> {
    const repo: ForgeRepo = { provider: 'github', host: GITHUB_HOST, path: target.path }
    const outcome = await graphql<{ repository?: { issueOrPullRequest?: PullNode | null } | null }>(SUMMARY_QUERY, {
      ...ownerAndName(repo),
      number: target.number
    })
    if (!outcome.ok) return outcome.failure
    const node = outcome.data.repository?.issueOrPullRequest
    const ref = node?.__typename === 'PullRequest'
      ? toPullSummary(node, repo)
      : node?.__typename === 'Issue'
        ? toIssueSummary(node, repo)
        : null
    return ref ? { status: 'ready', ref } : failure('unavailable')
  }

  async function readBranchPull(repo: ForgeRepo, branch: string): Promise<ForgeBranchPullResult> {
    const { owner, name } = ownerAndName(repo)
    const outcome = await graphql<{ repository?: { pullRequests?: { nodes?: Array<PullNode | null> } } | null }>(
      BRANCH_QUERY,
      { owner, name, branch }
    )
    if (!outcome.ok) return outcome.failure
    const nodes = (outcome.data.repository?.pullRequests?.nodes ?? []).filter(
      (node): node is PullNode => text(node?.headRepositoryOwner?.login).toLowerCase() === owner.toLowerCase()
    )
    const chosen = nodes.find((node) => node.state === 'OPEN') ?? nodes[0]
    const ref = chosen ? toPullSummary(chosen, repo) : null
    return ref ? { status: 'ready', ref } : { status: 'none' }
  }

  return { readRef, readBranchPull }
}

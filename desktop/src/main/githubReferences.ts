import { execFile } from 'child_process'
import { net } from 'electron'
import { runGitCommand } from './gitCommand'
import {
  isGithubRefNumber,
  isGithubRepoName,
  type GithubBranchPullResult,
  type GithubChecks,
  type GithubIssueState,
  type GithubIssueSummary,
  type GithubPullState,
  type GithubPullSummary,
  type GithubRefError,
  type GithubRefFailure,
  type GithubRefResult,
  type GithubRefTarget,
  type GithubRepo
} from '../shared/githubReferences'

type FetchImpl = (url: string, init?: RequestInit) => Promise<Response>

export interface GithubReferenceDeps {
  fetch?: FetchImpl
  runGh?: (args: string[]) => Promise<string>
  readOrigin?: (workspacePath: string) => Promise<string | null>
  now?: () => number
}

const GRAPHQL_URL = 'https://api.github.com/graphql'
const REQUEST_TIMEOUT_MS = 10_000
const SUCCESS_TTL_MS = 60_000
const BRANCH_SUCCESS_TTL_MS = 20_000
const FAILURE_TTL_MS = 5_000
const TOKEN_TTL_MS = 5 * 60_000
const MAX_BRANCH_LENGTH = 255

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

type GraphqlOutcome<T> = { ok: true; data: T } | { ok: false; failure: GithubRefFailure }

class GhTokenError extends Error {
  constructor(readonly reason: 'notInstalled' | 'auth') {
    super(reason)
  }
}

export function parseGithubRemoteUrl(remote: string): GithubRepo | null {
  const trimmed = remote.trim()
  const match =
    /^(?:https?|git|ssh):\/\/(?:[^@/]+@)?github\.com(?::\d+)?\/([^/]+)\/([^/]+?)(?:\.git)?\/?$/i.exec(trimmed) ??
    /^(?:[^@/]+@)?github\.com:([^/]+)\/([^/]+?)(?:\.git)?\/?$/i.exec(trimmed)
  if (!match) return null
  const [, owner, repo] = match
  return isGithubRepoName(owner) && isGithubRepoName(repo) ? { owner, repo } : null
}

function pullStateFrom(node: PullNode): GithubPullState {
  if (node.state === 'MERGED') return 'merged'
  if (node.state === 'CLOSED') return 'closed'
  if (node.isDraft === true) return 'draft'
  if (node.reviewDecision === 'CHANGES_REQUESTED') return 'changesRequested'
  if (node.mergeable === 'CONFLICTING') return 'conflicting'
  if (node.reviewDecision === 'APPROVED') return 'approved'
  return 'open'
}

function issueStateFrom(node: PullNode): GithubIssueState {
  if (node.state === 'OPEN') return 'open'
  return node.stateReason === 'NOT_PLANNED' ? 'notPlanned' : 'completed'
}

function checksFrom(counts: CountByState[]): GithubChecks {
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
  return { state, passed, failed, total }
}

function hasControlCharacter(value: string): boolean {
  return Array.from(value).some((char) => char.charCodeAt(0) < 32 || char.charCodeAt(0) === 127)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function count(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : 0
}

function toPullSummary(node: PullNode, repo: GithubRepo): GithubPullSummary | null {
  if (!isGithubRefNumber(node.number)) return null
  const contexts = node.commits?.nodes?.[0]?.commit?.statusCheckRollup?.contexts
  return {
    kind: 'pull',
    ...repo,
    number: node.number,
    title: text(node.title),
    author: text(node.author?.login) || 'ghost',
    createdAt: text(node.createdAt),
    state: pullStateFrom(node),
    additions: count(node.additions),
    deletions: count(node.deletions),
    changedFiles: count(node.changedFiles),
    checks: checksFrom([...(contexts?.checkRunCountsByState ?? []), ...(contexts?.statusContextCountsByState ?? [])])
  }
}

function toIssueSummary(node: PullNode, repo: GithubRepo): GithubIssueSummary | null {
  if (!isGithubRefNumber(node.number)) return null
  return {
    kind: 'issue',
    ...repo,
    number: node.number,
    title: text(node.title),
    author: text(node.author?.login) || 'ghost',
    createdAt: text(node.createdAt),
    state: issueStateFrom(node)
  }
}

function failure(error: GithubRefError, resetsAt?: number): GithubRefFailure {
  return resetsAt === undefined ? { status: 'error', error } : { status: 'error', error, resetsAt }
}

function resetsAtFrom(headers: Headers): number | undefined {
  const reset = Number(headers.get('x-ratelimit-reset'))
  return Number.isFinite(reset) && reset > 0 ? reset * 1000 : undefined
}

function defaultRunGh(args: string[]): Promise<string> {
  return new Promise((resolve, reject) => {
    execFile('gh', args, { timeout: REQUEST_TIMEOUT_MS, windowsHide: true }, (error, stdout) => {
      if (error) reject(error)
      else resolve(String(stdout))
    })
  })
}

async function defaultReadOrigin(workspacePath: string): Promise<string | null> {
  try {
    return (await runGitCommand(workspacePath, ['remote', 'get-url', 'origin'])).stdout.trim() || null
  } catch {
    return null
  }
}

interface CacheEntry<T> {
  value: T
  expiresAt: number
}

export function createGithubReferenceService(deps: GithubReferenceDeps = {}) {
  const fetchImpl = deps.fetch ?? ((url, init) => net.fetch(url, init))
  const runGh = deps.runGh ?? defaultRunGh
  const readOrigin = deps.readOrigin ?? defaultReadOrigin
  const now = deps.now ?? Date.now

  let token: { value: string; expiresAt: number } | null = null
  let tokenRequest: Promise<string> | null = null
  const results = new Map<string, CacheEntry<GithubRefResult | GithubBranchPullResult>>()
  const origins = new Map<string, CacheEntry<GithubRepo | null>>()
  const inFlight = new Map<string, Promise<unknown>>()

  function readToken(): Promise<string> {
    if (token && token.expiresAt > now()) return Promise.resolve(token.value)
    tokenRequest ??= runGh(['auth', 'token', '--hostname', 'github.com'])
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

  function cached<T extends GithubRefResult | GithubBranchPullResult>(
    key: string,
    successTtlMs: number,
    load: () => Promise<T>
  ): Promise<T> {
    const entry = results.get(key)
    if (entry && entry.expiresAt > now()) return Promise.resolve(entry.value as T)
    const pending = inFlight.get(key)
    if (pending) return pending as Promise<T>
    const request = load()
      .then((value) => {
        results.set(key, { value, expiresAt: now() + (value.status === 'error' ? FAILURE_TTL_MS : successTtlMs) })
        return value
      })
      .finally(() => inFlight.delete(key))
    inFlight.set(key, request)
    return request
  }

  async function getWorkspaceGithubRepo(workspacePath: string): Promise<GithubRepo | null> {
    if (typeof workspacePath !== 'string' || !workspacePath.trim()) return null
    const key = workspacePath.trim()
    const entry = origins.get(key)
    if (entry && entry.expiresAt > now()) return entry.value
    const remote = await readOrigin(key)
    const repo = remote ? parseGithubRemoteUrl(remote) : null
    origins.set(key, { value: repo, expiresAt: now() + SUCCESS_TTL_MS })
    return repo
  }

  function getReferenceSummary(target: unknown): Promise<GithubRefResult> {
    const candidate = (target ?? {}) as Partial<GithubRefTarget>
    if (
      !isGithubRepoName(candidate.owner) ||
      !isGithubRepoName(candidate.repo) ||
      (candidate.kind !== 'pull' && candidate.kind !== 'issue') ||
      !isGithubRefNumber(candidate.number)
    ) {
      return Promise.resolve(failure('unavailable'))
    }
    const repo = { owner: candidate.owner, repo: candidate.repo }
    const number = candidate.number
    const key = `ref:${repo.owner}/${repo.repo}#${number}`.toLowerCase()
    return cached(key, SUCCESS_TTL_MS, async (): Promise<GithubRefResult> => {
      const outcome = await graphql<{ repository?: { issueOrPullRequest?: PullNode | null } | null }>(SUMMARY_QUERY, {
        owner: repo.owner,
        name: repo.repo,
        number
      })
      if (!outcome.ok) return outcome.failure
      const node = outcome.data.repository?.issueOrPullRequest
      const ref = node?.__typename === 'PullRequest'
        ? toPullSummary(node, repo)
        : node?.__typename === 'Issue'
          ? toIssueSummary(node, repo)
          : null
      return ref ? { status: 'ready', ref } : failure('unavailable')
    })
  }

  async function getBranchPullRequest(workspacePath: string, branch: unknown): Promise<GithubBranchPullResult> {
    const branchName = typeof branch === 'string' ? branch.trim() : ''
    if (!branchName || branchName.length > MAX_BRANCH_LENGTH || hasControlCharacter(branchName)) {
      return { status: 'none' }
    }
    const repo = await getWorkspaceGithubRepo(workspacePath)
    if (!repo) return { status: 'none' }
    const key = `branch:${repo.owner}/${repo.repo}:`.toLowerCase() + branchName
    return cached(key, BRANCH_SUCCESS_TTL_MS, async (): Promise<GithubBranchPullResult> => {
      const outcome = await graphql<{ repository?: { pullRequests?: { nodes?: Array<PullNode | null> } } | null }>(
        BRANCH_QUERY,
        { owner: repo.owner, name: repo.repo, branch: branchName }
      )
      if (!outcome.ok) return outcome.failure
      const nodes = (outcome.data.repository?.pullRequests?.nodes ?? []).filter(
        (node): node is PullNode => text(node?.headRepositoryOwner?.login).toLowerCase() === repo.owner.toLowerCase()
      )
      const chosen = nodes.find((node) => node.state === 'OPEN') ?? nodes[0]
      const ref = chosen ? toPullSummary(chosen, repo) : null
      return ref ? { status: 'ready', ref } : { status: 'none' }
    })
  }

  return { getReferenceSummary, getBranchPullRequest, getWorkspaceGithubRepo }
}

export const githubReferences = createGithubReferenceService()

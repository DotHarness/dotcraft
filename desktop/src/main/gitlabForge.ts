import { execFile } from 'child_process'
import { net } from 'electron'
import {
  isForgeRefNumber,
  type ForgeBranchPullResult,
  type ForgeChecks,
  type ForgeIssueSummary,
  type ForgePullState,
  type ForgePullSummary,
  type ForgeRefError,
  type ForgeRefFailure,
  type ForgeRefResult,
  type ForgeRefTarget,
  type ForgeRepo
} from '../shared/forgeReferences'
import { downloadAvatar, toDataUrl } from './githubProfile'
import type { ForgeReader } from './forgeReader'

export interface GlabRun {
  code: number
  stdout: string
  stderr: string
}

export interface GitlabForgeDeps {
  /** Rejects with `code: 'ENOENT'` when glab is not installed. */
  runGlab?: (args: string[], env: NodeJS.ProcessEnv) => Promise<GlabRun>
  fetchAvatar?: (url: string) => Promise<string | null>
  env?: NodeJS.ProcessEnv
  now?: () => number
}

export interface GitlabForge extends ForgeReader {
  readAvatar(host: string, url: string): Promise<string | null>
}

type HostStatus = 'known' | 'unknown' | 'notInstalled'

const DEFAULT_HOST = 'gitlab.com'
const HOST_ENV_KEYS = ['GITLAB_HOST', 'GITLAB_URI', 'GL_HOST']
// glab applies these to whichever host it talks to, so they must not reach a host they were not set for.
const TOKEN_ENV_KEYS = new Set(['GITLAB_TOKEN', 'GITLAB_ACCESS_TOKEN', 'OAUTH_TOKEN'])
const REQUEST_TIMEOUT_MS = 10_000
const HOST_STATUS_TTL_MS = 60_000
const MAX_CONCURRENT_RUNS = 4
const MAX_OUTPUT_BYTES = 8 * 1024 * 1024
const MAX_AVATAR_CACHE = 64
const BRANCH_PAGE_SIZE = 20

interface GitlabUser {
  name?: unknown
  username?: unknown
  avatar_url?: unknown
}

interface GitlabItem {
  iid?: unknown
  title?: unknown
  state?: unknown
  draft?: unknown
  work_in_progress?: unknown
  has_conflicts?: unknown
  created_at?: unknown
  changes_count?: unknown
  author?: GitlabUser | null
  head_pipeline?: { status?: unknown } | null
  source_project_id?: unknown
  target_project_id?: unknown
}

type ApiOutcome<T> = { ok: true; body: T } | { ok: false; failure: ForgeRefFailure }

function failure(error: ForgeRefError): ForgeRefFailure {
  return { status: 'error', error }
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function pullStateFrom(item: GitlabItem): ForgePullState {
  if (item.state === 'merged') return 'merged'
  if (item.state === 'closed') return 'closed'
  if (item.draft === true || item.work_in_progress === true) return 'draft'
  if (item.has_conflicts === true) return 'conflicting'
  return 'open'
}

function checksFrom(status: unknown): ForgeChecks {
  switch (status) {
    case 'success':
      return { state: 'passing', counts: null }
    case 'failed':
    case 'canceled':
    case 'canceling':
      return { state: 'failing', counts: null }
    case 'created':
    case 'waiting_for_resource':
    case 'preparing':
    case 'pending':
    case 'running':
    case 'manual':
    case 'scheduled':
      return { state: 'pending', counts: null }
    default:
      return { state: 'none', counts: null }
  }
}

/** GitLab reports `changes_count` as a string and caps large diffs as `"1000+"`. */
function changedFilesFrom(value: unknown): number | null {
  const parsed = typeof value === 'string' ? parseInt(value, 10) : typeof value === 'number' ? value : NaN
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function createdAtFrom(value: unknown): string {
  const parsed = typeof value === 'string' ? Date.parse(value) : NaN
  return Number.isNaN(parsed) ? '' : new Date(parsed).toISOString()
}

function isGravatarHost(hostname: string): boolean {
  return hostname === 'gravatar.com' || hostname.endsWith('.gravatar.com')
}

/** Only the instance itself or Gravatar may serve an avatar, so a crafted author cannot point Desktop elsewhere. */
export function gitlabAvatarUrl(value: unknown, host: string): string | null {
  if (typeof value !== 'string' || !value || value.length > 2048) return null
  try {
    const url = new URL(value, `https://${host}`)
    if (url.protocol !== 'https:' || url.username || url.password) return null
    return url.hostname === host || isGravatarHost(url.hostname) ? url.href : null
  } catch {
    return null
  }
}

function authorFrom(user: GitlabUser | null | undefined, host: string): { author: string; authorAvatarUrl: string | null } {
  return {
    author: text(user?.name) || text(user?.username) || 'ghost',
    authorAvatarUrl: gitlabAvatarUrl(user?.avatar_url, host)
  }
}

function toPullSummary(item: GitlabItem, repo: ForgeRepo): ForgePullSummary | null {
  if (!isForgeRefNumber(item.iid)) return null
  return {
    kind: 'pull',
    ...repo,
    number: item.iid,
    title: text(item.title),
    ...authorFrom(item.author, repo.host),
    createdAt: createdAtFrom(item.created_at),
    state: pullStateFrom(item),
    additions: null,
    deletions: null,
    changedFiles: changedFilesFrom(item.changes_count),
    checks: checksFrom(item.head_pipeline?.status)
  }
}

function toIssueSummary(item: GitlabItem, repo: ForgeRepo): ForgeIssueSummary | null {
  if (!isForgeRefNumber(item.iid)) return null
  return {
    kind: 'issue',
    ...repo,
    number: item.iid,
    title: text(item.title),
    ...authorFrom(item.author, repo.host),
    createdAt: createdAtFrom(item.created_at),
    state: item.state === 'closed' ? 'completed' : 'open'
  }
}

function failureFromStatus(status: number): ForgeRefError {
  if (status === 401) return 'auth'
  if (status === 429) return 'rateLimit'
  return 'unavailable'
}

function defaultRunGlab(args: string[], env: NodeJS.ProcessEnv): Promise<GlabRun> {
  return new Promise((resolve, reject) => {
    execFile(
      'glab',
      args,
      { env, timeout: REQUEST_TIMEOUT_MS, windowsHide: true, maxBuffer: MAX_OUTPUT_BYTES },
      (error, stdout, stderr) => {
        const code = (error as { code?: unknown } | null)?.code
        if (code === 'ENOENT') {
          reject(error)
          return
        }
        resolve({ code: error ? (typeof code === 'number' ? code : 1) : 0, stdout: String(stdout), stderr: String(stderr) })
      }
    )
  })
}

async function defaultFetchAvatar(url: string): Promise<string | null> {
  const avatar = await downloadAvatar(url, net.fetch.bind(net))
  return avatar ? toDataUrl(avatar.bytes, avatar.mediaType) : null
}

export function createGitlabForge(deps: GitlabForgeDeps = {}): GitlabForge {
  const runGlab = deps.runGlab ?? defaultRunGlab
  const fetchAvatar = deps.fetchAvatar ?? defaultFetchAvatar
  const baseEnv = deps.env ?? process.env
  const now = deps.now ?? Date.now

  const hostStatuses = new Map<string, { status: HostStatus; at: number }>()
  const hostStatusRequests = new Map<string, Promise<HostStatus>>()
  const avatars = new Map<string, Promise<string | null>>()
  const waiting: Array<() => void> = []
  let running = 0

  function defaultHost(): string {
    for (const key of HOST_ENV_KEYS) {
      const value = baseEnv[key]?.trim()
      if (!value) continue
      try {
        return new URL(value.includes('://') ? value : `https://${value}`).hostname.toLowerCase()
      } catch {
        return ''
      }
    }
    return DEFAULT_HOST
  }

  function envFor(host: string): NodeJS.ProcessEnv {
    const env: NodeJS.ProcessEnv = { ...baseEnv, NO_COLOR: '1', GLAB_SEND_TELEMETRY: '0' }
    if (host !== defaultHost()) {
      for (const key of Object.keys(env)) if (TOKEN_ENV_KEYS.has(key.toUpperCase())) delete env[key]
    }
    return env
  }

  async function limited<T>(run: () => Promise<T>): Promise<T> {
    if (running >= MAX_CONCURRENT_RUNS) await new Promise<void>((resolve) => waiting.push(resolve))
    else running++
    try {
      return await run()
    } finally {
      const next = waiting.shift()
      if (next) next()
      else running--
    }
  }

  async function configToken(host: string, scope: string[]): Promise<string> {
    const result = await limited(() => runGlab(['config', 'get', 'token', ...scope], envFor(host)))
    return result.code === 0 ? result.stdout.trim() : ''
  }

  // glab falls back to the global token for hosts it has no entry for, so a host token equal to it proves nothing.
  async function readHostStatus(host: string): Promise<HostStatus> {
    try {
      const token = await configToken(host, ['--host', host])
      if (!token) return 'unknown'
      if (host === defaultHost()) return 'known'
      return token !== (await configToken(host, ['--global'])) ? 'known' : 'unknown'
    } catch {
      return 'notInstalled'
    }
  }

  function hostStatus(host: string): Promise<HostStatus> {
    const cached = hostStatuses.get(host)
    if (cached && now() - cached.at < HOST_STATUS_TTL_MS) return Promise.resolve(cached.status)
    let request = hostStatusRequests.get(host)
    if (!request) {
      request = readHostStatus(host)
        .then((status) => {
          hostStatuses.set(host, { status, at: now() })
          return status
        })
        .finally(() => hostStatusRequests.delete(host))
      hostStatusRequests.set(host, request)
    }
    return request
  }

  async function api<T>(host: string, endpoint: string): Promise<ApiOutcome<T>> {
    let result: GlabRun
    try {
      result = await limited(() => runGlab(['api', '--hostname', host, endpoint], envFor(host)))
    } catch {
      return { ok: false, failure: failure('notInstalled') }
    }
    if (result.code === 0) {
      try {
        return { ok: true, body: JSON.parse(result.stdout) as T }
      } catch {
        return { ok: false, failure: failure('unavailable') }
      }
    }
    const status = Number(/\bHTTP (\d{3})\b/.exec(result.stderr)?.[1] ?? 0)
    if (status === 401) hostStatuses.delete(host)
    return { ok: false, failure: failure(failureFromStatus(status)) }
  }

  function projectEndpoint(repo: ForgeRepo): string {
    return `projects/${encodeURIComponent(repo.path)}`
  }

  async function readMergeRequest(repo: ForgeRepo, number: number): Promise<ApiOutcome<ForgePullSummary>> {
    const outcome = await api<GitlabItem>(repo.host, `${projectEndpoint(repo)}/merge_requests/${number}`)
    if (!outcome.ok) return outcome
    const ref = toPullSummary(outcome.body, repo)
    return ref ? { ok: true, body: ref } : { ok: false, failure: failure('unavailable') }
  }

  async function readRef(target: ForgeRefTarget): Promise<ForgeRefResult> {
    const status = await hostStatus(target.host)
    if (status === 'notInstalled') return failure('notInstalled')
    if (status === 'unknown') return failure('auth')
    const repo: ForgeRepo = { provider: 'gitlab', host: target.host, path: target.path }
    if (target.kind === 'pull') {
      const outcome = await readMergeRequest(repo, target.number)
      return outcome.ok ? { status: 'ready', ref: outcome.body } : outcome.failure
    }
    const outcome = await api<GitlabItem>(repo.host, `${projectEndpoint(repo)}/issues/${target.number}`)
    if (!outcome.ok) return outcome.failure
    const ref = toIssueSummary(outcome.body, repo)
    return ref ? { status: 'ready', ref } : failure('unavailable')
  }

  // The list endpoint omits the pipeline and file count, so the chosen merge request is read again in full.
  async function readBranchPull(repo: ForgeRepo, branch: string): Promise<ForgeBranchPullResult> {
    if ((await hostStatus(repo.host)) !== 'known') return { status: 'none' }
    const query = `source_branch=${encodeURIComponent(branch)}&order_by=updated_at&sort=desc&per_page=${BRANCH_PAGE_SIZE}`
    const listed = await api<GitlabItem[]>(repo.host, `${projectEndpoint(repo)}/merge_requests?${query}`)
    if (!listed.ok) return listed.failure
    const own = (Array.isArray(listed.body) ? listed.body : []).filter(
      (item) => item && item.source_project_id === item.target_project_id && isForgeRefNumber(item.iid)
    )
    const chosen = own.find((item) => item.state === 'opened') ?? own[0]
    if (!chosen) return { status: 'none' }
    const outcome = await readMergeRequest(repo, chosen.iid as number)
    return outcome.ok ? { status: 'ready', ref: outcome.body } : outcome.failure
  }

  async function readAvatar(host: string, url: string): Promise<string | null> {
    const safe = gitlabAvatarUrl(url, host)
    if (!safe || (await hostStatus(host)) !== 'known') return null
    let request = avatars.get(safe)
    if (!request) {
      if (avatars.size >= MAX_AVATAR_CACHE) avatars.clear()
      request = fetchAvatar(safe).catch(() => null)
      avatars.set(safe, request)
    }
    return request
  }

  return { readRef, readBranchPull, readAvatar }
}

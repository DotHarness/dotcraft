export type ForgeProvider = 'github' | 'gitlab'

export type ForgeRefKind = 'pull' | 'issue'

export type ForgePullState =
  | 'open'
  | 'draft'
  | 'approved'
  | 'changesRequested'
  | 'conflicting'
  | 'merged'
  | 'closed'

export type ForgeIssueState = 'open' | 'completed' | 'notPlanned'

export type ForgeChecksState = 'passing' | 'failing' | 'pending' | 'none'

export interface ForgeCheckCounts {
  passed: number
  failed: number
  total: number
}

export interface ForgeChecks {
  state: ForgeChecksState
  /** Null when the forge reports only an overall status, as a GitLab pipeline does. */
  counts: ForgeCheckCounts | null
}

export type ForgeRefError = 'notInstalled' | 'auth' | 'rateLimit' | 'unavailable'

export interface ForgeRepo {
  provider: ForgeProvider
  host: string
  /** `owner/repo` on GitHub; the full `group/…/project` path on GitLab. */
  path: string
}

export interface ForgeRefTarget extends ForgeRepo {
  kind: ForgeRefKind
  number: number
}

interface ForgeRefSummaryBase extends ForgeRefTarget {
  title: string
  author: string
  /** GitLab only; GitHub avatars are looked up by the author's login. */
  authorAvatarUrl: string | null
  createdAt: string
}

export interface ForgePullSummary extends ForgeRefSummaryBase {
  kind: 'pull'
  state: ForgePullState
  additions: number | null
  deletions: number | null
  changedFiles: number | null
  checks: ForgeChecks
}

export interface ForgeIssueSummary extends ForgeRefSummaryBase {
  kind: 'issue'
  state: ForgeIssueState
}

export type ForgeRefSummary = ForgePullSummary | ForgeIssueSummary

export type ForgeRefFailure = { status: 'error'; error: ForgeRefError; resetsAt?: number }

export type ForgeRefResult = { status: 'ready'; ref: ForgeRefSummary } | ForgeRefFailure

export type ForgeBranchPullResult = { status: 'ready'; ref: ForgePullSummary } | { status: 'none' } | ForgeRefFailure

export const GITHUB_HOST = 'github.com'

const PATH_SEGMENT_PATTERN = /^[\w.-]+$/
const HOST_PATTERN = /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*$/
const GITHUB_REF_PATH_PATTERN = /^\/([^/]+\/[^/]+)\/(pull|issues)\/([1-9]\d{0,9})\/?$/
const GITLAB_REF_PATH_PATTERN = /^\/([^?#]+?)\/-\/(merge_requests|issues)\/([1-9]\d{0,9})\/?$/
const MAX_SEGMENT_LENGTH = 255
const MAX_PATH_LENGTH = 1024
const MAX_HOST_LENGTH = 253
const MAX_REF_NUMBER = 2_147_483_647
const TITLE_LIMIT = 32
const TITLE_MIN_CUT = 16

export function isForgeProvider(value: unknown): value is ForgeProvider {
  return value === 'github' || value === 'gitlab'
}

export function isForgeHost(value: unknown): value is string {
  return typeof value === 'string' && value.length <= MAX_HOST_LENGTH && HOST_PATTERN.test(value)
}

function isPathSegment(value: string): boolean {
  return value !== '.' && value !== '..' && value.length <= MAX_SEGMENT_LENGTH && PATH_SEGMENT_PATTERN.test(value)
}

export function isForgeRepoPath(provider: ForgeProvider, value: unknown): value is string {
  if (typeof value !== 'string' || value.length > MAX_PATH_LENGTH) return false
  const segments = value.split('/')
  const shaped = provider === 'github' ? segments.length === 2 : segments.length >= 2
  return shaped && segments.every(isPathSegment)
}

export function isForgeRepo(value: unknown): value is ForgeRepo {
  const candidate = (value ?? {}) as Partial<ForgeRepo>
  if (!isForgeProvider(candidate.provider) || !isForgeHost(candidate.host)) return false
  if (candidate.provider === 'github' && candidate.host !== GITHUB_HOST) return false
  return isForgeRepoPath(candidate.provider, candidate.path)
}

export function isForgeRefNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 && value <= MAX_REF_NUMBER
}

export function isForgeRefTarget(value: unknown): value is ForgeRefTarget {
  const { kind, number } = (value ?? {}) as Partial<ForgeRefTarget>
  return (kind === 'pull' || kind === 'issue') && isForgeRefNumber(number) && isForgeRepo(value)
}

export function parseForgeRefUrl(href: string | null | undefined): ForgeRefTarget | null {
  const trimmed = href?.trim() ?? ''
  if (!/^https:\/\//i.test(trimmed) || /[?#]/.test(trimmed)) return null
  let url: URL
  try {
    url = new URL(trimmed)
  } catch {
    return null
  }
  if (url.port || url.username || url.password) return null
  const host = url.hostname
  const github = host === GITHUB_HOST
  const match = (github ? GITHUB_REF_PATH_PATTERN : GITLAB_REF_PATH_PATTERN).exec(url.pathname)
  if (!match) return null
  const [, path, segment, digits] = match
  const target: ForgeRefTarget = {
    provider: github ? 'github' : 'gitlab',
    host,
    path,
    kind: segment === 'issues' ? 'issue' : 'pull',
    number: Number(digits)
  }
  return isForgeRefTarget(target) ? target : null
}

export function forgeRefUrl(target: ForgeRefTarget): string {
  if (target.provider === 'github') {
    return `https://${GITHUB_HOST}/${target.path}/${target.kind === 'pull' ? 'pull' : 'issues'}/${target.number}`
  }
  return `https://${target.host}/${target.path}/-/${target.kind === 'pull' ? 'merge_requests' : 'issues'}/${target.number}`
}

export function forgeChangesUrl(target: ForgeRefTarget): string {
  return `${forgeRefUrl(target)}/${target.provider === 'github' ? 'files' : 'diffs'}`
}

export function forgeRefNumberLabel(target: Pick<ForgeRefTarget, 'provider' | 'kind' | 'number'>): string {
  return `${target.provider === 'gitlab' && target.kind === 'pull' ? '!' : '#'}${target.number}`
}

export function sameForgeRepo(a: ForgeRepo | null | undefined, b: ForgeRepo | null | undefined): boolean {
  return Boolean(
    a && b &&
    a.provider === b.provider &&
    a.host.toLowerCase() === b.host.toLowerCase() &&
    a.path.toLowerCase() === b.path.toLowerCase()
  )
}

export function forgeRefShortLabel(target: ForgeRefTarget, workspaceRepo: ForgeRepo | null | undefined): string {
  const number = forgeRefNumberLabel(target)
  return sameForgeRepo(target, workspaceRepo) ? number : `${target.path}${number}`
}

export function usesForgeRefLabel(text: string, href: string, target: ForgeRefTarget): boolean {
  const label = text.trim()
  if (!label || label === href.trim()) return true
  const number = forgeRefNumberLabel(target)
  if (label === number) return true
  return label.toLowerCase() === `${target.path}${number}`.toLowerCase()
}

export function truncateForgeRefTitle(title: string): string {
  if (title.length <= TITLE_LIMIT) return title
  const head = title.slice(0, TITLE_LIMIT)
  const boundary = Math.max(head.lastIndexOf(' '), head.lastIndexOf('-'), head.lastIndexOf('/'))
  const cut = boundary >= TITLE_MIN_CUT ? head.slice(0, boundary) : head
  return `${cut.replace(/[\s.,:;·-]+$/, '')}…`
}

export type GithubRefKind = 'pull' | 'issue'

export type GithubPullState =
  | 'open'
  | 'draft'
  | 'approved'
  | 'changesRequested'
  | 'conflicting'
  | 'merged'
  | 'closed'

export type GithubIssueState = 'open' | 'completed' | 'notPlanned'

export type GithubChecksState = 'passing' | 'failing' | 'pending' | 'none'

export interface GithubChecks {
  state: GithubChecksState
  passed: number
  failed: number
  total: number
}

export type GithubRefError = 'notInstalled' | 'auth' | 'rateLimit' | 'unavailable'

export interface GithubRepo {
  owner: string
  repo: string
}

export interface GithubRefTarget extends GithubRepo {
  kind: GithubRefKind
  number: number
}

interface GithubRefSummaryBase extends GithubRefTarget {
  title: string
  author: string
  createdAt: string
}

export interface GithubPullSummary extends GithubRefSummaryBase {
  kind: 'pull'
  state: GithubPullState
  additions: number
  deletions: number
  changedFiles: number
  checks: GithubChecks
}

export interface GithubIssueSummary extends GithubRefSummaryBase {
  kind: 'issue'
  state: GithubIssueState
}

export type GithubRefSummary = GithubPullSummary | GithubIssueSummary

export type GithubRefFailure = { status: 'error'; error: GithubRefError; resetsAt?: number }

export type GithubRefResult = { status: 'ready'; ref: GithubRefSummary } | GithubRefFailure

export type GithubBranchPullResult = { status: 'ready'; ref: GithubPullSummary } | { status: 'none' } | GithubRefFailure

const REPO_NAME_PATTERN = /^[\w.-]+$/
const REF_PATH_PATTERN = /^\/([^/]+)\/([^/]+)\/(pull|issues)\/([1-9]\d{0,9})\/?$/
const MAX_REF_NUMBER = 2_147_483_647
const TITLE_LIMIT = 32
const TITLE_MIN_CUT = 16

export function isGithubRepoName(value: unknown): value is string {
  return typeof value === 'string' && value !== '.' && value !== '..' && value.length <= 100 && REPO_NAME_PATTERN.test(value)
}

export function isGithubRefNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 && value <= MAX_REF_NUMBER
}

export function parseGithubRefUrl(href: string | null | undefined): GithubRefTarget | null {
  const trimmed = href?.trim() ?? ''
  if (!/^https:\/\//i.test(trimmed) || /[?#]/.test(trimmed)) return null
  let url: URL
  try {
    url = new URL(trimmed)
  } catch {
    return null
  }
  if (url.hostname !== 'github.com' || url.port || url.username || url.password) return null
  const match = REF_PATH_PATTERN.exec(url.pathname)
  if (!match) return null
  const [, owner, repo, segment, digits] = match
  const number = Number(digits)
  if (!isGithubRepoName(owner) || !isGithubRepoName(repo) || !isGithubRefNumber(number)) return null
  return { owner, repo, kind: segment === 'pull' ? 'pull' : 'issue', number }
}

export function githubRefUrl(target: GithubRefTarget): string {
  return `https://github.com/${target.owner}/${target.repo}/${target.kind === 'pull' ? 'pull' : 'issues'}/${target.number}`
}

export function sameGithubRepo(a: GithubRepo | null | undefined, b: GithubRepo | null | undefined): boolean {
  return Boolean(a && b && a.owner.toLowerCase() === b.owner.toLowerCase() && a.repo.toLowerCase() === b.repo.toLowerCase())
}

export function githubRefShortLabel(target: GithubRefTarget, workspaceRepo: GithubRepo | null | undefined): string {
  return sameGithubRepo(target, workspaceRepo) ? `#${target.number}` : `${target.owner}/${target.repo}#${target.number}`
}

export function usesGithubRefLabel(text: string, href: string, target: GithubRefTarget): boolean {
  const label = text.trim()
  if (!label || label === href.trim()) return true
  if (label === `#${target.number}`) return true
  return label.toLowerCase() === `${target.owner}/${target.repo}#${target.number}`.toLowerCase()
}

export function truncateGithubRefTitle(title: string): string {
  if (title.length <= TITLE_LIMIT) return title
  const head = title.slice(0, TITLE_LIMIT)
  const boundary = Math.max(head.lastIndexOf(' '), head.lastIndexOf('-'), head.lastIndexOf('/'))
  const cut = boundary >= TITLE_MIN_CUT ? head.slice(0, boundary) : head
  return `${cut.replace(/[\s.,:;·-]+$/, '')}…`
}

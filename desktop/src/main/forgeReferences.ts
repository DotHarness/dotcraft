import { runGitCommand } from './gitCommand'
import {
  GITHUB_HOST,
  isForgeHost,
  isForgeRefTarget,
  isForgeRepo,
  type ForgeBranchPullResult,
  type ForgeProvider,
  type ForgeRefResult,
  type ForgeRefTarget,
  type ForgeRepo
} from '../shared/forgeReferences'
import type { ForgeReader } from './forgeReader'
import { createGithubForge } from './githubForge'
import { createGitlabForge, type GitlabForge } from './gitlabForge'

export interface ForgeReferenceDeps {
  github?: ForgeReader
  gitlab?: GitlabForge
  readOrigin?: (workspacePath: string) => Promise<string | null>
  now?: () => number
}

const SUCCESS_TTL_MS = 60_000
const BRANCH_SUCCESS_TTL_MS = 20_000
const FAILURE_TTL_MS = 5_000
const MAX_BRANCH_LENGTH = 255

const URL_REMOTE_PATTERN = /^(?:https?|git|ssh):\/\/(?:[^@/]+@)?([^/:]+)(?::\d+)?\/(.+?)(?:\.git)?\/?$/i
const SCP_REMOTE_PATTERN = /^(?:[^@/]+@)?([^/:]+):(?!\/)(.+?)(?:\.git)?\/?$/

/** `github.com` remotes are GitHub; any other host is a GitLab candidate that only `glab` can confirm. */
export function parseForgeRemoteUrl(remote: string): ForgeRepo | null {
  const trimmed = remote.trim()
  const match = URL_REMOTE_PATTERN.exec(trimmed) ?? SCP_REMOTE_PATTERN.exec(trimmed)
  if (!match) return null
  const host = match[1].toLowerCase()
  if (!isForgeHost(host)) return null
  const repo: ForgeRepo = { provider: host === GITHUB_HOST ? 'github' : 'gitlab', host, path: match[2] }
  return isForgeRepo(repo) ? repo : null
}

function hasControlCharacter(value: string): boolean {
  return Array.from(value).some((char) => char.charCodeAt(0) < 32 || char.charCodeAt(0) === 127)
}

function validBranch(branch: unknown): string | null {
  const branchName = typeof branch === 'string' ? branch.trim() : ''
  return !branchName || branchName.length > MAX_BRANCH_LENGTH || hasControlCharacter(branchName) ? null : branchName
}

function repoKey(repo: ForgeRepo): string {
  return `${repo.provider}|${repo.host}/${repo.path}`.toLowerCase()
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

export function createForgeReferenceService(deps: ForgeReferenceDeps = {}) {
  const gitlab = deps.gitlab ?? createGitlabForge()
  const readers: Record<ForgeProvider, ForgeReader> = { github: deps.github ?? createGithubForge(), gitlab }
  const readOrigin = deps.readOrigin ?? defaultReadOrigin
  const now = deps.now ?? Date.now

  const results = new Map<string, CacheEntry<ForgeRefResult | ForgeBranchPullResult>>()
  const origins = new Map<string, CacheEntry<ForgeRepo | null>>()
  const inFlight = new Map<string, Promise<unknown>>()

  function cached<T extends ForgeRefResult | ForgeBranchPullResult>(
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

  async function getWorkspaceRepo(workspacePath: string): Promise<ForgeRepo | null> {
    if (typeof workspacePath !== 'string' || !workspacePath.trim()) return null
    const key = workspacePath.trim()
    const entry = origins.get(key)
    if (entry && entry.expiresAt > now()) return entry.value
    const remote = await readOrigin(key)
    const repo = remote ? parseForgeRemoteUrl(remote) : null
    origins.set(key, { value: repo, expiresAt: now() + SUCCESS_TTL_MS })
    return repo
  }

  function getReferenceSummary(target: unknown): Promise<ForgeRefResult> {
    if (!isForgeRefTarget(target)) return Promise.resolve({ status: 'error', error: 'unavailable' })
    const valid: ForgeRefTarget = {
      provider: target.provider,
      host: target.host,
      path: target.path,
      kind: target.kind,
      number: target.number
    }
    const key = `ref:${repoKey(valid)}${valid.kind === 'pull' ? '!' : '#'}${valid.number}`
    return cached(key, SUCCESS_TTL_MS, () => readers[valid.provider].readRef(valid))
  }

  function repoBranchPullRequest(repo: ForgeRepo, branchName: string): Promise<ForgeBranchPullResult> {
    return cached(`branch:${repoKey(repo)}:${branchName}`, BRANCH_SUCCESS_TTL_MS, () =>
      readers[repo.provider].readBranchPull(repo, branchName)
    )
  }

  async function getBranchPullRequest(workspacePath: string, branch: unknown): Promise<ForgeBranchPullResult> {
    const branchName = validBranch(branch)
    if (!branchName) return { status: 'none' }
    const repo = await getWorkspaceRepo(workspacePath)
    return repo ? repoBranchPullRequest(repo, branchName) : { status: 'none' }
  }

  function getOriginBranchPullRequest(originUrl: unknown, branch: unknown): Promise<ForgeBranchPullResult> {
    const branchName = validBranch(branch)
    const repo = typeof originUrl === 'string' ? parseForgeRemoteUrl(originUrl) : null
    return branchName && repo ? repoBranchPullRequest(repo, branchName) : Promise.resolve({ status: 'none' })
  }

  function getAuthorAvatar(host: unknown, url: unknown): Promise<string | null> {
    if (!isForgeHost(host) || typeof url !== 'string') return Promise.resolve(null)
    return gitlab.readAvatar(host, url)
  }

  return { getReferenceSummary, getBranchPullRequest, getOriginBranchPullRequest, getWorkspaceRepo, getAuthorAvatar }
}

export const forgeReferences = createForgeReferenceService()

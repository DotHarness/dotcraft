import { app, net } from 'electron'
import { mkdir, readFile, writeFile } from 'fs/promises'
import { join } from 'path'

/**
 * Resolves a public GitHub identity (display name + avatar) for the Profile page.
 *
 * Runs in the main process — unlike the renderer it is not constrained by the
 * production Content-Security-Policy, so it can reach GitHub directly. Results are
 * cached under `userData/profile-cache/` so the avatar survives offline launches and
 * we avoid hammering GitHub's anonymous rate limit. The avatar is returned as a
 * `data:` URL, which the renderer CSP already allows (`img-src ... data:`), so no
 * CSP relaxation or custom protocol authorization is required.
 */

export interface GitHubIdentity {
  login: string
  name: string | null
  /** Avatar as a `data:` URL, or null when unavailable (renderer falls back to initials). */
  avatarDataUrl: string | null
}

/** GitHub logins are 1–39 chars of alphanumerics or single non-leading/trailing hyphens. */
const GITHUB_USERNAME_PATTERN = /^[a-zA-Z\d](?:[a-zA-Z\d]|-(?=[a-zA-Z\d])){0,38}$/
const CACHE_TTL_MS = 7 * 24 * 60 * 60 * 1000
const FETCH_TIMEOUT_MS = 10_000
const MAX_AVATAR_BYTES = 2 * 1024 * 1024
const AVATAR_SIZE = 160

type FetchImpl = (url: string, init?: RequestInit) => Promise<Response>

interface CachedMeta {
  login: string
  name: string | null
  avatarMediaType: string | null
  fetchedAt: number
}

function cacheDir(): string {
  return join(app.getPath('userData'), 'profile-cache')
}

/**
 * Returns the cached identity for a GitHub login, refreshing from GitHub when the
 * cache is missing or stale. On network failure a stale cache entry is returned when
 * available; otherwise null. Validates the username before any network/disk access.
 */
export async function getGitHubIdentity(
  rawUsername: string,
  fetchImpl: FetchImpl = net.fetch.bind(net)
): Promise<GitHubIdentity | null> {
  const login = (rawUsername ?? '').trim()
  if (!login || !GITHUB_USERNAME_PATTERN.test(login)) {
    return null
  }

  const key = login.toLowerCase()
  const cached = await readCacheEntry(key)
  if (cached && Date.now() - cached.meta.fetchedAt < CACHE_TTL_MS) {
    return { login: cached.meta.login, name: cached.meta.name, avatarDataUrl: cached.dataUrl }
  }

  try {
    const user = await fetchGitHubUser(login, fetchImpl)
    const avatar = await downloadAvatar(user.avatarUrl, fetchImpl)

    const dir = cacheDir()
    await mkdir(dir, { recursive: true })

    let dataUrl = cached?.dataUrl ?? null
    let avatarMediaType = cached?.meta.avatarMediaType ?? null
    if (avatar) {
      await writeFile(join(dir, `${key}.avatar`), avatar.bytes)
      dataUrl = toDataUrl(avatar.bytes, avatar.mediaType)
      avatarMediaType = avatar.mediaType
    }

    const meta: CachedMeta = { login: user.login, name: user.name, avatarMediaType, fetchedAt: Date.now() }
    await writeFile(join(dir, `${key}.json`), JSON.stringify(meta), 'utf8')
    return { login: user.login, name: user.name, avatarDataUrl: dataUrl }
  } catch {
    // Offline / GitHub error: fall back to a stale cache entry when we have one.
    if (cached) {
      return { login: cached.meta.login, name: cached.meta.name, avatarDataUrl: cached.dataUrl }
    }
    return null
  }
}

async function readCacheEntry(key: string): Promise<{ meta: CachedMeta; dataUrl: string | null } | null> {
  try {
    const metaRaw = await readFile(join(cacheDir(), `${key}.json`), 'utf8')
    const parsed = JSON.parse(metaRaw) as Partial<CachedMeta>
    if (!parsed || typeof parsed.login !== 'string' || typeof parsed.fetchedAt !== 'number') {
      return null
    }
    const meta: CachedMeta = {
      login: parsed.login,
      name: typeof parsed.name === 'string' ? parsed.name : null,
      avatarMediaType: typeof parsed.avatarMediaType === 'string' ? parsed.avatarMediaType : null,
      fetchedAt: parsed.fetchedAt
    }

    let dataUrl: string | null = null
    if (meta.avatarMediaType) {
      try {
        const bytes = await readFile(join(cacheDir(), `${key}.avatar`))
        dataUrl = toDataUrl(bytes, meta.avatarMediaType)
      } catch {
        dataUrl = null
      }
    }
    return { meta, dataUrl }
  } catch {
    return null
  }
}

async function fetchGitHubUser(
  login: string,
  fetchImpl: FetchImpl
): Promise<{ login: string; name: string | null; avatarUrl: string }> {
  const fallbackAvatar = `https://github.com/${encodeURIComponent(login)}.png`
  const res = await withTimeout((signal) =>
    fetchImpl(`https://api.github.com/users/${encodeURIComponent(login)}`, {
      headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'DotCraft' },
      signal
    })
  )
  if (!res.ok) {
    throw new Error(`GitHub user lookup failed: ${res.status}`)
  }
  const data = (await res.json()) as { login?: unknown; name?: unknown; avatar_url?: unknown }
  return {
    login: typeof data.login === 'string' && data.login.trim() !== '' ? data.login : login,
    name: typeof data.name === 'string' && data.name.trim() !== '' ? data.name : null,
    avatarUrl:
      typeof data.avatar_url === 'string' && data.avatar_url.trim() !== '' ? data.avatar_url : fallbackAvatar
  }
}

async function downloadAvatar(
  url: string,
  fetchImpl: FetchImpl
): Promise<{ bytes: Buffer; mediaType: string } | null> {
  const sized = url.includes('?') ? `${url}&s=${AVATAR_SIZE}` : `${url}?s=${AVATAR_SIZE}`
  try {
    const res = await withTimeout((signal) =>
      fetchImpl(sized, { headers: { 'User-Agent': 'DotCraft' }, signal })
    )
    if (!res.ok) {
      return null
    }
    const mediaType = (res.headers.get('content-type') ?? '').split(';')[0].trim().toLowerCase()
    if (!mediaType.startsWith('image/')) {
      return null
    }
    const buffer = Buffer.from(await res.arrayBuffer())
    if (buffer.byteLength === 0 || buffer.byteLength > MAX_AVATAR_BYTES) {
      return null
    }
    return { bytes: buffer, mediaType }
  } catch {
    return null
  }
}

function toDataUrl(bytes: Buffer, mediaType: string): string {
  return `data:${mediaType};base64,${bytes.toString('base64')}`
}

async function withTimeout(run: (signal: AbortSignal) => Promise<Response>): Promise<Response> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS)
  try {
    return await run(controller.signal)
  } finally {
    clearTimeout(timer)
  }
}

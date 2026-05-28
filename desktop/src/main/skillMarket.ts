import { promises as fs } from 'fs'
import * as path from 'path'
import { unzipSync } from 'fflate'
import type {
  MarketInstallResult,
  MarketDotCraftInstallPreparation,
  MarketSkillDetail,
  MarketSkillSummary,
  SkillMarketBindDotCraftInstallRequest,
  SkillMarketCleanupDotCraftInstallRequest,
  SkillMarketDetailRequest,
  SkillMarketInstallRequest,
  SkillMarketPrepareDotCraftInstallRequest,
  SkillMarketProviderId,
  SkillMarketSearchRequest,
  SkillMarketSearchResult
} from '../shared/skillMarket'

type FetchLike = typeof fetch

interface ProviderDefinition {
  id: SkillMarketProviderId
  label: string
  searchUrl(query: string, page: number, limit: number): string
  detailUrl(slug: string): string
  fileUrl(slug: string, filePath: string, version?: string): string
  downloadUrl(slug: string, version?: string): string
  sourceUrl(slug: string): string
}

interface InstallMarker {
  provider: SkillMarketProviderId
  slug: string
  version?: string
  installedAt: string
  sourceUrl?: string
}

interface DotCraftInstallMarker {
  provider: SkillMarketProviderId
  slug: string
  version?: string
  preparedAt: string
  sourceUrl?: string
}

interface NormalizedArchiveEntry {
  relativePath: string
  data: Uint8Array
}

const PROVIDERS: Record<SkillMarketProviderId, ProviderDefinition> = {
  skillhub: {
    id: 'skillhub',
    label: 'SkillHub',
    searchUrl: (query, page, limit) => {
      const url = new URL('https://api.skillhub.cn/api/skills')
      url.searchParams.set('page', String(page))
      url.searchParams.set('pageSize', String(limit))
      if (query.trim()) url.searchParams.set('keyword', query.trim())
      return url.href
    },
    detailUrl: (slug) => `https://api.skillhub.cn/api/v1/skills/${encodeURIComponent(slug)}`,
    fileUrl: (slug, filePath, version) => {
      const url = new URL(`https://api.skillhub.cn/api/v1/skills/${encodeURIComponent(slug)}/file`)
      url.searchParams.set('path', filePath)
      if (version) url.searchParams.set('version', version)
      return url.href
    },
    downloadUrl: (slug) => {
      const url = new URL('https://api.skillhub.cn/api/v1/download')
      url.searchParams.set('slug', slug)
      return url.href
    },
    sourceUrl: (slug) => `https://skillhub.cn/skills/${encodeURIComponent(slug)}`
  },
  clawhub: {
    id: 'clawhub',
    label: 'ClawHub',
    searchUrl: (query, _page, limit) => {
      const url = new URL('https://clawhub.ai/api/v1/search')
      url.searchParams.set('q', query.trim())
      url.searchParams.set('limit', String(limit))
      return url.href
    },
    detailUrl: (slug) => `https://clawhub.ai/api/v1/skills/${encodeURIComponent(slug)}`,
    fileUrl: (slug, filePath, version) => {
      const url = new URL(`https://clawhub.ai/api/v1/skills/${encodeURIComponent(slug)}/file`)
      url.searchParams.set('path', filePath)
      if (version) url.searchParams.set('version', version)
      return url.href
    },
    downloadUrl: (slug, version) => {
      const url = new URL('https://clawhub.ai/api/v1/download')
      url.searchParams.set('slug', slug)
      if (version) url.searchParams.set('version', version)
      return url.href
    },
    sourceUrl: (slug) => `https://clawhub.ai/skills/${encodeURIComponent(slug)}`
  }
}

const REQUEST_TIMEOUT_MS = 15_000
const MAX_ZIP_BYTES = 50 * 1024 * 1024
const MAX_EXTRACTED_BYTES = 100 * 1024 * 1024
const MAX_SINGLE_FILE_BYTES = 20 * 1024 * 1024
const MAX_FILE_COUNT = 1000
const INSTALL_MARKER = '.dotcraft-market.json'
const DOTCRAFT_INSTALL_MARKER = '.dotcraft-dotcraft-install.json'
const DOTCRAFT_STAGING_TTL_MS = 7 * 24 * 60 * 60 * 1000
const DOTCRAFT_BINDINGS_DIR = '.bindings'
const SKILL_DIR_RE = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/

export async function searchSkillMarket(
  workspacePath: string,
  request: SkillMarketSearchRequest,
  fetcher: FetchLike = fetch
): Promise<SkillMarketSearchResult> {
  const query = typeof request.query === 'string' ? request.query.trim() : ''
  const page = normalizePositiveInt(request.page, 1, 1000)
  const limit = normalizePositiveInt(request.limit, 20, 50)
  const providerIds = resolveProviderIds(request.provider)
  const settled = await Promise.allSettled(
    providerIds.map(async (providerId) => {
      const provider = PROVIDERS[providerId]
      const raw = await fetchJson(provider.searchUrl(query, page, limit), fetcher, provider.label)
      return extractItems(raw)
        .map((item) => normalizeSummary(provider, item))
        .filter((skill): skill is MarketSkillSummary => skill != null)
    })
  )
  const groups = settled
    .filter((result): result is PromiseFulfilledResult<MarketSkillSummary[]> => result.status === 'fulfilled')
    .map((result) => result.value)
  if (groups.length === 0) {
    const firstError = settled.find((result): result is PromiseRejectedResult => result.status === 'rejected')
    throw new Error(errorMessage(firstError?.reason ?? 'All skill markets failed'))
  }
  const skills = await annotateInstalledState(workspacePath, groups.flat())
  return { skills }
}

export async function getSkillMarketDetail(
  workspacePath: string,
  request: SkillMarketDetailRequest,
  fetcher: FetchLike = fetch
): Promise<MarketSkillDetail> {
  const provider = providerFor(request.provider)
  const slug = normalizeSlug(request.slug)
  const raw = await fetchJson(provider.detailUrl(slug), fetcher, provider.label)
  const detail = normalizeDetail(provider, unwrapPayload(raw), slug)
  const preview = await fetchTextOptional(
    provider.fileUrl(slug, 'SKILL.md', detail.version),
    fetcher
  )
  if (preview) detail.readme = preview
  const [annotated] = await annotateInstalledState(workspacePath, [detail])
  return annotated as MarketSkillDetail
}

export async function installSkillFromMarket(
  workspacePath: string,
  request: SkillMarketInstallRequest,
  fetcher: FetchLike = fetch
): Promise<MarketInstallResult> {
  if (!workspacePath) throw new Error('No workspace open')
  const provider = providerFor(request.provider)
  const slug = normalizeSlug(request.slug)
  const targetName = normalizeSkillDirName(slug)
  const skillsRoot = path.join(workspacePath, '.craft', 'skills')
  const targetDir = path.join(skillsRoot, targetName)
  const existing = await pathExists(targetDir)
  if (existing && request.overwrite !== true) {
    throw new Error(`Skill "${targetName}" is already installed`)
  }

  const downloadUrl = provider.downloadUrl(slug, request.version)
  const archive = await fetchBinary(downloadUrl, fetcher, provider.label)
  const entries = normalizeArchive(archive)
  const version = request.version

  await fs.mkdir(skillsRoot, { recursive: true })
  const stagingDir = path.join(skillsRoot, `.install-${targetName}-${Date.now()}`)
  try {
    await fs.rm(stagingDir, { recursive: true, force: true })
    await fs.mkdir(stagingDir, { recursive: true })
    for (const entry of entries) {
      const targetPath = path.join(stagingDir, entry.relativePath)
      assertWithin(stagingDir, targetPath)
      await fs.mkdir(path.dirname(targetPath), { recursive: true })
      await fs.writeFile(targetPath, entry.data)
    }
    const marker: InstallMarker = {
      provider: provider.id,
      slug,
      version,
      installedAt: new Date().toISOString(),
      sourceUrl: provider.sourceUrl(slug)
    }
    await fs.writeFile(path.join(stagingDir, INSTALL_MARKER), `${JSON.stringify(marker, null, 2)}\n`, 'utf-8')

    if (existing) {
      await fs.rm(targetDir, { recursive: true, force: true })
    }
    await fs.rename(stagingDir, targetDir)
  } catch (error) {
    await fs.rm(stagingDir, { recursive: true, force: true }).catch(() => {})
    throw error
  }

  return {
    skillName: targetName,
    targetDir,
    version,
    overwritten: existing
  }
}

export async function prepareDotCraftSkillInstall(
  workspacePath: string,
  request: SkillMarketPrepareDotCraftInstallRequest,
  fetcher: FetchLike = fetch
): Promise<MarketDotCraftInstallPreparation> {
  if (!workspacePath) throw new Error('No workspace open')
  const provider = providerFor(request.provider)
  const slug = normalizeSlug(request.slug)
  const targetName = normalizeSkillDirName(slug)
  const stagingRoot = path.join(workspacePath, '.craft', 'skill-install-staging')
  await cleanupOldDotCraftStaging(stagingRoot)

  const downloadUrl = provider.downloadUrl(slug, request.version)
  const archive = await fetchBinary(downloadUrl, fetcher, provider.label)
  const entries = normalizeArchive(archive)
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-')
  const stagingDir = path.join(stagingRoot, `${provider.id}.${targetName}.${timestamp}`)
  const candidateDir = path.join(stagingDir, 'source')
  const metadataPath = path.join(stagingDir, DOTCRAFT_INSTALL_MARKER)

  try {
    await fs.rm(stagingDir, { recursive: true, force: true })
    await fs.mkdir(candidateDir, { recursive: true })
    for (const entry of entries) {
      const targetPath = path.join(candidateDir, entry.relativePath)
      assertWithin(candidateDir, targetPath)
      await fs.mkdir(path.dirname(targetPath), { recursive: true })
      await fs.writeFile(targetPath, entry.data)
    }
    const sourceUrl = provider.sourceUrl(slug)
    const marker: DotCraftInstallMarker = {
      provider: provider.id,
      slug,
      version: request.version,
      preparedAt: new Date().toISOString(),
      sourceUrl
    }
    await fs.writeFile(metadataPath, `${JSON.stringify(marker, null, 2)}\n`, 'utf-8')

    return {
      skillName: targetName,
      provider: provider.id,
      slug,
      version: request.version,
      sourceUrl,
      workspacePath,
      stagingDir,
      candidateDir,
      metadataPath
    }
  } catch (error) {
    await fs.rm(stagingDir, { recursive: true, force: true }).catch(() => {})
    throw error
  }
}

export async function bindDotCraftSkillInstall(
  workspacePath: string,
  request: SkillMarketBindDotCraftInstallRequest
): Promise<void> {
  if (!workspacePath) throw new Error('No workspace open')
  const threadId = normalizeThreadId(request.threadId)
  const stagingRoot = path.join(workspacePath, '.craft', 'skill-install-staging')
  const stagingDir = path.resolve(request.stagingDir)
  assertWithin(stagingRoot, stagingDir)
  if (!(await pathExists(stagingDir))) throw new Error('DotCraft install staging directory not found')

  const bindingsRoot = path.join(stagingRoot, DOTCRAFT_BINDINGS_DIR)
  await fs.mkdir(bindingsRoot, { recursive: true })
  const bindingPath = path.join(bindingsRoot, `${threadId}.json`)
  await fs.writeFile(
    bindingPath,
    `${JSON.stringify({ threadId, stagingDir, boundAt: new Date().toISOString() }, null, 2)}\n`,
    'utf-8'
  )
}

export async function cleanupDotCraftSkillInstall(
  workspacePath: string,
  request: SkillMarketCleanupDotCraftInstallRequest
): Promise<void> {
  if (!workspacePath) return
  const threadId = normalizeThreadId(request.threadId)
  const stagingRoot = path.join(workspacePath, '.craft', 'skill-install-staging')
  const bindingPath = path.join(stagingRoot, DOTCRAFT_BINDINGS_DIR, `${threadId}.json`)
  let stagingDir: string | null = null
  try {
    const raw = JSON.parse(await fs.readFile(bindingPath, 'utf-8')) as { stagingDir?: unknown }
    if (typeof raw.stagingDir === 'string' && raw.stagingDir.trim()) {
      const resolved = path.resolve(raw.stagingDir)
      assertWithin(stagingRoot, resolved)
      stagingDir = resolved
    }
  } catch {
    stagingDir = null
  }

  if (stagingDir) {
    await fs.rm(stagingDir, { recursive: true, force: true }).catch(() => {})
  }
  await fs.rm(bindingPath, { force: true }).catch(() => {})
}

export function normalizeArchive(zipBytes: Uint8Array): NormalizedArchiveEntry[] {
  let files: Record<string, Uint8Array>
  try {
    files = unzipSync(zipBytes)
  } catch (error) {
    throw new Error(`Failed to extract skill archive: ${errorMessage(error)}`)
  }

  const rawEntries = Object.entries(files).filter(([name]) => !name.endsWith('/'))
  if (rawEntries.length === 0) throw new Error('Skill archive is empty')
  if (rawEntries.length > MAX_FILE_COUNT) throw new Error(`Skill archive has too many files (${rawEntries.length})`)

  const safeEntries = rawEntries.map(([rawPath, data]) => ({
    relativePath: normalizeArchivePath(rawPath),
    data
  }))
  const commonRoot = findCommonSkillRoot(safeEntries.map((entry) => entry.relativePath))
  let total = 0
  const normalized = safeEntries.map((entry) => {
    const relativePath = commonRoot ? entry.relativePath.slice(commonRoot.length + 1) : entry.relativePath
    if (!relativePath || relativePath.includes('\\')) {
      throw new Error(`Invalid archive path: ${entry.relativePath}`)
    }
    if (entry.data.byteLength > MAX_SINGLE_FILE_BYTES) {
      throw new Error(`Archive file is too large: ${relativePath}`)
    }
    total += entry.data.byteLength
    return { relativePath, data: entry.data }
  })
  if (total > MAX_EXTRACTED_BYTES) throw new Error('Skill archive expands beyond the allowed size')
  if (!normalized.some((entry) => entry.relativePath === 'SKILL.md')) {
    throw new Error('Skill archive must contain SKILL.md at its root')
  }
  return normalized
}

function providerFor(id: SkillMarketProviderId): ProviderDefinition {
  const provider = PROVIDERS[id]
  if (!provider) throw new Error(`Unsupported skill market provider: ${id}`)
  return provider
}

function resolveProviderIds(provider: SkillMarketSearchRequest['provider']): SkillMarketProviderId[] {
  if (provider === 'skillhub' || provider === 'clawhub') return [provider]
  return ['skillhub', 'clawhub']
}

function normalizePositiveInt(value: unknown, fallback: number, max: number): number {
  const n = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(n) || n <= 0) return fallback
  return Math.min(Math.floor(n), max)
}

function normalizeSlug(slug: string): string {
  const trimmed = String(slug ?? '').trim()
  if (!trimmed) throw new Error('Skill slug is required')
  return trimmed
}

function normalizeSkillDirName(slug: string): string {
  if (!SKILL_DIR_RE.test(slug)) {
    throw new Error(`Invalid skill slug for local install: ${slug}`)
  }
  return slug
}

async function fetchJson(url: string, fetcher: FetchLike, label: string): Promise<unknown> {
  assertHttpsUrl(url)
  const response = await fetchWithTimeout(url, fetcher)
  if (!response.ok) {
    throw new Error(`${label} request failed (${response.status})`)
  }
  return response.json()
}

async function fetchTextOptional(url: string, fetcher: FetchLike): Promise<string | undefined> {
  try {
    assertHttpsUrl(url)
    const response = await fetchWithTimeout(url, fetcher)
    assertHttpsUrl(response.url || url)
    if (!response.ok) return undefined
    const text = await response.text()
    return text.trim() ? text : undefined
  } catch {
    return undefined
  }
}

async function fetchBinary(url: string, fetcher: FetchLike, label: string): Promise<Uint8Array> {
  assertHttpsUrl(url)
  const response = await fetchWithTimeout(url, fetcher)
  assertHttpsUrl(response.url || url)
  if (!response.ok) {
    throw new Error(`${label} download failed (${response.status})`)
  }
  const contentLength = response.headers.get('content-length')
  if (contentLength && Number(contentLength) > MAX_ZIP_BYTES) {
    throw new Error('Skill archive is too large')
  }
  const buffer = await response.arrayBuffer()
  if (buffer.byteLength > MAX_ZIP_BYTES) {
    throw new Error('Skill archive is too large')
  }
  return new Uint8Array(buffer)
}

async function fetchWithTimeout(url: string, fetcher: FetchLike): Promise<Response> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  try {
    return await fetcher(url, {
      signal: controller.signal,
      redirect: 'follow',
      headers: { accept: 'application/json, application/zip, */*' }
    })
  } catch (error) {
    throw new Error(`Skill market request failed: ${errorMessage(error)}`)
  } finally {
    clearTimeout(timer)
  }
}

function assertHttpsUrl(url: string): void {
  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    throw new Error(`Invalid skill market URL: ${url}`)
  }
  if (parsed.protocol !== 'https:') {
    throw new Error(`Skill market URL must use HTTPS: ${url}`)
  }
}

function extractItems(raw: unknown): unknown[] {
  const payload = unwrapPayload(raw)
  if (Array.isArray(payload)) return payload
  if (!isRecord(payload)) return []
  const candidates = [payload.skills, payload.items, payload.results, payload.data, payload.list]
  for (const candidate of candidates) {
    if (Array.isArray(candidate)) return candidate
    if (isRecord(candidate)) {
      const nested = extractItems(candidate)
      if (nested.length > 0) return nested
    }
  }
  return []
}

function unwrapPayload(raw: unknown): unknown {
  if (!isRecord(raw)) return raw
  if (isRecord(raw.data) || Array.isArray(raw.data)) return raw.data
  if (isRecord(raw.result) || Array.isArray(raw.result)) return raw.result
  return raw
}

function normalizeSummary(provider: ProviderDefinition, item: unknown): MarketSkillSummary | null {
  if (!isRecord(item)) return null
  const slug = firstString(item.slug, item.name, item.id, item.packageName)
  if (!slug) return null
  const name = firstString(item.displayName, item.title, item.name, item.slug) ?? slug
  const latestVersion = isRecord(item.latestVersion) ? item.latestVersion : {}
  const stats = isRecord(item.stats) ? item.stats : {}
  const tagRecord = isRecord(item.tags) ? item.tags : {}
  return {
    provider: provider.id,
    slug,
    name,
    description: firstString(item.description, item.summary, item.shortDescription),
    version: firstString(
      item.version,
      item.latestVersion,
      item.currentVersion,
      latestVersion.version,
      latestVersion.name,
      tagRecord.latest
    ),
    author: normalizeAuthor(item.author, item.owner, item.publisher, item.user),
    downloads: firstNumber(item.downloads, item.downloadCount, item.installCount, item.pulls, stats.downloads),
    rating: firstNumber(item.rating, item.stars, item.score, stats.stars),
    tags: normalizeTags(item.tags, item.categories),
    sourceUrl: firstString(item.url, item.sourceUrl) ?? provider.sourceUrl(slug)
  }
}

function normalizeDetail(provider: ProviderDefinition, raw: unknown, fallbackSlug: string): MarketSkillDetail {
  const item = isRecord(raw) && isRecord(raw.skill) ? raw.skill : raw
  const record = isRecord(raw) ? raw : {}
  const itemRecord = isRecord(item) ? item : {}
  const summary =
    normalizeSummary(provider, item) ??
    ({
      provider: provider.id,
      slug: fallbackSlug,
      name: fallbackSlug,
      sourceUrl: provider.sourceUrl(fallbackSlug)
    } satisfies MarketSkillSummary)
  const latestVersion = isRecord(record.latestVersion) ? record.latestVersion : {}
  const stats = isRecord(itemRecord.stats) ? itemRecord.stats : {}
  const tagRecord = isRecord(itemRecord.tags) ? itemRecord.tags : {}
  return {
    ...summary,
    version:
      summary.version ??
      firstString(record.version, record.latestVersion, latestVersion.version, latestVersion.name, tagRecord.latest),
    downloads:
      summary.downloads ??
      firstNumber(record.downloads, record.downloadCount, record.installCount, record.pulls, stats.downloads),
    rating: summary.rating ?? firstNumber(record.rating, record.stars, record.score, stats.stars),
    readme: firstString(
      record.readme,
      record.content,
      record.skillMd,
      record.skillMarkdown,
      itemRecord.readme,
      itemRecord.content,
      itemRecord.skillMd,
      itemRecord.skillMarkdown
    ),
    files: normalizeFiles(record.files ?? itemRecord.files),
    versions: normalizeVersions(record.versions ?? itemRecord.versions)
  }
}

async function annotateInstalledState<T extends MarketSkillSummary>(
  workspacePath: string,
  skills: T[]
): Promise<T[]> {
  if (!workspacePath || skills.length === 0) return skills
  const skillsRoot = path.join(workspacePath, '.craft', 'skills')
  return Promise.all(
    skills.map(async (skill) => {
      let installed = false
      let updateAvailable = false
      try {
        const targetName = normalizeSkillDirName(skill.slug)
        const marker = await readInstallMarker(path.join(skillsRoot, targetName, INSTALL_MARKER))
        installed = marker != null || (await pathExists(path.join(skillsRoot, targetName, 'SKILL.md')))
        updateAvailable =
          marker != null &&
          marker.provider === skill.provider &&
          marker.slug === skill.slug &&
          Boolean(marker.version) &&
          Boolean(skill.version) &&
          marker.version !== skill.version
      } catch {
        installed = false
      }
      return { ...skill, installed, updateAvailable }
    })
  )
}

async function readInstallMarker(markerPath: string): Promise<InstallMarker | null> {
  try {
    const text = await fs.readFile(markerPath, 'utf-8')
    const raw = JSON.parse(text) as Partial<InstallMarker>
    if (raw.provider !== 'skillhub' && raw.provider !== 'clawhub') return null
    if (typeof raw.slug !== 'string' || !raw.slug) return null
    return {
      provider: raw.provider,
      slug: raw.slug,
      version: typeof raw.version === 'string' ? raw.version : undefined,
      installedAt: typeof raw.installedAt === 'string' ? raw.installedAt : '',
      sourceUrl: typeof raw.sourceUrl === 'string' ? raw.sourceUrl : undefined
    }
  } catch {
    return null
  }
}

function normalizeArchivePath(rawPath: string): string {
  const normalized = rawPath.replace(/\\/g, '/').split('/').filter(Boolean).join('/')
  if (!normalized || normalized.startsWith('/') || /^[A-Za-z]:/.test(normalized)) {
    throw new Error(`Invalid archive path: ${rawPath}`)
  }
  const segments = normalized.split('/')
  if (segments.some((segment) => segment === '..' || segment === '.')) {
    throw new Error(`Invalid archive path: ${rawPath}`)
  }
  return normalized
}

function findCommonSkillRoot(paths: string[]): string | null {
  if (paths.includes('SKILL.md')) return null
  const roots = new Set(paths.map((entryPath) => entryPath.split('/')[0]))
  if (roots.size !== 1) return null
  const [root] = [...roots]
  return paths.includes(`${root}/SKILL.md`) ? root : null
}

function assertWithin(root: string, targetPath: string): void {
  const resolvedRoot = path.resolve(root)
  const resolvedTarget = path.resolve(targetPath)
  if (resolvedTarget !== resolvedRoot && !resolvedTarget.startsWith(resolvedRoot + path.sep)) {
    throw new Error(`Archive path escapes install directory: ${targetPath}`)
  }
}

async function pathExists(targetPath: string): Promise<boolean> {
  try {
    await fs.access(targetPath)
    return true
  } catch {
    return false
  }
}

async function cleanupOldDotCraftStaging(stagingRoot: string): Promise<void> {
  const now = Date.now()
  let entries: Array<import('fs').Dirent>
  try {
    entries = await fs.readdir(stagingRoot, { withFileTypes: true })
  } catch {
    return
  }

  await Promise.all(entries
    .filter((entry) => entry.isDirectory() && entry.name !== DOTCRAFT_BINDINGS_DIR)
    .map(async (entry) => {
      const fullPath = path.join(stagingRoot, entry.name)
      try {
        const stats = await fs.stat(fullPath)
        if (now - stats.mtimeMs > DOTCRAFT_STAGING_TTL_MS) {
          await fs.rm(fullPath, { recursive: true, force: true })
        }
      } catch {
        // Best-effort cleanup must never block a DotCraft install preparation.
      }
    }))

  await cleanupDotCraftInstallBindings(stagingRoot)
}

async function cleanupDotCraftInstallBindings(stagingRoot: string): Promise<void> {
  const bindingsRoot = path.join(stagingRoot, DOTCRAFT_BINDINGS_DIR)
  let entries: Array<import('fs').Dirent>
  try {
    entries = await fs.readdir(bindingsRoot, { withFileTypes: true })
  } catch {
    return
  }

  await Promise.all(entries
    .filter((entry) => entry.isFile() && entry.name.endsWith('.json'))
    .map(async (entry) => {
      const bindingPath = path.join(bindingsRoot, entry.name)
      try {
        const raw = JSON.parse(await fs.readFile(bindingPath, 'utf-8')) as { stagingDir?: unknown }
        const stagingDir = typeof raw.stagingDir === 'string' ? path.resolve(raw.stagingDir) : ''
        if (!stagingDir) {
          await fs.rm(bindingPath, { force: true })
          return
        }
        assertWithin(stagingRoot, stagingDir)
        if (!(await pathExists(stagingDir))) {
          await fs.rm(bindingPath, { force: true })
        }
      } catch {
        await fs.rm(bindingPath, { force: true }).catch(() => {})
      }
    }))
}

function normalizeThreadId(threadId: string): string {
  const trimmed = String(threadId ?? '').trim()
  if (!trimmed) throw new Error('Thread id is required')
  if (!/^[A-Za-z0-9_.:-]+$/.test(trimmed)) throw new Error(`Invalid thread id: ${threadId}`)
  return trimmed
}

function normalizeAuthor(...values: unknown[]): string | undefined {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) return value.trim()
    if (isRecord(value)) {
      const found = firstString(value.name, value.username, value.login, value.displayName)
      if (found) return found
    }
  }
  return undefined
}

function normalizeTags(...values: unknown[]): string[] | undefined {
  for (const value of values) {
    if (Array.isArray(value)) {
      const tags = value
        .map((tag) => (typeof tag === 'string' ? tag : isRecord(tag) ? firstString(tag.name, tag.slug) : undefined))
        .filter((tag): tag is string => Boolean(tag))
      if (tags.length > 0) return tags
    }
  }
  return undefined
}

function normalizeFiles(value: unknown): Array<{ path: string; size?: number }> | undefined {
  if (!Array.isArray(value)) return undefined
  const files = value
    .map((file) => {
      if (typeof file === 'string') return { path: file }
      if (!isRecord(file)) return null
      const filePath = firstString(file.path, file.name)
      if (!filePath) return null
      const size = firstNumber(file.size, file.bytes)
      return size == null ? { path: filePath } : { path: filePath, size }
    })
    .filter((file): file is { path: string; size?: number } => file != null)
  return files.length > 0 ? files : undefined
}

function normalizeVersions(value: unknown): string[] | undefined {
  if (!Array.isArray(value)) return undefined
  const versions = value
    .map((version) => (typeof version === 'string' ? version : isRecord(version) ? firstString(version.version, version.name) : undefined))
    .filter((version): version is string => Boolean(version))
  return versions.length > 0 ? versions : undefined
}

function firstString(...values: unknown[]): string | undefined {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) return value.trim()
    if (typeof value === 'number' && Number.isFinite(value)) return String(value)
  }
  return undefined
}

function firstNumber(...values: unknown[]): number | undefined {
  for (const value of values) {
    if (typeof value === 'number' && Number.isFinite(value)) return value
    if (typeof value === 'string' && value.trim() !== '') {
      const parsed = Number(value)
      if (Number.isFinite(parsed)) return parsed
    }
  }
  return undefined
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

import { compareAppVersions, isValidAppVersion } from './whatsNew'

export const DOTCRAFT_RELEASES_API_URL = 'https://api.github.com/repos/DotHarness/dotcraft/releases/latest'
export const DOTCRAFT_RELEASE_DOWNLOAD_BASE_URL = 'https://github.com/DotHarness/dotcraft/releases/download/'

export type AppUpdateStatus =
  | 'idle'
  | 'checking'
  | 'not-available'
  | 'available'
  | 'downloading'
  | 'downloaded'
  | 'error'

export interface GitHubReleaseAsset {
  name?: string
  size?: number
  browser_download_url?: string
}

export interface GitHubRelease {
  tag_name?: string
  name?: string
  html_url?: string
  body?: string
  published_at?: string
  draft?: boolean
  prerelease?: boolean
  assets?: GitHubReleaseAsset[]
}

export interface AppUpdateInfo {
  currentVersion: string
  latestVersion: string
  tagName: string
  releaseName?: string
  releaseNotes?: string
  publishedAt?: string
  htmlUrl?: string
  assetName: string
  sizeBytes: number
  downloadUrl: string
}

export interface AppUpdateProgress {
  transferredBytes: number
  totalBytes: number
  percent: number
}

export interface AppUpdateState {
  status: AppUpdateStatus
  currentVersion: string
  update?: AppUpdateInfo
  progress?: AppUpdateProgress
  error?: string
}

export type AppUpdatePlatform = 'win32' | 'darwin' | 'linux' | string
export type AppUpdateArch = 'x64' | 'arm64' | string

export function normalizeReleaseTagVersion(tagName: string | undefined | null): string | null {
  const version = tagName?.trim().replace(/^v/i, '') ?? ''
  return isValidAppVersion(version) ? version : null
}

export function isAllowedReleaseDownloadUrl(url: string): boolean {
  return url.startsWith(DOTCRAFT_RELEASE_DOWNLOAD_BASE_URL)
}

export function hasNewerRelease(currentVersion: string, latestVersion: string): boolean {
  if (!isValidAppVersion(currentVersion) || !isValidAppVersion(latestVersion)) {
    return false
  }
  return compareAppVersions(latestVersion, currentVersion) > 0
}

export function selectUpdateAsset(
  assets: GitHubReleaseAsset[] | undefined,
  platform: AppUpdatePlatform,
  arch: AppUpdateArch
): GitHubReleaseAsset | null {
  const scored = (assets ?? [])
    .map((asset) => ({ asset, score: scoreUpdateAsset(asset, platform, arch) }))
    .filter((entry) => entry.score > 0)
    .sort((a, b) => b.score - a.score)
  return scored[0]?.asset ?? null
}

export function resolveUpdateFromRelease(
  currentVersion: string,
  release: GitHubRelease,
  platform: AppUpdatePlatform,
  arch: AppUpdateArch
): AppUpdateInfo | null {
  if (release.draft || release.prerelease) return null

  const latestVersion = normalizeReleaseTagVersion(release.tag_name)
  if (!latestVersion || !hasNewerRelease(currentVersion, latestVersion)) return null

  const asset = selectUpdateAsset(release.assets, platform, arch)
  const assetName = asset?.name?.trim() ?? ''
  const downloadUrl = asset?.browser_download_url?.trim() ?? ''
  if (!assetName || !downloadUrl || !isAllowedReleaseDownloadUrl(downloadUrl)) return null

  return {
    currentVersion,
    latestVersion,
    tagName: release.tag_name?.trim() || `v${latestVersion}`,
    releaseName: release.name?.trim() || undefined,
    releaseNotes: release.body?.trim() || undefined,
    publishedAt: release.published_at?.trim() || undefined,
    htmlUrl: release.html_url?.trim() || undefined,
    assetName,
    sizeBytes: Math.max(0, asset?.size ?? 0),
    downloadUrl
  }
}

function scoreUpdateAsset(
  asset: GitHubReleaseAsset,
  platform: AppUpdatePlatform,
  arch: AppUpdateArch
): number {
  const name = asset.name?.trim().toLowerCase() ?? ''
  const url = asset.browser_download_url?.trim() ?? ''
  if (!name || !isAllowedReleaseDownloadUrl(url)) return -1
  if (name.endsWith('.blockmap') || name.endsWith('.yml')) return -1

  let score = 0
  const normalizedArch = arch.toLowerCase()
  const hasArch = name.includes(normalizedArch)
  const hasUniversal = name.includes('universal')
  if (!hasArch && !hasUniversal && hasDifferentKnownArchitecture(name, normalizedArch)) return -1

  if (platform === 'win32') {
    if (!name.endsWith('.exe') || !name.includes('setup')) return -1
    score += 100
    if (name.includes('win') || name.includes('windows')) score += 20
  } else if (platform === 'darwin') {
    if (!name.endsWith('.dmg')) return -1
    score += 100
    if (name.includes('mac') || name.includes('darwin') || name.includes('osx')) score += 20
  } else if (platform === 'linux') {
    if (!(name.endsWith('.appimage') || name.endsWith('.deb'))) return -1
    score += name.endsWith('.appimage') ? 100 : 90
    if (name.includes('linux')) score += 20
  } else {
    return -1
  }

  if (hasArch) score += 10
  else if (hasUniversal) score += 5

  return score
}

function hasDifferentKnownArchitecture(name: string, arch: string): boolean {
  if (!arch) return false

  return ['arm64', 'x64', 'ia32', 'armv7l']
    .some((knownArch) => knownArch !== arch && name.includes(knownArch))
}

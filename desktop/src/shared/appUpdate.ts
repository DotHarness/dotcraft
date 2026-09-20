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

function normalizeReleaseTagVersion(tagName: string | undefined | null): string | null {
  const version = tagName?.trim().replace(/^v/i, '') ?? ''
  return isValidAppVersion(version) ? version : null
}

export function isAllowedReleaseDownloadUrl(url: string): boolean {
  return url.startsWith(DOTCRAFT_RELEASE_DOWNLOAD_BASE_URL)
}

function hasNewerRelease(currentVersion: string, latestVersion: string): boolean {
  if (!isValidAppVersion(currentVersion) || !isValidAppVersion(latestVersion)) {
    return false
  }
  return compareAppVersions(latestVersion, currentVersion) > 0
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

  const tagName = `v${latestVersion}`
  const asset = selectExactUpdateAsset(
    release.assets,
    tagName,
    latestVersion,
    platform,
    arch
  )
  const assetName = asset?.name?.trim() ?? ''
  const downloadUrl = asset?.browser_download_url?.trim() ?? ''

  return {
    currentVersion,
    latestVersion,
    tagName,
    releaseName: release.name?.trim() || undefined,
    releaseNotes: release.body?.trim() || undefined,
    publishedAt: release.published_at?.trim() || undefined,
    htmlUrl: release.html_url?.trim() || undefined,
    assetName,
    sizeBytes: Math.max(0, asset?.size ?? 0),
    downloadUrl
  }
}

function selectExactUpdateAsset(
  assets: GitHubReleaseAsset[] | undefined,
  tagName: string,
  version: string,
  platform: AppUpdatePlatform,
  arch: AppUpdateArch
): GitHubReleaseAsset {
  const expectedNames = expectedDesktopAssetNames(version, platform, arch)
  if (expectedNames.length === 0) {
    throw new Error(`DotCraft Desktop updates are not published for ${platform}/${arch}.`)
  }

  for (const expectedName of expectedNames) {
    const matches = (assets ?? []).filter((asset) => asset.name?.trim() === expectedName)
    if (matches.length > 1) {
      throw new Error(`Release ${tagName} contains multiple ${expectedName} assets.`)
    }
    if (matches.length === 0) continue

    const asset = matches[0]
    const expectedUrl = `${DOTCRAFT_RELEASE_DOWNLOAD_BASE_URL}${tagName}/${expectedName}`
    if (asset.browser_download_url?.trim() !== expectedUrl) {
      throw new Error(`Release ${tagName} has an invalid download URL for ${expectedName}.`)
    }
    return asset
  }

  throw new Error(`Release ${tagName} does not contain a DotCraft Desktop asset for ${platform}/${arch}.`)
}

function expectedDesktopAssetNames(
  version: string,
  platform: AppUpdatePlatform,
  arch: AppUpdateArch
): string[] {
  const normalizedArch = arch.trim().toLowerCase()
  if (!/^[a-z0-9]+$/.test(normalizedArch)) return []

  const prefix = `DotCraft-v${version}`
  if (platform === 'win32') return [`${prefix}-win-${normalizedArch}-Setup.exe`]
  if (platform === 'darwin') return [`${prefix}-macos-${normalizedArch}.dmg`]
  if (platform === 'linux') {
    return [
      `${prefix}-linux-${normalizedArch}.AppImage`,
      `${prefix}-linux-${normalizedArch}.deb`
    ]
  }
  return []
}

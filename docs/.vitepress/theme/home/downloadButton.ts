/** Homepage release download buttons backed by the checked-in release manifest. */
import { withBase } from 'vitepress'

export const RELEASES_PAGE = 'https://github.com/DotHarness/dotcraft/releases'
const MANIFEST_PATH = '/release-downloads.json'

type AssetId =
  | 'desktop-win-x64'
  | 'desktop-win-arm64'
  | 'desktop-macos-arm64'
  | 'desktop-macos-x64'
  | 'cli-linux-x64'

export interface Platform {
  id: string
  os: 'windows' | 'apple' | 'linux'
  assetId: AssetId
  label: { en: string; zh: string }
}

export const PLATFORMS: Platform[] = [
  { id: 'win-x64', os: 'windows', assetId: 'desktop-win-x64', label: { en: 'Windows (x64)', zh: 'Windows (x64)' } },
  { id: 'win-arm64', os: 'windows', assetId: 'desktop-win-arm64', label: { en: 'Windows (ARM64)', zh: 'Windows (ARM64)' } },
  { id: 'mac-arm64', os: 'apple', assetId: 'desktop-macos-arm64', label: { en: 'macOS (Apple Silicon)', zh: 'macOS（Apple 芯片）' } },
  { id: 'mac-x64', os: 'apple', assetId: 'desktop-macos-x64', label: { en: 'macOS (Intel)', zh: 'macOS（Intel）' } },
  { id: 'linux-x64', os: 'linux', assetId: 'cli-linux-x64', label: { en: 'Linux (x64)', zh: 'Linux (x64)' } }
]

export interface ReleaseManifest {
  tag: string
  assets: Record<AssetId, { fileName: string; url: string }>
}

let manifestPromise: Promise<ReleaseManifest> | null = null

export function loadManifest(): Promise<ReleaseManifest> {
  manifestPromise ??= fetch(withBase(MANIFEST_PATH)).then(async (response) => {
    if (!response.ok) throw new Error(`Release manifest ${response.status}`)
    return (await response.json()) as ReleaseManifest
  })
  return manifestPromise
}

export function startDownload(platform: Platform): void {
  void loadManifest()
    .then((manifest) => {
      window.location.href = manifest.assets[platform.assetId].url
    })
    .catch(() => {
      window.location.href = RELEASES_PAGE
    })
}

/** Best-effort platform detection; mobile and unknown platforms use the generic label. */
export function detectPlatform(): Platform | null {
  const ua = navigator.userAgent
  const uaData = (navigator as Navigator & { userAgentData?: { platform?: string; mobile?: boolean } }).userAgentData

  if (uaData?.mobile || /Android|iPhone|iPad|iPod/i.test(ua)) return null

  const platform = (uaData?.platform ?? '').toLowerCase()
  const isWindows = platform.includes('windows') || /Windows/i.test(ua)
  const isMac = platform.includes('macos') || platform.includes('mac') || /Macintosh|Mac OS X/i.test(ua)
  const isLinux = platform.includes('linux') || /Linux/i.test(ua)

  if (isWindows) return find(/ARM64|aarch64/i.test(ua) ? 'win-arm64' : 'win-x64')
  if (isMac) return find('mac-arm64')
  if (isLinux) return find('linux-x64')
  return null
}

function find(id: string): Platform | null {
  return PLATFORMS.find((platform) => platform.id === id) ?? null
}

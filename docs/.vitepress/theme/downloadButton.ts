/** Homepage release download buttons backed by the checked-in release manifest. */
import { withBase } from 'vitepress'

const REPO = 'DotHarness/dotcraft'
const RELEASES_PAGE = `https://github.com/${REPO}/releases`
const MANIFEST_PATH = '/release-downloads.json'

type AssetId =
  | 'desktop-win-x64'
  | 'desktop-win-arm64'
  | 'desktop-macos-arm64'
  | 'desktop-macos-x64'
  | 'cli-linux-x64'

interface Platform {
  id: string
  assetId: AssetId
  label: { en: string; zh: string }
}

const PLATFORMS: Platform[] = [
  { id: 'win-x64', assetId: 'desktop-win-x64', label: { en: 'Windows (x64)', zh: 'Windows (x64)' } },
  { id: 'win-arm64', assetId: 'desktop-win-arm64', label: { en: 'Windows (ARM64)', zh: 'Windows (ARM64)' } },
  { id: 'mac-arm64', assetId: 'desktop-macos-arm64', label: { en: 'macOS (Apple Silicon)', zh: 'macOS（Apple 芯片）' } },
  { id: 'mac-x64', assetId: 'desktop-macos-x64', label: { en: 'macOS (Intel)', zh: 'macOS（Intel）' } },
  { id: 'linux-x64', assetId: 'cli-linux-x64', label: { en: 'Linux (x64)', zh: 'Linux (x64)' } }
]

interface ReleaseManifest {
  assets: Record<AssetId, { fileName: string; url: string }>
}

type Lang = 'en' | 'zh'

const T = {
  download: { en: 'Download', zh: '下载' },
  downloadFor: { en: 'Download for', zh: '下载' },
  fallback: { en: 'Download release', zh: '下载 Release' }
}

interface WiredRoot extends HTMLElement {
  _dcDownloadWired?: boolean
}

let manifestPromise: Promise<ReleaseManifest> | null = null

export function setupDownloadButton(): void {
  const roots = document.querySelectorAll<WiredRoot>('[data-download]')
  for (const root of roots) setupDownloadRoot(root)
}

function setupDownloadRoot(root: WiredRoot): void {
  const lang: Lang = root.dataset.downloadLang === 'zh' ? 'zh' : 'en'
  const main = root.querySelector<HTMLAnchorElement>('[data-download-main]')
  const toggle = root.querySelector<HTMLButtonElement>('[data-download-toggle]')
  const menu = root.querySelector<HTMLElement>('[data-download-menu]')
  if (!main || !toggle || !menu) return

  const detected = detectPlatform()
  const closeMs =
    parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--dropdown-close-dur')) || 150

  const openMenu = (): void => {
    menu.hidden = false
    menu.classList.remove('is-closing')
    requestAnimationFrame(() => menu.classList.add('is-open'))
    toggle.setAttribute('aria-expanded', 'true')
    root.classList.add('dc-download--open')
  }
  const closeMenu = (): void => {
    if (!menu.classList.contains('is-open')) return
    menu.classList.remove('is-open')
    menu.classList.add('is-closing')
    window.setTimeout(() => {
      menu.classList.remove('is-closing')
      menu.hidden = true
    }, closeMs)
    toggle.setAttribute('aria-expanded', 'false')
    root.classList.remove('dc-download--open')
  }

  const render = (): void => {
    main.textContent = detected ? `${T.downloadFor[lang]} ${detected.label[lang]}` : T.fallback[lang]
    menu.replaceChildren()
    for (const platform of PLATFORMS) {
      const item = document.createElement('button')
      item.type = 'button'
      item.className = 'dc-download__item'
      item.setAttribute('role', 'menuitem')
      item.textContent = `${T.download[lang]} ${platform.label[lang]}`
      item.addEventListener('click', () => {
        closeMenu()
        startDownload(platform)
      })
      menu.appendChild(item)
    }
  }

  if (!root._dcDownloadWired) {
    root._dcDownloadWired = true

    if (detected) {
      void loadManifest()
        .then((manifest) => {
          main.href = manifest.assets[detected.assetId].url
        })
        .catch(() => {
          main.href = RELEASES_PAGE
        })
      main.addEventListener('click', (event) => {
        event.preventDefault()
        startDownload(detected)
      })
    }
    toggle.addEventListener('click', (event) => {
      event.stopPropagation()
      menu.classList.contains('is-open') ? closeMenu() : openMenu()
    })
    document.addEventListener('click', (event) => {
      if (!root.contains(event.target as Node)) closeMenu()
    })
    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') closeMenu()
    })

    if (document.readyState !== 'complete') {
      window.addEventListener('load', render, { once: true })
    }
  }

  render()
}

function startDownload(platform: Platform): void {
  void loadManifest()
    .then((manifest) => {
      window.location.href = manifest.assets[platform.assetId].url
    })
    .catch(() => {
      window.location.href = RELEASES_PAGE
    })
}

function loadManifest(): Promise<ReleaseManifest> {
  manifestPromise ??= fetch(withBase(MANIFEST_PATH)).then(async (response) => {
    if (!response.ok) throw new Error(`Release manifest ${response.status}`)
    return (await response.json()) as ReleaseManifest
  })
  return manifestPromise
}

/** Best-effort platform detection; mobile and unknown platforms use the generic label. */
function detectPlatform(): Platform | null {
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

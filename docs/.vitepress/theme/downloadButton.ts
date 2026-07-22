/**
 * Hero "Download release" split button: detects the visitor's platform, labels
 * the main button, and builds a per-platform menu. Each click resolves the
 * matching asset from the latest release and navigates to it. The asset list is
 * warmed on load so the click can navigate synchronously (downloads are blocked
 * outside a user gesture). Falls back to the releases page when the API is down.
 */
const REPO = 'DotHarness/dotcraft'
const RELEASES_PAGE = `https://github.com/${REPO}/releases`
const LATEST_API = `https://api.github.com/repos/${REPO}/releases/latest`
const CACHE_KEY = 'dotcraft:latest-release-assets'

/** A selectable download target; `suffix` matches the end of a release asset name. */
interface Platform {
  id: string
  suffix: string
  label: { en: string; zh: string }
}

const PLATFORMS: Platform[] = [
  { id: 'win-x64', suffix: 'win-x64-Setup.exe', label: { en: 'Windows (x64)', zh: 'Windows (x64)' } },
  { id: 'win-arm64', suffix: 'win-arm64-Setup.exe', label: { en: 'Windows (ARM64)', zh: 'Windows (ARM64)' } },
  { id: 'mac-arm64', suffix: 'macos-arm64.dmg', label: { en: 'macOS (Apple Silicon)', zh: 'macOS（Apple 芯片）' } },
  { id: 'mac-x64', suffix: 'macos-x64.dmg', label: { en: 'macOS (Intel)', zh: 'macOS（Intel）' } },
  { id: 'linux-x64', suffix: 'linux-x64.tar.gz', label: { en: 'Linux (x64)', zh: 'Linux (x64)' } }
]

type Lang = 'en' | 'zh'

const T = {
  download: { en: 'Download', zh: '下载' },
  downloadFor: { en: 'Download for', zh: '下载' },
  fallback: { en: 'Download release', zh: '下载 Release' }
}

/** JS-property flag (survives hydration) marking an already-wired root. */
interface WiredRoot extends HTMLElement {
  _dcDownloadWired?: boolean
}

export function setupDownloadButton(): void {
  const root = document.querySelector<WiredRoot>('[data-download]')
  if (!root) return

  const lang: Lang = root.dataset.downloadLang === 'zh' ? 'zh' : 'en'
  const main = root.querySelector<HTMLAnchorElement>('[data-download-main]')
  const toggle = root.querySelector<HTMLButtonElement>('[data-download-toggle]')
  const menu = root.querySelector<HTMLElement>('[data-download-menu]')
  if (!main || !toggle || !menu) return

  const detected = detectPlatform()

  // transitions-dev menu dropdown: `.is-open` grows the menu from its trigger;
  // close swaps to `.is-closing` for the softer exit, cleaned up after
  // --dropdown-close-dur so the next open starts from the rest scale.
  const closeMs =
    parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--dropdown-close-dur')) || 150

  const openMenu = (): void => {
    menu.hidden = false
    menu.classList.remove('is-closing')
    // Paint the hidden rest state first so the open transition plays.
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

  // Idempotent — re-run safely after hydration resets the SSR markup.
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

  // Wire listeners once; the flag survives hydration so the toggle isn't double-wired.
  if (!root._dcDownloadWired) {
    root._dcDownloadWired = true

    void warmAssets()

    if (detected) {
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

    // Re-render after load so the menu survives SSR hydration.
    if (document.readyState !== 'complete') {
      window.addEventListener('load', render, { once: true })
    }
  }

  render()
}

interface Asset {
  name: string
  url: string
}

/** Cached so a click resolves synchronously (downloads need the user gesture). */
let assetCache: Asset[] | null = null

function startDownload(platform: Platform): void {
  const cached = resolveSync(platform)
  if (cached) {
    window.location.href = cached
    return
  }
  // Cold path: cache not ready yet — resolve then navigate, releases page on failure.
  void fetchLatestAssets()
    .then((assets) => {
      const match = assets.find((a) => a.name.endsWith(platform.suffix))
      window.location.href = match ? match.url : RELEASES_PAGE
    })
    .catch(() => {
      window.location.href = RELEASES_PAGE
    })
}

function resolveSync(platform: Platform): string | null {
  const assets = assetCache ?? readCache()
  if (assets) assetCache = assets
  return assets?.find((a) => a.name.endsWith(platform.suffix))?.url ?? null
}

/** Populate the cache ahead of any click (called once on setup). */
async function warmAssets(): Promise<void> {
  if (assetCache) return
  try {
    await fetchLatestAssets()
  } catch {
    // Offline / rate-limited — clicks fall back to the releases page.
  }
}

async function fetchLatestAssets(): Promise<Asset[]> {
  const cached = assetCache ?? readCache()
  if (cached) {
    assetCache = cached
    return cached
  }

  const response = await fetch(LATEST_API, { headers: { Accept: 'application/vnd.github+json' } })
  if (!response.ok) throw new Error(`GitHub API ${response.status}`)
  const data = (await response.json()) as { assets?: Array<{ name: string; browser_download_url: string }> }
  const assets: Asset[] = (data.assets ?? []).map((a) => ({ name: a.name, url: a.browser_download_url }))
  assetCache = assets
  writeCache(assets)
  return assets
}

function readCache(): Asset[] | null {
  try {
    const raw = sessionStorage.getItem(CACHE_KEY)
    return raw ? (JSON.parse(raw) as Asset[]) : null
  } catch {
    return null
  }
}

function writeCache(assets: Asset[]): void {
  try {
    sessionStorage.setItem(CACHE_KEY, JSON.stringify(assets))
  } catch {
    // sessionStorage unavailable (private mode) — resolve fresh each time.
  }
}

/**
 * Best-effort platform detection. macOS arch isn't reliably exposed, so Macs
 * default to Apple Silicon (Intel stays in the menu); mobile/unknown returns
 * null so the generic label shows.
 */
function detectPlatform(): Platform | null {
  const ua = navigator.userAgent
  const uaData = (navigator as Navigator & { userAgentData?: { platform?: string; mobile?: boolean } }).userAgentData

  if (uaData?.mobile || /Android|iPhone|iPad|iPod/i.test(ua)) return null

  const platform = (uaData?.platform ?? '').toLowerCase()
  const isWindows = platform.includes('windows') || /Windows/i.test(ua)
  const isMac = platform.includes('macos') || platform.includes('mac') || /Macintosh|Mac OS X/i.test(ua)
  const isLinux = platform.includes('linux') || /Linux/i.test(ua)

  if (isWindows) {
    const arm = /ARM64|aarch64/i.test(ua)
    return find(arm ? 'win-arm64' : 'win-x64')
  }
  if (isMac) return find('mac-arm64')
  if (isLinux) return find('linux-x64')
  return null
}

function find(id: string): Platform | null {
  return PLATFORMS.find((p) => p.id === id) ?? null
}

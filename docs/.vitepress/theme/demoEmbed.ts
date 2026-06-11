/**
 * Homepage hero embed for the Desktop web demo.
 *
 * The hero renders a poster image inside `.dc-demo`. After the host page has
 * loaded, this module checks that the built demo exists, mounts an iframe over
 * the poster, and fades it in. The iframe ignores pointer input until the
 * visitor explicitly activates it, so page scrolling is never hijacked;
 * Escape or clicking outside deactivates it again.
 */
import { withBase } from 'vitepress'

/** Logical size the simulator renders at; scaled down to fit the hero slot. */
const FRAME_WIDTH = 1280
const FRAME_HEIGHT = 800
/** Below this viewport width the hero stacks; keep the static poster. */
const MIN_EMBED_VIEWPORT = 861

export function setupDemoEmbed(): void {
  const container = document.querySelector<HTMLElement>('.dc-demo')
  if (!container || container.dataset.demoInit === 'true') return
  container.dataset.demoInit = 'true'

  if (!window.matchMedia(`(min-width: ${MIN_EMBED_VIEWPORT}px)`).matches) return

  const lang = container.dataset.demoLang === 'zh' ? 'zh' : 'en'
  const demoBase = withBase('/demo/')

  const mount = (): void => {
    // Confirm the demo build actually exists. A status/HEAD check is not
    // enough: the VitePress dev server answers unknown paths with the SPA
    // shell (HTTP 200), which would recursively embed the docs site itself.
    void fetch(`${demoBase}index.html`)
      .then(async (response) => {
        if (!response.ok) return
        const body = await response.text()
        if (body.includes('<title>DotCraft Desktop Demo</title>')) {
          mountFrame(container, demoBase, lang)
        }
      })
      .catch(() => {
        // No demo build available (e.g. local docs dev); the poster stays.
      })
  }

  if (document.readyState === 'complete') {
    mount()
  } else {
    window.addEventListener('load', mount, { once: true })
  }
}

function isDarkTheme(): boolean {
  return document.documentElement.classList.contains('dark')
}

function mountFrame(container: HTMLElement, demoBase: string, lang: string): void {
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches

  const iframe = document.createElement('iframe')
  iframe.className = 'dc-demo__frame'
  iframe.title = 'DotCraft Desktop live demo'
  iframe.setAttribute('loading', 'lazy')
  iframe.width = String(FRAME_WIDTH)
  iframe.height = String(FRAME_HEIGHT)

  const applyScale = (): void => {
    const scale = container.clientWidth / FRAME_WIDTH
    iframe.style.transform = `scale(${scale})`
  }

  const src = `${demoBase}?theme=${isDarkTheme() ? 'dark' : 'light'}&lang=${lang}`

  const activate = (): void => {
    container.classList.add('dc-demo--active')
  }
  const deactivate = (): void => {
    container.classList.remove('dc-demo--active')
  }

  const button = document.createElement('button')
  button.type = 'button'
  button.className = 'dc-demo__activate'
  button.textContent = lang === 'zh' ? '试一试 Live Demo' : 'Try the live demo'
  button.addEventListener('click', (event) => {
    event.stopPropagation()
    activate()
  })

  iframe.addEventListener('load', () => {
    applyScale()
    container.classList.add('dc-demo--ready')
    container.appendChild(button)
  })

  const loadFrame = (): void => {
    iframe.src = src
    container.appendChild(iframe)
  }

  // Reduced-motion visitors keep the poster until they explicitly opt in.
  if (reducedMotion) {
    const optIn = document.createElement('button')
    optIn.type = 'button'
    optIn.className = 'dc-demo__activate'
    optIn.textContent = lang === 'zh' ? '加载交互演示' : 'Load the live demo'
    optIn.addEventListener('click', () => {
      optIn.remove()
      loadFrame()
    }, { once: true })
    container.appendChild(optIn)
  } else {
    loadFrame()
  }

  window.addEventListener('resize', applyScale)

  document.addEventListener('click', (event) => {
    if (!container.contains(event.target as Node)) deactivate()
  })
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') deactivate()
  })

  // Keep the embedded demo's theme in lockstep with the site appearance.
  const themeObserver = new MutationObserver(() => {
    iframe.contentWindow?.postMessage(
      { type: 'dotcraft-demo:set-theme', theme: isDarkTheme() ? 'dark' : 'light' },
      '*'
    )
  })
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
}

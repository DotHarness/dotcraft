import { withBase } from 'vitepress'

export const DEMO_WIDTH = 1280
export const MIN_EMBED_VIEWPORT = 861

export async function findDemo(): Promise<string | null> {
  const page = withBase('/demo/index.html')
  // The dev server answers unknown paths with the site shell (200), so check the title.
  try {
    const response = await fetch(page)
    if (!response.ok) return null
    return (await response.text()).includes('<title>DotCraft Desktop Demo</title>') ? page : null
  } catch {
    return null
  }
}

export function fitDemo(container: HTMLElement, frame: HTMLIFrameElement): void {
  const zoom = Math.min(1, container.clientWidth / DEMO_WIDTH)
  frame.style.zoom = String(zoom)
  frame.style.width = `${DEMO_WIDTH}px`
  frame.style.height = `${container.clientHeight / zoom}px`
}

export function demoRendered(frame: HTMLIFrameElement | undefined): boolean {
  const doc = frame?.contentDocument
  return !doc || !!doc.getElementById('root')?.childElementCount
}

export function setDemoTheme(frame: HTMLIFrameElement | undefined, dark: boolean): void {
  frame?.contentWindow?.postMessage({ type: 'dotcraft-demo:set-theme', theme: dark ? 'dark' : 'light' }, '*')
}

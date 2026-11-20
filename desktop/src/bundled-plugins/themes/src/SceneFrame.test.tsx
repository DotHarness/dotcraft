import { act, fireEvent, render } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Theme, ThemeBackdrop } from '../../../shared/themes'
import { SceneFrame } from './SceneFrame'
import { BUILTIN_THEMES } from './builtInThemes'
import { MCP_APP_SANDBOX_PROXY_READY_METHOD } from '../../../shared/mcpAppSandbox'
import { SCENE_STATUS_METHOD, svgDataUrl } from './sceneDocument'
import { reportBackdropReady } from './themeLibrary'

vi.mock('./themeLibrary', () => ({ themeKey: (theme: { id: string; revision: number }) => `${theme.id}:${theme.revision}`, reportBackdropReady: vi.fn() }))
const svgTheme = BUILTIN_THEMES.find((candidate) => candidate.backdrop?.kind === 'scene' && candidate.backdrop.format === 'svg')!
const svgScene = svgTheme.backdrop as Extract<ThemeBackdrop, { kind: 'scene' }>
const htmlScene: Extract<ThemeBackdrop, { kind: 'scene' }> = {
  kind: 'scene',
  format: 'html',
  source: '<canvas></canvas><script>window.draw = true</script>',
  poster: svgScene.poster
}
const htmlTheme: Theme = {
  id: 'custom-canvas',
  name: 'Canvas',
  colorScheme: 'dark',
  revision: 1,
  updatedAt: '2026-11-20T00:00:00.000Z',
  backdrop: htmlScene
}
const nonce = '00000000-0000-4000-8000-000000000001'
let motionListener: () => void
let reduced: boolean

function send(frame: HTMLIFrameElement, method: string, params?: unknown): void {
  const event = new MessageEvent('message', { data: { method, params } })
  Object.defineProperty(event, 'source', { value: frame.contentWindow })
  window.dispatchEvent(event)
}

function setHidden(hidden: boolean): void {
  act(() => {
    Object.defineProperty(document, 'hidden', { value: hidden, configurable: true })
    document.dispatchEvent(new Event('visibilitychange'))
  })
}

beforeEach(() => {
  vi.clearAllMocks()
  reduced = false
  document.documentElement.removeAttribute('data-reduce-motion')
  Object.defineProperty(document, 'hidden', { value: false, configurable: true })
  vi.spyOn(crypto, 'randomUUID').mockReturnValue(nonce)
  vi.stubGlobal('matchMedia', () => ({ get matches() { return reduced }, addEventListener: (_event: string, listener: () => void) => { motionListener = listener }, removeEventListener: vi.fn() }))
})

describe('themes execution frame', () => {
  it('renders SVG scenes as non-interactive images, keeps them alive across visibility changes, and swaps to the poster on reduced motion or error', () => {
    const { container, rerender } = render(<SceneFrame theme={svgTheme} scene={svgScene} animate />)
    expect(container.querySelector('iframe')).toBeNull()
    const img = container.querySelector('img')!
    expect(img.getAttribute('src')).toBe(svgDataUrl(svgScene.source))
    fireEvent.load(img)
    expect(reportBackdropReady).toHaveBeenLastCalledWith(svgTheme, true)

    setHidden(true)
    setHidden(false)
    expect(container.querySelector('img')).toBe(img)
    expect(img.getAttribute('src')).toBe(svgDataUrl(svgScene.source))

    act(() => { reduced = true; motionListener() })
    expect(img.getAttribute('src')).toBe(svgDataUrl(svgScene.poster))
    act(() => { reduced = false; motionListener() })
    expect(img.getAttribute('src')).toBe(svgDataUrl(svgScene.source))

    fireEvent.error(img)
    expect(img.getAttribute('src')).toBe(svgDataUrl(svgScene.poster))
    expect(reportBackdropReady).toHaveBeenLastCalledWith(svgTheme, false)
    fireEvent.load(img)
    expect(reportBackdropReady).toHaveBeenLastCalledWith(svgTheme, true)

    rerender(<SceneFrame theme={svgTheme} scene={svgScene} animate={false} />)
    expect(img.getAttribute('src')).toBe(svgDataUrl(svgScene.poster))
  })

  it('boots HTML scenes through the isolated proxy, refuses foreign status messages, and tears down on a scene error', () => {
    const { container } = render(<SceneFrame theme={htmlTheme} scene={htmlScene} animate />)
    const frame = container.querySelector('iframe')!
    expect(frame.getAttribute('sandbox')).toBe('allow-scripts')
    expect(frame.getAttribute('name')).toBe('dotcraft-themes')
    expect(frame.getAttribute('src')).toContain('dotcraft-mcp-sandbox://')
    const post = vi.fn()
    Object.defineProperty(frame, 'contentWindow', { value: { postMessage: post }, configurable: true })
    act(() => send(frame, MCP_APP_SANDBOX_PROXY_READY_METHOD))
    expect(post).toHaveBeenCalledWith(expect.objectContaining({ params: { html: expect.any(String) } }), '*')
    act(() => window.dispatchEvent(new MessageEvent('message', { source: window, data: { method: SCENE_STATUS_METHOD, params: { nonce, state: 'ready' } } })))
    expect(reportBackdropReady).not.toHaveBeenCalledWith(htmlTheme, true)
    act(() => send(frame, SCENE_STATUS_METHOD, { nonce, state: 'ready' }))
    expect(reportBackdropReady).toHaveBeenLastCalledWith(htmlTheme, true)
    act(() => send(frame, SCENE_STATUS_METHOD, { nonce, state: 'failed' }))
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.querySelector('img')).not.toBeNull()
    expect(reportBackdropReady).toHaveBeenLastCalledWith(htmlTheme, false)
  })

  it('keeps HTML execution mounted while hidden and unmounts on reduced motion or disabled animation', () => {
    const { container, rerender } = render(<SceneFrame theme={htmlTheme} scene={htmlScene} animate />)
    const original = container.querySelector('iframe')!
    setHidden(true)
    expect(container.querySelector('iframe')).toBe(original)
    setHidden(false)
    expect(container.querySelector('iframe')).toBe(original)

    act(() => { reduced = true; motionListener() })
    expect(container.querySelector('iframe')).toBeNull()
    fireEvent.load(container.querySelector('img')!)
    expect(reportBackdropReady).toHaveBeenLastCalledWith(htmlTheme, true)
    act(() => { reduced = false; motionListener() })
    expect(container.querySelector('iframe')).not.toBeNull()
    expect(container.querySelector('iframe')).not.toBe(original)

    rerender(<SceneFrame theme={htmlTheme} scene={htmlScene} animate={false} />)
    expect(container.querySelector('iframe')).toBeNull()
  })

  it('falls back to the poster and keeps the theme drawn when HTML startup times out', () => {
    vi.useFakeTimers()
    try {
      const { container } = render(<SceneFrame theme={htmlTheme} scene={htmlScene} animate />)
      expect(container.querySelector('iframe')).not.toBeNull()
      act(() => vi.advanceTimersByTime(8000))
      expect(container.querySelector('iframe')).toBeNull()
      expect(reportBackdropReady).toHaveBeenLastCalledWith(htmlTheme, false)
      fireEvent.load(container.querySelector('img')!)
      expect(reportBackdropReady).toHaveBeenLastCalledWith(htmlTheme, true)
    } finally {
      vi.useRealTimers()
    }
  })
})

import { act, fireEvent, render, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SceneFrame } from './SceneFrame'
import { BUILTIN_THEMES } from './builtInThemes'
import { MCP_APP_SANDBOX_PROXY_READY_METHOD } from '../../../shared/mcpAppSandbox'
import { SCENE_STATUS_METHOD } from './sceneDocument'
import { reportBackdropReady } from './themeLibrary'

vi.mock('./themeLibrary', () => ({ themeKey: (theme: { id: string; revision: number }) => `${theme.id}:${theme.revision}`, reportBackdropReady: vi.fn() }))
const theme = BUILTIN_THEMES.find((candidate) => candidate.backdrop?.kind === 'scene')!
const scene = theme.backdrop as Extract<typeof theme.backdrop, { kind: 'scene' }>
const nonce = '00000000-0000-4000-8000-000000000001'
let motionListener: () => void
let reduced: boolean

function send(frame: HTMLIFrameElement, method: string, params?: unknown): void {
  const event = new MessageEvent('message', { data: { method, params } })
  Object.defineProperty(event, 'source', { value: frame.contentWindow })
  window.dispatchEvent(event)
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
  it('boots through the isolated proxy, refuses foreign status messages, and tears down on a scene error', async () => {
    const { container } = render(<SceneFrame theme={theme} scene={scene} animate />)
    await waitFor(() => expect(container.querySelector('iframe')).not.toBeNull())
    const frame = container.querySelector('iframe')!
    expect(frame.getAttribute('sandbox')).toBe('allow-scripts')
    expect(frame.getAttribute('name')).toBe('dotcraft-themes')
    await waitFor(() => expect(frame.getAttribute('src')).toContain('dotcraft-mcp-sandbox://'))
    const post = vi.fn()
    Object.defineProperty(frame, 'contentWindow', { value: { postMessage: post }, configurable: true })
    act(() => send(frame, MCP_APP_SANDBOX_PROXY_READY_METHOD))
    expect(post).toHaveBeenCalledWith(expect.objectContaining({ params: { html: expect.any(String) } }), '*')
    act(() => window.dispatchEvent(new MessageEvent('message', { source: window, data: { method: SCENE_STATUS_METHOD, params: { nonce, state: 'ready' } } })))
    expect(reportBackdropReady).not.toHaveBeenCalledWith(theme, true)
    act(() => send(frame, SCENE_STATUS_METHOD, { nonce, state: 'ready' }))
    expect(reportBackdropReady).toHaveBeenLastCalledWith(theme, true)
    act(() => send(frame, SCENE_STATUS_METHOD, { nonce, state: 'failed' }))
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.querySelector('img')).not.toBeNull()
    expect(reportBackdropReady).toHaveBeenLastCalledWith(theme, false)
  })

  it('unmounts animation on reduced motion and resumes in a new execution frame', async () => {
    const { container } = render(<SceneFrame theme={theme} scene={scene} animate />)
    await waitFor(() => expect(container.querySelector('iframe')).not.toBeNull())
    const original = container.querySelector('iframe')
    act(() => { reduced = true; motionListener() })
    expect(container.querySelector('iframe')).toBeNull()
    fireEvent.load(container.querySelector('img')!)
    expect(reportBackdropReady).toHaveBeenLastCalledWith(theme, true)
    act(() => { reduced = false; motionListener() })
    expect(container.querySelector('iframe')).not.toBeNull()
    expect(container.querySelector('iframe')).not.toBe(original)
  })

  it('unmounts execution while hidden or when animation is disabled', async () => {
    const { container, rerender } = render(<SceneFrame theme={theme} scene={scene} animate />)
    await waitFor(() => expect(container.querySelector('iframe')).not.toBeNull())
    act(() => {
      Object.defineProperty(document, 'hidden', { value: true, configurable: true })
      document.dispatchEvent(new Event('visibilitychange'))
    })
    expect(container.querySelector('iframe')).toBeNull()
    act(() => {
      Object.defineProperty(document, 'hidden', { value: false, configurable: true })
      document.dispatchEvent(new Event('visibilitychange'))
    })
    expect(container.querySelector('iframe')).not.toBeNull()
    rerender(<SceneFrame theme={theme} scene={scene} animate={false} />)
    expect(container.querySelector('iframe')).toBeNull()
  })

  it('falls back to the poster and keeps the theme drawn when startup times out', async () => {
    vi.useFakeTimers()
    const { container } = render(<SceneFrame theme={theme} scene={scene} animate />)
    await act(async () => { await Promise.resolve() })
    expect(container.querySelector('iframe')).not.toBeNull()
    act(() => vi.advanceTimersByTime(8000))
    expect(container.querySelector('iframe')).toBeNull()
    expect(reportBackdropReady).toHaveBeenLastCalledWith(theme, false)
    fireEvent.load(container.querySelector('img')!)
    expect(reportBackdropReady).toHaveBeenLastCalledWith(theme, true)
  })
})

// @vitest-environment jsdom
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { buildVisualizationDocument } from '../components/conversation/visualizationSecurity'

interface ViewApi {
  displayMode: string
  maxHeight: number
  requestDisplayMode: (options: { mode: string }) => Promise<{ mode: string }>
}

const posted: Array<{ method: string; params: Record<string, unknown> }> = []

beforeAll(() => {
  const html = buildVisualizationDocument('<div></div>', 'dark', 'en', 'view-test', { widgetState: null, displayMode: 'inline', maxHeight: 10_000 }, { design: 'Design', designs: 'Designs', previousDesign: 'Previous design', nextDesign: 'Next design' })
  vi.spyOn(window, 'postMessage').mockImplementation(message => { posted.push(message as (typeof posted)[number]) })
  new Function(/<body><script>([\s\S]*?)<\/script>/.exec(html)![1])()
})

function view(): ViewApi {
  return (window as unknown as { openai: ViewApi }).openai
}

function fromHost(method: string, params: Record<string, unknown>): void {
  const event = new MessageEvent('message', { data: { method, params: { ...params, viewId: 'view-test' } } })
  Object.defineProperty(event, 'source', { value: window })
  window.dispatchEvent(event)
}

describe('visualization bridge display mode', () => {
  it('requests a display mode from the host and resolves with the granted mode', async () => {
    const pending = view().requestDisplayMode({ mode: 'fullscreen' })
    const request = posted.at(-1)!
    expect(request).toEqual({ method: 'visualization/requestDisplayMode', params: { mode: 'fullscreen', id: expect.any(String), viewId: 'view-test' } })

    fromHost('visualization/result', { id: request.params.id, ok: true, result: { mode: 'inline' } })

    await expect(pending).resolves.toEqual({ mode: 'inline' })
    await expect(view().requestDisplayMode({ mode: 'pip' })).rejects.toThrow('Unsupported display mode')
  })

  it('updates displayMode and maxHeight from host globals and announces them', () => {
    const listener = vi.fn()
    addEventListener('openai:set_globals', listener)
    expect(view()).toMatchObject({ displayMode: 'inline', maxHeight: 10_000 })

    fromHost('visualization/globals', { globals: { displayMode: 'fullscreen', maxHeight: 640 } })

    expect(view()).toMatchObject({ displayMode: 'fullscreen', maxHeight: 640 })
    expect((listener.mock.calls[0][0] as CustomEvent).detail.globals).toEqual({ displayMode: 'fullscreen', maxHeight: 640 })
    expect(Object.isFrozen(view())).toBe(true)
  })
})

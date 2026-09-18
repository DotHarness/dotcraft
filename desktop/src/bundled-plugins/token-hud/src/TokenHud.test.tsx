import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import { render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TokenHud } from './TokenHud'
import type { UsageState } from './usage'

const state = vi.hoisted(() => ({
  usage: {
    tokensPerSecond: null,
    waitingForSample: false,
    totalTokens: null,
    cacheHitRate: null,
    firstTokenLatencyMs: null
  } as UsageState
}))

vi.mock('./usage', () => ({
  getUsage: () => state.usage,
  subscribeUsage: () => () => undefined
}))

vi.mock('./settings', () => {
  const settings = { visible: true, opacity: 80 }
  return {
    getSettings: () => settings,
    subscribeSettings: () => () => undefined
  }
})

function renderHud(usage: Partial<UsageState>, busy = false): HTMLElement {
  state.usage = { ...state.usage, ...usage }
  const host = {
    session: { workspacePath: '/workspace', threadId: 'thread-1', mode: 'agent', busy, onChange: () => () => undefined },
    environment: { locale: 'en' }
  }
  return render(<TokenHud host={host as unknown as DesktopPluginSurfaceProps<'composer.status.trailing'>['host']} />).container
}

const metric = (container: HTMLElement, name: string): string | null =>
  container.querySelector(`[data-metric="${name}"]`)?.textContent ?? null

describe('TokenHud timing cells', () => {
  it('hides both speed and latency while idle with nothing measured', () => {
    const container = renderHud({ totalTokens: 562_000, cacheHitRate: 0.77 })
    expect(metric(container, 'speed')).toBeNull()
    expect(metric(container, 'latency')).toBeNull()
    expect(metric(container, 'total')).toBe('562Ktotal')
  })

  it('shows a pending speed while a turn is measuring', () => {
    const container = renderHud({ totalTokens: 562_000, cacheHitRate: 0.77 }, true)
    expect(metric(container, 'speed')).toBe('…tok/s')
    expect(metric(container, 'latency')).toBeNull()
  })

  it('renders nothing when no metric is available', () => {
    const container = renderHud({ totalTokens: null, cacheHitRate: null })
    expect(container.firstChild).toBeNull()
  })
})

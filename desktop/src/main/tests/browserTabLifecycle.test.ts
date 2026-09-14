import { describe, expect, it, vi } from 'vitest'
import { BrowserTabLifecycle, readBrowserTurnNotification, type LifecycleTab } from '../browserTabLifecycle'

function fixture() {
  const lifecycle = new BrowserTabLifecycle()
  const tabs = new Map<string, LifecycleTab>([
    ['agent', { id: 'agent' }],
    ['user', { id: 'user', adopted: true, userOwned: true }]
  ])
  const effects = {
    release: vi.fn((tab: LifecycleTab) => { tabs.delete(tab.id) }),
    retain: vi.fn(),
    close: vi.fn((tab: LifecycleTab) => { tabs.delete(tab.id) })
  }
  return { lifecycle, tabs, effects }
}

describe('browser turn lifecycle', () => {
  it.each(['completed', 'failed', 'cancelled'])('reads the actual %s turn envelope', (status) => {
    expect(readBrowserTurnNotification(`turn/${status}`, { threadId: 'thread', turn: { id: 'turn' } }))
      .toEqual({ threadId: 'thread', turnId: 'turn', terminal: true })
    expect(readBrowserTurnNotification(`turn/${status}`, { threadId: 'thread', turnId: 'turn' }))
      .toEqual({ threadId: 'thread', turnId: 'turn', terminal: true })
    expect(readBrowserTurnNotification(`turn/${status}`, { turn: { id: 'turn', threadId: 'thread' } }))
      .toEqual({ threadId: 'thread', turnId: 'turn', terminal: true })
  })

  it('ignores evaluations and notifications without real turn identity', () => {
    expect(readBrowserTurnNotification('item/completed', { threadId: 'thread', turnId: 'turn' })).toBeUndefined()
    expect(readBrowserTurnNotification('turn/completed', { threadId: 'thread' })).toBeUndefined()
  })

  it('releases deliverables and user pages permanently without closing them', () => {
    const { lifecycle, tabs, effects } = fixture()
    lifecycle.recordUse('one')
    const delivered = tabs.get('agent')
    lifecycle.mark('agent', 'deliverable')
    lifecycle.recordUse('one')
    expect(lifecycle.finishTurn('one', tabs.values(), effects)).toBe(true)
    expect(effects.release).toHaveBeenCalledWith(delivered, 'deliverable')
    expect(tabs.size).toBe(0)
    lifecycle.recordUse('two')
    expect(lifecycle.finishTurn('one', tabs.values(), effects)).toBe(false)
    lifecycle.finishTurn('two', tabs.values(), effects)
    expect(effects.close).not.toHaveBeenCalled()
  })

  it('keeps handoff pages through chat but consumes marks before the next browser turn', () => {
    const { lifecycle, tabs, effects } = fixture()
    lifecycle.recordUse('one')
    lifecycle.mark('agent', 'handoff')
    lifecycle.finishTurn('one', tabs.values(), effects)
    lifecycle.beginTurn('chat')
    expect(lifecycle.finishTurn('chat', tabs.values(), effects)).toBe(false)
    expect(tabs.has('agent')).toBe(true)
    lifecycle.recordUse('two')
    expect(lifecycle.finishTurn('one', tabs.values(), effects)).toBe(false)
    lifecycle.finishTurn('two', tabs.values(), effects)
    expect(tabs.size).toBe(0)
    expect(effects.close).toHaveBeenCalledTimes(1)
    expect(lifecycle.finishTurn('two', tabs.values(), effects)).toBe(false)
  })

  it('carries explicit finalize keep through terminal cleanup', () => {
    const { lifecycle, tabs, effects } = fixture()
    lifecycle.recordUse('one')
    expect(lifecycle.finalize(tabs.values(), new Map([['agent', 'handoff']]), effects))
      .toEqual({ ok: true, kept: ['agent'], released: ['user'], closed: [] })
    lifecycle.finishTurn('one', tabs.values(), effects)
    expect([...tabs.keys()]).toEqual(['agent'])
    expect(effects.retain).toHaveBeenCalledTimes(2)
    expect(effects.retain).toHaveBeenLastCalledWith(tabs.get('agent'), 'handoff')
  })
})

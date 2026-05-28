import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { useToastStore } from '../stores/toastStore'
import { useThreadStore } from '../stores/threadStore'
import { useConversationStore } from '../stores/conversationStore'
import { useConnectionStore } from '../stores/connectionStore'
import { addRecentWorkspace, clearRecentWorkspaces, getRecentWorkspaces } from '../../main/settings'
import type { AppSettings } from '../../main/settings'

// ─── Helpers ──────────────────────────────────────────────────────────────────

const ts = () => useToastStore.getState()
const thr = () => useThreadStore.getState()
const conv = () => useConversationStore.getState()
const conn = () => useConnectionStore.getState()

beforeEach(() => {
  // Reset all stores
  useToastStore.setState({ toasts: [] })
  thr().reset()
  conv().reset()
  conn().reset()
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

// ─── toastStore ───────────────────────────────────────────────────────────────

describe('toastStore', () => {
  it('starts with empty toasts list', () => {
    expect(ts().toasts).toHaveLength(0)
  })

  it('addToast adds a toast with id, message, type, duration', () => {
    ts().addToast('hello', 'info', 5000)
    const toasts = ts().toasts
    expect(toasts).toHaveLength(1)
    expect(toasts[0].message).toBe('hello')
    expect(toasts[0].type).toBe('info')
    expect(toasts[0].duration).toBe(5000)
    expect(toasts[0].id).toMatch(/^toast-/)
  })

  it('removeToast removes the correct toast', () => {
    ts().addToast('first', 'success', 5000)
    ts().addToast('second', 'error', 5000)
    const firstId = ts().toasts[0].id
    ts().removeToast(firstId)
    const remaining = ts().toasts
    expect(remaining).toHaveLength(1)
    expect(remaining[0].message).toBe('second')
  })

  it('auto-dismisses toast after its duration', () => {
    ts().addToast('auto', 'info', 3000)
    expect(ts().toasts).toHaveLength(1)
    vi.advanceTimersByTime(3001)
    expect(ts().toasts).toHaveLength(0)
  })

  it('does not auto-dismiss before duration', () => {
    ts().addToast('not yet', 'warning', 5000)
    vi.advanceTimersByTime(4999)
    expect(ts().toasts).toHaveLength(1)
  })

  it('stacks multiple toasts', () => {
    ts().addToast('one', 'info', 5000)
    ts().addToast('two', 'success', 5000)
    ts().addToast('three', 'error', 5000)
    expect(ts().toasts).toHaveLength(3)
  })

  it('each toast has a unique id', () => {
    ts().addToast('a', 'info', 5000)
    ts().addToast('b', 'info', 5000)
    const ids = ts().toasts.map((t) => t.id)
    expect(new Set(ids).size).toBe(2)
  })
})

// ─── Recent workspaces (settings module) ─────────────────────────────────────

describe('recent workspaces LRU', () => {
  it('adds a workspace to the front of the list', () => {
    const settings: AppSettings = {}
    addRecentWorkspace(settings, '/path/to/workspace-a')
    const recents = getRecentWorkspaces(settings)
    expect(recents).toHaveLength(1)
    expect(recents[0].path).toBe('/path/to/workspace-a')
    expect(recents[0].name).toBe('workspace-a')
  })

  it('moves an existing workspace to the front on re-open', () => {
    const settings: AppSettings = {}
    addRecentWorkspace(settings, '/path/a')
    addRecentWorkspace(settings, '/path/b')
    addRecentWorkspace(settings, '/path/a') // open again
    const recents = getRecentWorkspaces(settings)
    expect(recents[0].path).toBe('/path/a')
    expect(recents).toHaveLength(2)
  })

  it('deduplicates by path', () => {
    const settings: AppSettings = {}
    addRecentWorkspace(settings, '/same/path')
    addRecentWorkspace(settings, '/same/path')
    addRecentWorkspace(settings, '/same/path')
    expect(getRecentWorkspaces(settings)).toHaveLength(1)
  })

  it('evicts oldest entry when more than 20 are added', () => {
    const settings: AppSettings = {}
    for (let i = 1; i <= 21; i++) {
      addRecentWorkspace(settings, `/path/workspace-${i}`)
    }
    const recents = getRecentWorkspaces(settings)
    expect(recents).toHaveLength(20)
    // The oldest (workspace-1) should have been evicted
    expect(recents.some((r) => r.path === '/path/workspace-1')).toBe(false)
    // The newest (workspace-21) should be at the front
    expect(recents[0].path).toBe('/path/workspace-21')
  })

  it('stores lastOpenedAt as an ISO date string', () => {
    const settings: AppSettings = {}
    const before = new Date().toISOString()
    addRecentWorkspace(settings, '/path/ws')
    const after = new Date().toISOString()
    const entry = getRecentWorkspaces(settings)[0]
    expect(entry.lastOpenedAt >= before).toBe(true)
    expect(entry.lastOpenedAt <= after).toBe(true)
  })

  it('clears recent workspaces without touching other settings', () => {
    const settings: AppSettings = {
      modulesDirectory: '/modules',
      locale: 'en'
    }
    addRecentWorkspace(settings, '/path/a')
    addRecentWorkspace(settings, '/path/b')
    const previousLastWorkspacePath = settings.lastWorkspacePath

    clearRecentWorkspaces(settings)

    expect(getRecentWorkspaces(settings)).toEqual([])
    expect(settings.lastWorkspacePath).toBe(previousLastWorkspacePath)
    expect(settings.modulesDirectory).toBe('/modules')
    expect(settings.locale).toBe('en')
  })

  it('clearRecentWorkspaces is idempotent for empty lists', () => {
    const settings: AppSettings = {}

    clearRecentWorkspaces(settings)
    clearRecentWorkspaces(settings)

    expect(getRecentWorkspaces(settings)).toEqual([])
  })
})

// ─── Workspace switch: store clearing ────────────────────────────────────────

describe('workspace switch: stores clear on disconnected/connecting status', () => {
  it('threadStore.reset() clears thread list and active thread', () => {
    // Simulate having loaded some threads
    thr().setThreadList([
      { id: 'thread-1', displayName: 'Test', status: 'active', lastActiveAt: new Date().toISOString(), threadMode: 'agent' }
    ])
    thr().setActiveThreadId('thread-1')
    expect(thr().threadList).toHaveLength(1)
    expect(thr().activeThreadId).toBe('thread-1')

    // Workspace switch triggers reset
    thr().reset()

    expect(thr().threadList).toHaveLength(0)
    expect(thr().activeThreadId).toBeNull()
    expect(thr().activeThread).toBeNull()
  })

  it('conversationStore.reset() clears turns, streaming state, and plan', () => {
    conv().onTurnStarted({
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'running',
      items: [],
      startedAt: new Date().toISOString()
    })
    expect(conv().turns.length).toBeGreaterThan(0)
    expect(conv().turnStatus).toBe('running')

    conv().reset()

    expect(conv().turns).toHaveLength(0)
    expect(conv().turnStatus).toBe('idle')
    expect(conv().streamingMessage).toBe('')
    expect(conv().plan).toBeNull()
  })

  it('runningTurnThreadIds is cleared on threadStore.reset()', () => {
    thr().markTurnStarted('thread-1')
    thr().markTurnStarted('thread-2')
    expect(thr().runningTurnThreadIds.size).toBe(2)

    thr().reset()

    expect(thr().runningTurnThreadIds.size).toBe(0)
  })
})

// ─── Activity indicator (runningTurnThreadIds) ────────────────────────────────

describe('threadStore.runningTurnThreadIds', () => {
  it('markTurnStarted adds threadId to the set', () => {
    thr().markTurnStarted('thread-abc')
    expect(thr().runningTurnThreadIds.has('thread-abc')).toBe(true)
  })

  it('markTurnEnded removes threadId from the set', () => {
    thr().markTurnStarted('thread-abc')
    thr().markTurnEnded('thread-abc')
    expect(thr().runningTurnThreadIds.has('thread-abc')).toBe(false)
  })

  it('markTurnEnded on unknown thread is a no-op', () => {
    expect(() => thr().markTurnEnded('nonexistent')).not.toThrow()
  })

  it('tracks multiple threads simultaneously', () => {
    thr().markTurnStarted('t1')
    thr().markTurnStarted('t2')
    thr().markTurnStarted('t3')
    expect(thr().runningTurnThreadIds.size).toBe(3)

    thr().markTurnEnded('t2')
    expect(thr().runningTurnThreadIds.size).toBe(2)
    expect(thr().runningTurnThreadIds.has('t1')).toBe(true)
    expect(thr().runningTurnThreadIds.has('t3')).toBe(true)
  })
})

// ─── Clipboard copy: extract last agentMessage text ──────────────────────────

describe('clipboard copy: extract last agentMessage', () => {
  it('finds the last agentMessage item across multiple turns', () => {
    // Set up two turns, each with agent messages
    conv().onTurnStarted({
      id: 'turn-1', threadId: 'th', status: 'completed',
      items: [
        { id: 'i1', type: 'agentMessage', status: 'completed', text: 'First reply',
          createdAt: new Date().toISOString(), completedAt: new Date().toISOString() }
      ],
      startedAt: new Date().toISOString()
    })
    conv().onTurnStarted({
      id: 'turn-2', threadId: 'th', status: 'running',
      items: [
        { id: 'i2', type: 'agentMessage', status: 'completed', text: 'Second reply',
          createdAt: new Date().toISOString(), completedAt: new Date().toISOString() }
      ],
      startedAt: new Date().toISOString()
    })

    const turns = conv().turns
    let lastAgentText: string | null = null
    for (let i = turns.length - 1; i >= 0; i--) {
      const items = turns[i].items
      for (let j = items.length - 1; j >= 0; j--) {
        const item = items[j]
        if (item.type === 'agentMessage' && item.text) {
          lastAgentText = item.text
          break
        }
      }
      if (lastAgentText) break
    }
    expect(lastAgentText).toBe('Second reply')
  })

  it('returns null when there are no agentMessage items', () => {
    conv().onTurnStarted({
      id: 'turn-1', threadId: 'th', status: 'running',
      items: [
        { id: 'i1', type: 'userMessage', status: 'completed', text: 'hi',
          createdAt: new Date().toISOString(), completedAt: new Date().toISOString() }
      ],
      startedAt: new Date().toISOString()
    })

    const turns = conv().turns
    let lastAgentText: string | null = null
    for (let i = turns.length - 1; i >= 0; i--) {
      const items = turns[i].items
      for (let j = items.length - 1; j >= 0; j--) {
        const item = items[j]
        if (item.type === 'agentMessage' && item.text) {
          lastAgentText = item.text
          break
        }
      }
      if (lastAgentText) break
    }
    expect(lastAgentText).toBeNull()
  })
})

// ─── Scroll position cache (logic validation) ────────────────────────────────

describe('scroll position cache round-trip', () => {
  it('stores and retrieves scroll positions for multiple threads', () => {
    const cache = new Map<string, number>()

    // Simulate saving scroll position when leaving thread
    cache.set('thread-1', 350)
    cache.set('thread-2', 0)
    cache.set('thread-3', 9999)

    expect(cache.get('thread-1')).toBe(350)
    expect(cache.get('thread-2')).toBe(0)
    expect(cache.get('thread-3')).toBe(9999)
  })

  it('near-bottom detection: scroll within 50px of bottom restores to bottom', () => {
    const NEAR_BOTTOM_THRESHOLD = 50
    const scrollHeight = 1000
    const clientHeight = 400

    // Saved position near bottom (within 50px)
    const savedPos = 555 // scrollHeight - savedPos - clientHeight = 45 < 50
    const atBottom = scrollHeight - savedPos - clientHeight <= NEAR_BOTTOM_THRESHOLD
    expect(atBottom).toBe(true)

    // Saved position far from bottom
    const savedPosFar = 400 // scrollHeight - 400 - 400 = 200 > 50
    const notAtBottom = scrollHeight - savedPosFar - clientHeight <= NEAR_BOTTOM_THRESHOLD
    expect(notAtBottom).toBe(false)
  })

  it('overwrites existing cache entry on re-visit', () => {
    const cache = new Map<string, number>()
    cache.set('thread-1', 100)
    cache.set('thread-1', 250)
    expect(cache.get('thread-1')).toBe(250)
  })
})

// ─── connectionStore.setStatus ────────────────────────────────────────────────

describe('connectionStore.setStatus', () => {
  it('updates status to connected with serverInfo', () => {
    conn().setStatus({
      status: 'connected',
      serverInfo: { name: 'DotCraft AppServer', version: '1.0.0' },
      capabilities: {}
    })
    expect(conn().status).toBe('connected')
    expect(conn().serverInfo?.name).toBe('DotCraft AppServer')
    expect(conn().errorMessage).toBeNull()
  })

  it('updates status to error with errorType', () => {
    conn().setStatus({
      status: 'error',
      errorMessage: 'binary not found',
      errorType: 'binary-not-found'
    })
    expect(conn().status).toBe('error')
    expect(conn().errorType).toBe('binary-not-found')
    expect(conn().errorMessage).toBe('binary not found')
  })

  it('clears error info when switching to connecting', () => {
    conn().setStatus({ status: 'error', errorMessage: 'crash', errorType: 'crash' })
    conn().setStatus({ status: 'connecting' })
    expect(conn().status).toBe('connecting')
    expect(conn().errorMessage).toBeNull()
    expect(conn().errorType).toBeNull()
  })
})

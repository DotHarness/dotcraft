import { describe, expect, it } from 'vitest'
import { mergeUpdatedSettings } from '../settingsMerge'
import type { AppSettings } from '../settings'

describe('mergeUpdatedSettings', () => {
  it('merges other nested transport settings without dropping unspecified fields', () => {
    const current: AppSettings = {
      webSocket: {
        host: '127.0.0.1',
        port: 9100
      },
      remote: {
        url: 'wss://example.test/ws',
        token: 'abc'
      }
    }

    const next = mergeUpdatedSettings(current, {
      webSocket: {
        port: 9200
      },
      remote: {
        url: 'wss://other.test/ws'
      }
    })

    expect(next.webSocket).toEqual({
      host: '127.0.0.1',
      port: 9200
    })
    expect(next.remote).toEqual({
      url: 'wss://other.test/ws',
      token: 'abc'
    })
  })

  it('keeps lastOpenEditorId when explicitly provided', () => {
    const current: AppSettings = {
      lastOpenEditorId: 'explorer'
    }

    const next = mergeUpdatedSettings(current, {
      lastOpenEditorId: 'cursor'
    })

    expect(next.lastOpenEditorId).toBe('cursor')
  })

  it('merges notification settings without dropping unspecified fields', () => {
    const current: AppSettings = {
      notifications: {
        taskCompletionMode: 'whenUnfocused'
      }
    }

    const next = mergeUpdatedSettings(current, {
      notifications: {
        taskCompletionMode: 'never'
      }
    })

    expect(next.notifications).toEqual({
      taskCompletionMode: 'never'
    })
  })

  it('merges pinned thread ids by workspace without dropping other workspaces', () => {
    const current: AppSettings = {
      pinnedThreadIdsByWorkspace: {
        'E:\\Git\\dotcraft': ['thread-a'],
        'E:\\Git\\oratorio': ['thread-b']
      }
    }

    const next = mergeUpdatedSettings(current, {
      pinnedThreadIdsByWorkspace: {
        'E:\\Git\\dotcraft': ['thread-c', 'thread-a']
      }
    })

    expect(next.pinnedThreadIdsByWorkspace).toEqual({
      'E:\\Git\\dotcraft': ['thread-c', 'thread-a'],
      'E:\\Git\\oratorio': ['thread-b']
    })
  })
})

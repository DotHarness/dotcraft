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

  it('merges profile settings when provided', () => {
    const current: AppSettings = {}

    const next = mergeUpdatedSettings(current, {
      profile: { githubUsername: 'octocat' }
    })

    expect(next.profile).toEqual({ githubUsername: 'octocat' })
  })

  it('merges pinned thread ids by workspace without dropping other workspaces', () => {
    const current: AppSettings = {
      pinnedThreadIdsByWorkspace: {
        'C:\\Projects\\dotcraft-sample': ['thread-a'],
        'C:\\Projects\\workflow-sample': ['thread-b']
      }
    }

    const next = mergeUpdatedSettings(current, {
      pinnedThreadIdsByWorkspace: {
        'C:\\Projects\\dotcraft-sample': ['thread-c', 'thread-a']
      }
    })

    expect(next.pinnedThreadIdsByWorkspace).toEqual({
      'C:\\Projects\\dotcraft-sample': ['thread-c', 'thread-a'],
      'C:\\Projects\\workflow-sample': ['thread-b']
    })
  })

  it('merges manual thread orders by project without dropping other projects', () => {
    const current: AppSettings = {
      threadOrderByProject: {
        'c:/projects/a': ['thread-a', 'thread-b'],
        'c:/projects/b': ['thread-c']
      }
    }

    const next = mergeUpdatedSettings(current, {
      threadOrderByProject: { 'c:/projects/a': ['thread-b', 'thread-a'] }
    })

    expect(next.threadOrderByProject).toEqual({
      'c:/projects/a': ['thread-b', 'thread-a'],
      'c:/projects/b': ['thread-c']
    })
  })

  it('merges turn bookmarks by thread without dropping other threads', () => {
    const current: AppSettings = {
      turnBookmarksByThread: {
        'c:/ws::thread-1': ['turn-1:user-1'],
        'c:/ws::thread-2': ['turn-2:user-2']
      }
    }

    const next = mergeUpdatedSettings(current, {
      turnBookmarksByThread: { 'c:/ws::thread-1': [] }
    })

    expect(next.turnBookmarksByThread).toEqual({
      'c:/ws::thread-1': [],
      'c:/ws::thread-2': ['turn-2:user-2']
    })
  })
})

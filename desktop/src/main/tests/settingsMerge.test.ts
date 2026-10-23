import { describe, expect, it } from 'vitest'
import { mergeUpdatedSettings } from '../settingsMerge'
import type { AppSettings } from '../settings'
import { rememberOpenTarget } from '../../shared/openTargets'

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

  it('remembers an explicit open target pick for the project and globally without dropping other projects', () => {
    const current: AppSettings = {
      openTargetPreferences: { global: 'vscode', perProject: { '/projects/alpha': 'rider' } }
    }

    const next = mergeUpdatedSettings(current, {
      openTargetPreferences: rememberOpenTarget('zed', '/projects/beta')
    })

    expect(next.openTargetPreferences).toEqual({
      global: 'zed',
      perProject: { '/projects/alpha': 'rider', '/projects/beta': 'zed' }
    })
  })

  it('changes only the global open target from Settings', () => {
    const current: AppSettings = {
      openTargetPreferences: { global: 'vscode', perProject: { '/projects/alpha': 'rider' } }
    }

    const next = mergeUpdatedSettings(current, { openTargetPreferences: { global: 'fileManager' } })

    expect(next.openTargetPreferences).toEqual({
      global: 'fileManager',
      perProject: { '/projects/alpha': 'rider' }
    })
  })

  it('merges per-workspace and per-thread maps by key without dropping other entries', () => {
    const current: AppSettings = {
      pinnedThreadIdsByWorkspace: {
        'C:\\Projects\\dotcraft-sample': ['thread-a'],
        'C:\\Projects\\workflow-sample': ['thread-b']
      },
      threadOrderByProject: {
        'c:/projects/a': ['thread-a', 'thread-b'],
        'c:/projects/b': ['thread-c']
      },
      turnBookmarksByThread: {
        'c:/ws::thread-1': ['turn-1:user-1'],
        'c:/ws::thread-2': ['turn-2:user-2']
      }
    }

    const next = mergeUpdatedSettings(current, {
      pinnedThreadIdsByWorkspace: { 'C:\\Projects\\dotcraft-sample': ['thread-c', 'thread-a'] },
      threadOrderByProject: { 'c:/projects/a': ['thread-b', 'thread-a'] },
      turnBookmarksByThread: { 'c:/ws::thread-1': [] }
    })

    expect(next.pinnedThreadIdsByWorkspace).toEqual({
      'C:\\Projects\\dotcraft-sample': ['thread-c', 'thread-a'],
      'C:\\Projects\\workflow-sample': ['thread-b']
    })
    expect(next.threadOrderByProject).toEqual({
      'c:/projects/a': ['thread-b', 'thread-a'],
      'c:/projects/b': ['thread-c']
    })
    expect(next.turnBookmarksByThread).toEqual({
      'c:/ws::thread-1': [],
      'c:/ws::thread-2': ['turn-2:user-2']
    })
  })
})

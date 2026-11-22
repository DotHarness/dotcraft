import { afterEach, describe, expect, it } from 'vitest'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { validateActivation } from '../plugins/desktopPluginValidation'
import { publishDesktopPluginGeneration, withdrawDesktopPluginGeneration } from '../plugins/desktopPluginRegistry'
import { useOwnedThreadsStore } from '../stores/ownedThreadsStore'
import { leaveSpace, resolveActiveSpace, useSpaceStore } from '../stores/spaceStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { OwnedThreadRecord } from '../../shared/ownedThreads'

const host = {} as DesktopPluginHost
const component = (): null => null
const label = { default: 'Oratorio' }

function activation(views: string[]) {
  return {
    mainViews: [{ id: 'board', label, component }],
    spaces: [{ id: 'oratorio', label, views, sidebar: component, open: () => {} }]
  }
}

const RECORD: OwnedThreadRecord = {
  pluginId: 'oratorio', key: 'conductor', space: 'oratorio', title: 'Oratorio',
  threadId: 'conductor-thread', workspacePath: '/chats', fingerprint: ''
}

afterEach(() => {
  withdrawDesktopPluginGeneration('oratorio')
  useOwnedThreadsStore.getState().setRecords([])
  useSpaceStore.setState({ returnTo: null, outside: null, entering: false, last: {} })
})

describe('Desktop extension spaces', () => {
  it('rejects a space that names a view the plugin does not contribute', () => {
    expect(() => validateActivation('oratorio', '1.0.0', 'r1', host, activation(['missing']))).toThrow(/views/)
  })

  it('opens the space on its views and on threads the plugin owns for it', () => {
    const generation = validateActivation('oratorio', '1.0.0', 'r1', host, activation(['board']))
    const spaces = generation.spaces
    const [boardView] = spaces[0].viewKeys

    expect(resolveActiveSpace(spaces, boardView, null, [])).toMatchObject({ location: { kind: 'view', viewId: 'board' } })
    expect(resolveActiveSpace(spaces, 'conversation', 'conductor-thread', [RECORD]))
      .toMatchObject({ location: { kind: 'thread', threadId: 'conductor-thread' } })
    expect(resolveActiveSpace(spaces, 'conversation', 'other-thread', [RECORD])).toBeNull()
    expect(resolveActiveSpace(spaces, 'conversation', 'conductor-thread', [{ ...RECORD, space: null }])).toBeNull()
  })

  it('leaves to a new chat when the place it was entered from no longer exists', async () => {
    publishDesktopPluginGeneration(validateActivation('oratorio', '1.0.0', 'r1', host, activation(['board'])))
    useOwnedThreadsStore.getState().setRecords([RECORD])
    useUIStore.getState().setActiveMainView('conversation')
    useThreadStore.getState().setActiveThreadId('conductor-thread')
    useSpaceStore.setState({
      returnTo: {
        projectKey: 'removed-project',
        location: { kind: 'conversation', threadId: 'removed-thread', detailVisible: false, activeDetailTab: { kind: 'launcher' }, selectedChangeKey: null }
      }
    })

    await leaveSpace()

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })
})

import { afterEach, describe, expect, it } from 'vitest'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { validateActivation } from '../plugins/desktopPluginValidation'
import { publishDesktopPluginGeneration, withdrawDesktopPluginGeneration } from '../plugins/desktopPluginRegistry'
import { useOwnedThreadsStore } from '../stores/ownedThreadsStore'
import { beginPendingThreadArchive, endPendingThreadArchive } from '../stores/pendingThreadArchives'
import { leaveSpace, resolveActiveSpace, startSpaceTracking, useSpaceStore } from '../stores/spaceStore'
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

  it('keeps the place to return to while a move inside the space switches workspace', async () => {
    const generation = validateActivation('oratorio', '1.0.0', 'r1', host, activation(['board']))
    publishDesktopPluginGeneration(generation)
    useOwnedThreadsStore.getState().setRecords([RECORD])
    useUIStore.getState().setActiveMainView(generation.spaces[0].viewKeys[0])
    const stop = startSpaceTracking()
    const target = { projectKey: 'project', location: { kind: 'agents' as const } }
    useSpaceStore.setState({ returnTo: target })

    const request = useUIStore.getState().setPendingProjectThreadOpen({ projectKey: '/chats', workspacePath: '/chats', threadId: RECORD.threadId })
    useUIStore.getState().setActiveMainView('conversation')
    await Promise.resolve()
    useUIStore.getState().consumePendingProjectThreadOpen('/chats', [RECORD.threadId], request?.requestId)
    useThreadStore.getState().setActiveThreadId(RECORD.threadId)
    await Promise.resolve()
    stop()

    expect(useSpaceStore.getState().returnTo).toEqual(target)
  })

  it('goes back to where the space was entered from when its thread is archived', async () => {
    publishDesktopPluginGeneration(validateActivation('oratorio', '1.0.0', 'r1', host, activation(['board'])))
    useOwnedThreadsStore.getState().setRecords([RECORD])
    useUIStore.getState().setActiveMainView('conversation')
    useThreadStore.getState().setActiveThreadId(RECORD.threadId)
    const stop = startSpaceTracking()
    useSpaceStore.setState({ returnTo: { projectKey: null, location: { kind: 'agents' } } })

    const projectKey = useThreadStore.getState().threadListProjectKey
    beginPendingThreadArchive(projectKey ?? '', RECORD.threadId, new Set([RECORD.threadId]))
    useThreadStore.getState().setActiveThreadId(null)
    await Promise.resolve()
    stop()
    endPendingThreadArchive(projectKey ?? '', RECORD.threadId)

    expect(useUIStore.getState().activeMainView).toBe('agents')
  })
})

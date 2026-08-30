import type { DesktopPluginHost, DesktopPluginSessionSnapshot } from '@dotcraft/plugin'
import { waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { clearDesktopPluginKernel } from '../plugins/desktopPluginKernel'
import {
  clearDesktopPluginRegistry,
  useDesktopPluginRegistry
} from '../plugins/desktopPluginRegistry'
import { DesktopPluginRuntime } from '../plugins/desktopPluginRuntime'
import { useConversationStore } from '../stores/conversationStore'
import type { PluginEntry } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useToastStore } from '../stores/toastStore'

const revision = 'a'.repeat(64)
let runtime: DesktopPluginRuntime | null = null

beforeEach(() => {
  clearDesktopPluginRegistry()
  clearDesktopPluginKernel()
  useToastStore.setState({ toasts: [] })
  resetStores()
})

afterEach(async () => {
  await runtime?.stop()
  runtime = null
  clearDesktopPluginRegistry()
  clearDesktopPluginKernel()
  vi.restoreAllMocks()
  resetStores()
})

function resetStores(): void {
  useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: '' })
  useThreadStore.setState({ activeThread: null, activeThreadId: null })
  useConversationStore.setState({ turnStatus: 'idle', threadMode: 'agent' })
}

function activeThread(id: string): void {
  useThreadStore.setState({
    activeThreadId: id,
    activeThread: { id, workspacePath: 'X:\\threads\\other' } as never
  })
}

function plugin(enabled = true): PluginEntry {
  return {
    id: 'fixture.desktop',
    displayName: 'Fixture Desktop Plugin',
    version: '1.0.0',
    enabled,
    installed: true,
    installable: false,
    removable: false,
    source: 'local',
    rootPath: 'X:\\fixtures\\fixture.desktop',
    functions: [],
    skills: [],
    apps: [],
    desktop: { entry: './desktop/dist/index.mjs', revision, styles: [] },
    mcpServers: [],
    lspServers: []
  }
}

async function activate(register: (host: DesktopPluginHost) => void): Promise<DesktopPluginHost> {
  let host!: DesktopPluginHost
  runtime = new DesktopPluginRuntime({
    registerModule: async () => ({ entryUrl: 'dotcraft-plugin://fixture.desktop/entry.mjs', styleUrls: [] }),
    removeModule: async () => ({ ok: true }),
    importModule: async () => ({
      activate: (value: DesktopPluginHost) => {
        host = value
        register(value)
        return { mainViews: [], settingsPages: [] }
      }
    })
  })
  runtime.reconcile([plugin()])
  await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
  return host
}

/** Wraps a store's `subscribe` so the test can prove the watcher let go of that store. */
function watchSubscription(store: { subscribe: (listener: () => void) => () => void }): () => number {
  const subscribe = store.subscribe.bind(store)
  const released = vi.fn()
  vi.spyOn(store, 'subscribe').mockImplementation((listener: () => void) => {
    const stop = subscribe(listener)
    return () => {
      released()
      stop()
    }
  })
  return () => released.mock.calls.length
}

describe('host.session', () => {
  it('composes the foreground workspace, thread, mode, and busy state', async () => {
    useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: 'X:\\workspaces\\alpha' })
    activeThread('thread-1')
    useConversationStore.setState({ turnStatus: 'running', threadMode: 'plan' })
    const host = await activate(() => {})

    expect({
      workspacePath: host.session.workspacePath,
      threadId: host.session.threadId,
      mode: host.session.mode,
      busy: host.session.busy
    }).toEqual({
      workspacePath: 'X:\\workspaces\\alpha',
      threadId: 'thread-1',
      mode: 'plan',
      busy: true
    })
  })

  it('reports no workspace before one is in the foreground', async () => {
    const host = await activate(() => {})
    expect(host.session.workspacePath).toBeNull()
    expect(host.session.threadId).toBeNull()
  })

  it('stays idle for an approval, which is Composer presentation rather than session state', async () => {
    useConversationStore.setState({ turnStatus: 'waitingApproval' })
    const host = await activate(() => {})
    expect(host.session.busy).toBe(false)
  })

  it('notifies on a change and stays quiet for a store write outside the snapshot', async () => {
    const changes: DesktopPluginSessionSnapshot[] = []
    await activate((host) => host.session.onChange((session) => changes.push(session)))

    useThreadStore.setState({ loading: true })
    expect(changes).toHaveLength(0)

    useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: 'X:\\workspaces\\beta' })
    activeThread('thread-2')
    useConversationStore.setState({ turnStatus: 'waitingInput' })

    expect(changes).toEqual([
      { workspacePath: 'X:\\workspaces\\beta', threadId: null, mode: 'agent', busy: false },
      { workspacePath: 'X:\\workspaces\\beta', threadId: 'thread-2', mode: 'agent', busy: false },
      { workspacePath: 'X:\\workspaces\\beta', threadId: 'thread-2', mode: 'agent', busy: true }
    ])

    useConversationStore.setState({ turnStatus: 'waitingInput' })
    expect(changes).toHaveLength(3)
  })

  it('detaches all three store subscriptions when the generation is disposed', async () => {
    const released = [
      watchSubscription(useWorkspaceProjectsStore),
      watchSubscription(useThreadStore),
      watchSubscription(useConversationStore)
    ]
    const listener = vi.fn()
    await activate((host) => {
      host.session.onChange(listener)
      host.session.onChange(() => {})
    })

    runtime!.reconcile([plugin(false)])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(0))

    expect(released.map((count) => count())).toEqual([1, 1, 1])
    useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: 'X:\\workspaces\\gamma' })
    expect(listener).not.toHaveBeenCalled()
  })
})

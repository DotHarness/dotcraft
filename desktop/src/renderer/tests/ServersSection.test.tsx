import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DevServerList, DevServerSnapshot } from '../../shared/devServers'
import { ServersSection } from '../components/conversation/summary/ServersSection'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import type { ThreadSummary } from '../types/thread'

const FOLDER = '/work/app'

function server(overrides: Partial<DevServerSnapshot>): DevServerSnapshot {
  return {
    serverId: 'web-1',
    name: 'web',
    threadId: 'thread-1',
    folder: FOLDER,
    port: 5173,
    url: 'http://localhost:5173',
    status: 'running',
    startedAt: '2026-11-20T00:00:00.000Z',
    ...overrides
  }
}

let list: DevServerList
let notify: (() => void) | null
const api = {
  list: vi.fn(async () => list),
  watch: vi.fn((_folder: string, listener: () => void) => {
    notify = listener
    return () => { notify = null }
  }),
  stop: vi.fn(async () => undefined)
}

function show() {
  return render(<LocaleProvider loadSettings={false}><ServersSection threadId="thread-1" /></LocaleProvider>)
}

beforeEach(() => {
  list = { launch: { status: 'missing', path: `${FOLDER}/.craft/launch.json` }, servers: [] }
  notify = null
  Object.assign(window, { api: { ...(window as unknown as { api?: object }).api, devServers: api } })
  useConversationStore.setState({ workspacePath: FOLDER, remoteWorkspaceActive: false })
  useThreadStore.setState({ activeThread: null, threadList: [] })
  useToastStore.setState({ toasts: [] })
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe('Summary Servers section', () => {
  it('lists the thread’s live servers and leaves out stopped ones', async () => {
    list = {
      ...list,
      servers: [
        server({}),
        server({ serverId: 'api-1', name: 'api', port: 8080, url: 'http://localhost:8080', status: 'starting' }),
        server({ serverId: 'old-1', name: 'old', status: 'stopped' })
      ]
    }
    show()
    expect(await screen.findByRole('button', { name: /web/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /api/ })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /old/ })).toBeNull()
    expect(api.list).toHaveBeenCalledWith({ folder: FOLDER, threadId: 'thread-1' })
  })

  it('stops a server and follows the change notification', async () => {
    list = { ...list, servers: [server({})] }
    show()
    fireEvent.click(await screen.findByRole('button', { name: 'Stop server' }))
    expect(api.stop).toHaveBeenCalledWith('web-1')

    list = { ...list, servers: [server({ status: 'stopped' })] }
    act(() => notify?.())
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Servers' })).toBeNull())
  })

  it('opens the server in a browser tab of the thread', async () => {
    list = { ...list, servers: [server({})] }
    const openBrowser = vi.spyOn(useDetailPanelStore.getState(), 'openBrowser')
    show()
    fireEvent.click(await screen.findByRole('button', { name: /web/ }))
    expect(openBrowser).toHaveBeenCalledWith(expect.objectContaining({ threadId: 'thread-1', initialUrl: 'http://localhost:5173' }))
  })

  it('reads servers for the thread’s execution folder', async () => {
    useThreadStore.setState({ activeThread: { id: 'thread-1', workspacePath: FOLDER, effectiveWorkspacePath: '/work/app/web' } as ThreadSummary })
    show()
    await waitFor(() => expect(api.list).toHaveBeenCalledWith({ folder: '/work/app/web', threadId: 'thread-1' }))
  })

  it('explains a server whose port another server holds instead of opening the holder’s page', async () => {
    list = {
      ...list,
      servers: [server({ status: 'error', failure: { kind: 'portInUse', port: 5173, holder: 'sameThread', message: 'Port 5173 is in use.' } })]
    }
    const openBrowser = vi.spyOn(useDetailPanelStore.getState(), 'openBrowser')
    show()
    fireEvent.click(await screen.findByRole('button', { name: /web/ }))
    expect(openBrowser).not.toHaveBeenCalled()
    expect(useToastStore.getState().toasts).toHaveLength(1)
  })

  it('shows nothing in a remote workspace', async () => {
    useConversationStore.setState({ remoteWorkspaceActive: true })
    list = { ...list, servers: [server({})] }
    show()
    await act(async () => undefined)
    expect(api.list).not.toHaveBeenCalled()
    expect(screen.queryByRole('region', { name: 'Servers' })).toBeNull()
  })
})

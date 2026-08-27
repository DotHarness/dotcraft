import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useComposerMascot } from '../components/conversation/useComposerMascot'
import { useConversationStore, type PendingApproval } from '../stores/conversationStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const THREAD_ID = 'thread-mascot'
const pluginActions = {
  fetchPlugins: usePluginStore.getState().fetchPlugins,
  installPlugin: usePluginStore.getState().installPlugin
}

const dotcraftPlugin: PluginEntry = {
  id: 'dotcraft',
  displayName: 'DotCraft',
  version: '0.4.0',
  enabled: true,
  installed: false,
  installable: true,
  removable: false,
  source: 'builtIn',
  rootPath: '<built-in>',
  functions: [],
  skills: [],
  mcpServers: [],
  lspServers: []
}

function approval(overrides: Partial<PendingApproval> = {}): PendingApproval {
  return {
    bridgeId: 'bridge-mascot-1',
    threadId: THREAD_ID,
    turnId: 'turn-mascot',
    requestId: 'request-mascot-1',
    locallySubmittedDecision: null,
    itemId: 'approval-mascot-1',
    approvalType: 'shell',
    operation: 'npm test',
    target: '<workspace>',
    reason: 'Run the test suite.',
    ...overrides
  }
}

function MascotHarness(): JSX.Element {
  const interaction = useComposerMascot({ threadId: THREAD_ID, workspacePath: '<workspace>' })
  const bubble = interaction?.bubble
  return (
    <div>
      {bubble && (
        <section aria-label="mascot bubble">
          <h1>{bubble.title}</h1>
          {bubble.body && <p>{bubble.body}</p>}
          {bubble.actions?.map((action) => (
            <button key={action.label} type="button" onClick={action.onClick}>
              {action.label}
            </button>
          ))}
        </section>
      )}
      {interaction?.menuItems.map((item) => (
        <button key={item.label} type="button" onClick={item.onClick}>
          {item.label}
        </button>
      ))}
    </div>
  )
}

function renderHarness(): void {
  render(
    <LocaleProvider>
      <MascotHarness />
    </LocaleProvider>
  )
}

describe('useComposerMascot approval nudge', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
    })
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({ pendingWelcomeTurn: null })
    usePluginStore.setState({
      plugins: [],
      loading: false,
      error: null,
      snapshotRevision: 0,
      completeSnapshotRevision: 0,
      ...pluginActions
    })
  })

  it('does not show an approval bubble for a waiting status without an actionable request', () => {
    useConversationStore.setState({
      turnStatus: 'waitingApproval',
      pendingApproval: null,
      pendingApprovals: []
    })

    renderHarness()

    expect(screen.queryByRole('heading', { name: 'Your approval is needed' })).not.toBeInTheDocument()
  })

  it('keeps the same approval dismissed across rerenders and replay, but shows a new request', async () => {
    const first = approval()
    useConversationStore.setState({
      turnStatus: 'waitingApproval',
      pendingApproval: first,
      pendingApprovals: [first]
    })

    renderHarness()

    expect(await screen.findByRole('heading', { name: 'Your approval is needed' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Got it' }))
    await waitFor(() => {
      expect(screen.queryByRole('heading', { name: 'Your approval is needed' })).not.toBeInTheDocument()
    })

    const replayed = { ...first, reason: 'Same request replayed with updated details.' }
    act(() => {
      useConversationStore.setState({
        pendingApproval: replayed,
        pendingApprovals: [replayed]
      })
    })
    expect(screen.queryByRole('heading', { name: 'Your approval is needed' })).not.toBeInTheDocument()

    const second = approval({
      bridgeId: 'bridge-mascot-2',
      requestId: 'request-mascot-2',
      itemId: 'approval-mascot-2',
      reason: 'Run the next command.'
    })
    act(() => {
      useConversationStore.setState({
        pendingApproval: second,
        pendingApprovals: [second]
      })
    })

    expect(await screen.findByRole('heading', { name: 'Your approval is needed' })).toBeInTheDocument()
  })

  it('confirms and installs the DotCraft plugin before starting issue reporting', async () => {
    const installPlugin = vi.fn().mockResolvedValue({})
    const sendRequest = vi.fn().mockImplementation(async (method: string) => {
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-report',
            displayName: null,
            status: 'idle',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-08-27T00:00:00.000Z',
            lastActiveAt: '2026-08-27T00:00:00.000Z'
          }
        }
      }
      return {}
    })
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: { sendRequest }
    })
    usePluginStore.setState({ plugins: [dotcraftPlugin], installPlugin })

    renderHarness()
    fireEvent.click(screen.getByRole('button', { name: 'Report' }))

    expect(await screen.findByRole('heading', { name: 'Set up the helper first' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Install & continue' }))

    await waitFor(() => expect(installPlugin).toHaveBeenCalledWith('dotcraft'))
    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn?.inputParts?.[0]).toEqual({
        type: 'skillRef',
        name: 'dotcraft-report-issue'
      })
    })
  })

  it('starts diagnosis directly when the DotCraft plugin is installed', async () => {
    const installPlugin = vi.fn().mockResolvedValue({})
    const sendRequest = vi.fn().mockResolvedValue({
      thread: {
        id: 'thread-diagnosis',
        displayName: null,
        status: 'idle',
        originChannel: 'dotcraft-desktop',
        createdAt: '2026-08-27T00:00:00.000Z',
        lastActiveAt: '2026-08-27T00:00:00.000Z'
      }
    })
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: { sendRequest }
    })
    usePluginStore.setState({
      plugins: [{ ...dotcraftPlugin, installed: true, installable: false }],
      installPlugin
    })
    useConversationStore.setState({
      turnStatus: 'running',
      turns: [{
        id: 'turn-failed',
        threadId: THREAD_ID,
        status: 'inProgress',
        items: [],
        startedAt: '2026-08-27T00:00:00.000Z'
      }]
    })

    renderHarness()
    act(() => {
      useConversationStore.setState({
        turnStatus: 'idle',
        turns: [{
          id: 'turn-failed',
          threadId: THREAD_ID,
          status: 'failed',
          error: 'Provider request failed.',
          items: [],
          startedAt: '2026-08-27T00:00:00.000Z'
        }]
      })
    })

    fireEvent.click(await screen.findByRole('button', { name: 'Look into it' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn?.inputParts?.[0]).toEqual({
        type: 'skillRef',
        name: 'dotcraft-error-diagnosis'
      })
    })
    expect(installPlugin).not.toHaveBeenCalled()
  })
})

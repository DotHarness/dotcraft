import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { WorktreeGeneralTab } from '../components/settings/panels/worktrees/WorktreeGeneralTab'
import { usePrefetchSettingsWorktrees } from '../components/settings/panels/worktrees/worktreeSettingsResource'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { activateWorkspaceResources } from '../stores/providerConnectionScope'
import { useSourceControlStore } from '../stores/sourceControlStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const sendConnectionRequest = vi.fn()
const workspace = '/repo'
const git = { effectiveProvider: 'git' }
const emptyText = 'No worktrees in this workspace.'
let connection = 0

function pendingResult() {
  let resolve!: (value: unknown) => void
  let reject!: (error: Error) => void
  const promise = new Promise((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

function connect(): void {
  const connectionId = String(++connection)
  useConnectionStore.setState({ status: 'connected', connectionKey: workspace, connectionId })
  activateWorkspaceResources({ projectKey: workspace, connectionKey: workspace, connectionId })
}

function SettingsVisit({ showWorktrees }: { showWorktrees: boolean }) {
  usePrefetchSettingsWorktrees(workspace)
  return showWorktrees ? <WorktreeGeneralTab workspacePath={workspace} /> : null
}

function settings(showWorktrees = true) {
  return (
    <LocaleProvider loadSettings={false}>
      <SettingsVisit showWorktrees={showWorktrees} />
    </LocaleProvider>
  )
}

describe('WorktreeGeneralTab', () => {
  beforeEach(() => {
    sendRequest.mockReset()
    sendConnectionRequest.mockReset()
    useSourceControlStore.getState().ensure(null, false)
    connect()
    installDesktopApiMock({ initialLocale: 'en', appServer: { sendRequest, sendConnectionRequest } })
  })

  afterEach(() => {
    cleanup()
    activateWorkspaceResources(null)
    useConnectionStore.getState().reset()
    vi.useRealTimers()
  })

  it('prefetches in Settings and reuses the confirmed empty list during a slow refresh on reopen', async () => {
    vi.useFakeTimers()
    sendRequest.mockResolvedValueOnce(git)
    sendConnectionRequest.mockResolvedValueOnce({ data: [] })
    const first = render(settings(false))
    await act(async () => {})
    expect(sendConnectionRequest).toHaveBeenCalledTimes(1)

    first.rerender(settings())
    expect(screen.getByText(emptyText)).toBeVisible()
    expect(sendConnectionRequest).toHaveBeenCalledTimes(1)
    first.unmount()

    const list = pendingResult()
    sendConnectionRequest.mockReturnValueOnce(list.promise)
    const reopened = render(settings(false))
    reopened.rerender(settings())
    act(() => { vi.runOnlyPendingTimers() })
    expect(screen.getByText(emptyText)).toBeVisible()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()

    await act(async () => list.resolve({ data: [{ path: '/wt', branchName: 'topic' }] }))
    expect(screen.getByText('topic')).toBeVisible()
    expect(screen.queryByText(emptyText)).not.toBeInTheDocument()
  })

  it('keeps the branch prefix usable without reporting an empty list on a pending or failed first read', async () => {
    const list = pendingResult()
    sendRequest.mockResolvedValueOnce(git)
    sendConnectionRequest.mockReturnValueOnce(list.promise)
    render(settings())
    await act(async () => {})

    expect(screen.getByRole('textbox', { name: 'Branch prefix' })).toBeEnabled()
    expect(screen.queryByText(emptyText)).not.toBeInTheDocument()

    await act(async () => list.reject(new Error('List unavailable')))
    expect(screen.getByText('List unavailable')).toBeVisible()
    expect(screen.queryByText(emptyText)).not.toBeInTheDocument()
  })

  it('isolates cached and late results after reconnecting to the same workspace', async () => {
    sendRequest.mockResolvedValue(git)
    sendConnectionRequest.mockResolvedValueOnce({ data: [{ path: '/old-wt', branchName: 'old-topic' }] })
    const first = render(settings())
    await act(async () => {})
    expect(screen.getByText('old-topic')).toBeVisible()
    first.unmount()

    const oldList = pendingResult()
    const newList = pendingResult()
    sendConnectionRequest.mockReturnValueOnce(oldList.promise).mockReturnValueOnce(newList.promise)
    render(settings())
    act(connect)
    expect(sendConnectionRequest).toHaveBeenLastCalledWith(workspace, 'worktree/list', {}, undefined, {
      connectionId: useConnectionStore.getState().connectionId
    })
    expect(screen.queryByText('old-topic')).not.toBeInTheDocument()
    expect(screen.queryByText(emptyText)).not.toBeInTheDocument()

    await act(async () => newList.resolve({ data: [] }))
    await act(async () => oldList.resolve({ data: [{ path: '/late-wt', branchName: 'late-topic' }] }))
    expect(screen.queryByText('late-topic')).not.toBeInTheDocument()
    expect(screen.getByText(emptyText)).toBeVisible()
  })

  it('removes a confirmed deletion from the list and ignores an older refresh that still includes it', async () => {
    const existing = { data: [{ path: '/wt', branchName: 'topic' }] }
    sendRequest.mockResolvedValueOnce(git)
    sendConnectionRequest.mockResolvedValueOnce(existing)
    const first = render(settings())
    await act(async () => {})
    first.unmount()

    const oldList = pendingResult()
    const newList = pendingResult()
    sendConnectionRequest.mockReturnValueOnce(oldList.promise).mockResolvedValueOnce({})
      .mockReturnValueOnce(newList.promise)
    render(settings())
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Remove' })) })
    expect(sendConnectionRequest).toHaveBeenCalledWith(workspace, 'worktree/remove', {
      path: '/wt', discardChanges: false
    }, undefined, { connectionId: useConnectionStore.getState().connectionId })
    expect(screen.queryByText('topic')).not.toBeInTheDocument()

    await act(async () => oldList.resolve(existing))
    expect(screen.queryByText('topic')).not.toBeInTheDocument()
    await act(async () => newList.resolve({ data: [] }))
    expect(screen.getByText(emptyText)).toBeVisible()
  })
})

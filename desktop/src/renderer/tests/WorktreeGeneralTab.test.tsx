import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { WorktreeGeneralTab } from '../components/settings/panels/worktrees/WorktreeGeneralTab'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useSourceControlStore } from '../stores/sourceControlStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const workspace = '/repo'
const git = { effectiveProvider: 'git' }
const emptyText = 'No worktrees in this workspace.'

function pendingResult() {
  let resolve!: (value: unknown) => void
  const promise = new Promise((yes) => { resolve = yes })
  return { promise, resolve }
}

function tab(workspacePath = workspace) {
  return (
    <LocaleProvider loadSettings={false}>
      <WorktreeGeneralTab workspacePath={workspacePath} />
    </LocaleProvider>
  )
}

describe('WorktreeGeneralTab', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    sendRequest.mockReset()
    useSourceControlStore.getState().ensure(null, false)
    installDesktopApiMock({ initialLocale: 'en', appServer: { sendRequest } })
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
  })

  it('reopens with known Git settings and waits for the fresh list before reporting it empty', async () => {
    sendRequest.mockResolvedValueOnce(git).mockResolvedValueOnce({ data: [] })
    const first = render(tab())
    await act(async () => {})
    expect(screen.getByText(emptyText)).toBeVisible()
    first.unmount()

    const list = pendingResult()
    sendRequest.mockReturnValueOnce(list.promise)
    render(tab())

    expect(screen.getByRole('textbox', { name: 'Branch prefix' })).toBeEnabled()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    expect(screen.queryByText(emptyText)).not.toBeInTheDocument()
    expect(sendRequest.mock.calls.map(([method]) => method)).toEqual([
      'sourceControl/get', 'worktree/list', 'worktree/list'
    ])

    await act(async () => list.resolve({ data: [{ path: '/wt', branchName: 'topic' }] }))
    act(() => { vi.advanceTimersByTime(300) })
    expect(screen.getByText('topic')).toBeVisible()
    expect(screen.queryByText(emptyText)).not.toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('announces slow loads after the grace period without hiding the available prefix', async () => {
    const provider = pendingResult()
    const list = pendingResult()
    sendRequest.mockReturnValueOnce(provider.promise).mockReturnValueOnce(list.promise)
    render(tab())

    act(() => { vi.advanceTimersByTime(299) })
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    act(() => { vi.advanceTimersByTime(1) })
    expect(screen.getByRole('status')).toHaveAttribute('aria-busy', 'true')

    await act(async () => provider.resolve(git))
    expect(screen.getByRole('textbox', { name: 'Branch prefix' })).toBeEnabled()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    act(() => { vi.advanceTimersByTime(300) })
    expect(screen.getByRole('status')).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByRole('textbox', { name: 'Branch prefix' })).toBeEnabled()
    expect(screen.queryByText(emptyText)).not.toBeInTheDocument()

    await act(async () => list.resolve({ data: [] }))
    expect(screen.getByText(emptyText)).toBeVisible()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('does not use the previous workspace’s Git binding or late worktree results', async () => {
    const oldList = pendingResult()
    const provider = pendingResult()
    sendRequest.mockResolvedValueOnce(git).mockReturnValueOnce(oldList.promise)
      .mockReturnValueOnce(provider.promise).mockResolvedValueOnce({ data: [] })
    const view = render(tab())
    await act(async () => {})
    expect(screen.getByRole('textbox', { name: 'Branch prefix' })).toBeInTheDocument()

    view.rerender(tab('/other-repo'))
    expect(screen.queryByRole('textbox', { name: 'Branch prefix' })).not.toBeInTheDocument()

    await act(async () => provider.resolve(git))
    expect(screen.getByText(emptyText)).toBeVisible()
    await act(async () => oldList.resolve({ data: [{ path: '/old-wt', branchName: 'old-topic' }] }))
    expect(screen.queryByText('old-topic')).not.toBeInTheDocument()
    expect(screen.getByText(emptyText)).toBeVisible()
  })
})

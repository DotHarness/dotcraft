// @vitest-environment jsdom

import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TopThreadsTable } from '../components/settings/usage/TopThreadsTable'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useUIStore } from '../stores/uiStore'
import { useThreadStore } from '../stores/threadStore'
import { installDesktopApiMock } from './desktopApiMock'
import { translate } from '../../shared/locales'

const rows = ['/projects/one', '/projects/two'].map((workspacePath, index) => ({
  workspacePath, threadId: 'shared-thread-id', title: `Task ${index + 1}`, originChannel: 'dotcraft-desktop',
  lastActiveAt: '2026-11-08T00:00:00Z', turns: 1, totalTokens: 100, inputTokens: 80,
  outputTokens: 20, cachedInputTokens: 60, cacheHitRate: 0.75, archived: index === 1
}))

afterEach(() => {
  useWorkspaceProjectsStore.getState().reset()
  useThreadStore.getState().reset()
  useUIStore.setState({ pendingProjectThreadOpen: null })
})

it('routes a ranking row to its local workspace even when the remote foreground has the same path', async () => {
  const switchWorkspace = vi.fn(async () => undefined)
  installDesktopApiMock({ workspace: { switch: switchWorkspace } })
  useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: '/projects/two', foregroundProjectId: 'remote:ssh:sample' })
  render(<LocaleProvider loadSettings={false}>
    <TopThreadsTable state={{ data: rows, loading: false, error: null }} range="7d"
      onRangeChange={vi.fn()} onRetry={vi.fn()} formatDateTime={(value) => value} t={(key, vars) => translate('en', key as never, vars)} />
  </LocaleProvider>)

  fireEvent.click(screen.getByRole('button', { name: 'Task 2' }))

  await waitFor(() => expect(switchWorkspace).toHaveBeenCalledWith('/projects/two'))
  expect(useUIStore.getState().pendingProjectThreadOpen).toEqual({
    projectKey: '/projects/two', workspacePath: '/projects/two', threadId: 'shared-thread-id', includeArchived: true
  })
  expect(useThreadStore.getState().activeThreadId).toBeNull()
})

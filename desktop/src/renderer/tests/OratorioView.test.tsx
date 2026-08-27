import { act, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { LocaleProvider } from '../contexts/LocaleContext'
import { oratorioClient } from '../../bundled-plugins/oratorio/src/oratorio-client'
import { OratorioView } from '../../bundled-plugins/oratorio/src/OratorioView'
import type { ItemSummaryDto, TaskListResponse } from '../../bundled-plugins/oratorio/src/oratorio-contracts'
import type { DesktopPluginOratorioEvent } from '@dotcraft/plugin'
import { installDesktopApiMock } from './desktopApiMock'
import { installOratorioTestHost } from './oratorioPluginTestHost'

function task(title: string): ItemSummaryDto {
  return {
    itemId: 'task-42',
    source: 'github',
    externalId: '42',
    kind: 'pullRequest',
    title,
    repository: 'example/widgets',
    assignee: null,
    branch: 'refs/pull/42/head',
    labels: [],
    state: 'discovered',
    currentRound: 1,
    checkState: 'pending',
    latestSummary: 'Review is pending.',
    createdAt: '2026-08-10T06:00:00Z',
    updatedAt: '2026-08-10T06:10:00Z',
    taskStatus: 'in_progress',
  }
}

function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((next) => { resolve = next })
  return { promise, resolve }
}

describe('OratorioView', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('keeps the current board visible while an event-triggered refresh is pending', async () => {
    let onEvent: ((event: DesktopPluginOratorioEvent) => void) | null = null
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      oratorio: {
        getContext: vi.fn().mockResolvedValue({ provider: 'local', workspacePath: null, connected: true, revision: 1 }),
        onEvent: vi.fn((listener: (event: DesktopPluginOratorioEvent) => void) => { onEvent = listener; return vi.fn() }),
        focusRun: vi.fn().mockResolvedValue(undefined),
      },
    })

    const backgroundRefresh = deferred<TaskListResponse>()
    const listTasks = vi.spyOn(oratorioClient, 'listTasks')
      .mockResolvedValueOnce({ tasks: [task('Original title')], nextCursor: null })
      .mockReturnValueOnce(backgroundRefresh.promise)

    const host = installOratorioTestHost()
    render(
      <LocaleProvider>
        <OratorioView host={host} contributionId="board" />
      </LocaleProvider>,
    )

    expect(await screen.findByText('Original title')).toBeInTheDocument()
    act(() => {
      onEvent?.({ type: 'data-changed', revision: 2 })
      onEvent?.({ type: 'board-event', revision: 3, event: { type: 'task/updated' } })
    })
    await waitFor(() => expect(listTasks).toHaveBeenCalledTimes(2))

    expect(screen.getByText('Original title')).toBeInTheDocument()
    expect(screen.queryByRole('status', { name: 'Loading board' })).not.toBeInTheDocument()

    await act(async () => {
      backgroundRefresh.resolve({ tasks: [task('Updated title')], nextCursor: null })
      await backgroundRefresh.promise
    })
    expect(await screen.findByText('Updated title')).toBeInTheDocument()
  })
})

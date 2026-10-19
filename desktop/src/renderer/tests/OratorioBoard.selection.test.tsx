import './setupPluginRuntime'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { OratorioBoard, type OratorioBoardState } from '../../bundled-plugins/oratorio/src/OratorioBoard'
import { mapItemDetail, mapItemSummary } from '../../bundled-plugins/oratorio/src/oratorio-mappers'
import type { ItemDetailResponse } from '../../bundled-plugins/oratorio/src/oratorio-contracts'
import type { OratorioTask } from '../../bundled-plugins/oratorio/src/oratorio-model'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'
import { installOratorioTestHost } from './oratorioPluginTestHost'

function task(id: string): OratorioTask {
  const detail: ItemDetailResponse = {
    item: {
      itemId: id, shortId: id, workspaceId: 'workspace', source: 'github', externalId: id,
      kind: 'issue', title: `Task ${id}`, description: `Body ${id}`, repository: 'example/project',
      assignee: null, branch: null, labels: [], state: 'discovered', currentRound: 0,
      checkState: 'notConfigured', latestSummary: null, currentRunId: null, lastSourceSyncAt: null,
      createdAt: '2026-10-19T00:00:00Z', updatedAt: '2026-10-19T00:00:00Z', taskStatus: 'todo',
    },
    runs: [], timeline: [],
  }
  return mapItemDetail(detail)
}

function summary(task: OratorioTask): OratorioTask {
  return mapItemSummary(task.detail!.item)
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((next) => { resolve = next })
  return { promise, resolve }
}

const initialState: OratorioBoardState = {
  mode: 'active', query: '', repository: 'all', assignee: 'all', selectedTaskId: 'A', scrollTop: 0,
}

function board(tasks: OratorioTask[], options: Pick<ComponentProps<typeof OratorioBoard>,
  'initialState' | 'onLoadTaskDetail' | 'onStateChange'> = {}) {
  return <LocaleProvider>
    <OratorioBoard tasks={tasks} initialState={initialState} {...options}
      onOpenDetail={vi.fn()} onOpenSettings={vi.fn()} onOpenThread={vi.fn()} />
  </LocaleProvider>
}

function drawer() {
  return within(screen.getByRole('complementary', { name: /Task details for/ }))
}

describe('OratorioBoard selection restoration', () => {
  beforeEach(() => {
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) } })
    installOratorioTestHost()
  })

  it.each(['active', 'all'] as const)('keeps a newly selected task after detail loading and refresh in %s mode', async (mode) => {
    const a = task('A')
    const b = task('B')
    const response = deferred<OratorioTask>()
    const options = {
      initialState: { ...initialState, mode },
      onLoadTaskDetail: vi.fn((selected: OratorioTask) => selected.id === 'B' ? response.promise : Promise.resolve(a)),
      onStateChange: vi.fn(),
    }
    const view = render(board([summary(a), summary(b)], options))
    expect(await drawer().findByText('Body A')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Task B/ }))
    expect(screen.getByRole('complementary', { name: 'Task details for B' })).toBeInTheDocument()
    await act(async () => { response.resolve(b); await response.promise })

    expect(drawer().getByText('Body B')).toBeInTheDocument()
    view.rerender(board([summary(a), summary(b)], options))
    expect(drawer().getByText('Body B')).toBeInTheDocument()
    expect(options.onStateChange).toHaveBeenLastCalledWith(expect.objectContaining({ selectedTaskId: 'B' }))
  })

  it('preserves the saved identity until tasks arrive and restores it only once', () => {
    const a = task('A')
    const onStateChange = vi.fn()
    const view = render(board([], { onStateChange }))
    expect(screen.queryByRole('complementary', { name: /Task details for/ })).not.toBeInTheDocument()
    expect(onStateChange).toHaveBeenLastCalledWith(expect.objectContaining({ selectedTaskId: 'A' }))

    const retainedState = onStateChange.mock.lastCall![0] as OratorioBoardState
    view.rerender(board([a], { initialState: retainedState, onStateChange }))
    expect(screen.getByRole('complementary', { name: 'Task details for A' })).toBeInTheDocument()
    fireEvent.click(drawer().getByRole('button', { name: 'Close', exact: true }))
    view.rerender(board([{ ...a }], { initialState: retainedState, onStateChange }))
    expect(screen.queryByRole('complementary', { name: /Task details for/ })).not.toBeInTheDocument()
    expect(onStateChange).toHaveBeenLastCalledWith(expect.objectContaining({ selectedTaskId: null }))
  })

  it('cancels pending restoration when another task is selected before the saved task arrives', async () => {
    const a = task('A')
    const b = task('B')
    const options = { onLoadTaskDetail: vi.fn().mockResolvedValue(b), onStateChange: vi.fn() }
    const view = render(board([summary(b)], options))
    fireEvent.click(screen.getByRole('button', { name: /Task B/ }))
    expect(await drawer().findByText('Body B')).toBeInTheDocument()

    view.rerender(board([summary(a), summary(b)], options))
    expect(drawer().getByText('Body B')).toBeInTheDocument()
    expect(options.onStateChange).toHaveBeenLastCalledWith(expect.objectContaining({ selectedTaskId: 'B' }))
  })
})

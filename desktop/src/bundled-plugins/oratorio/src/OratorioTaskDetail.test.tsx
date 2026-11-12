import '../../../renderer/tests/setupPluginRuntime'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from '../../../renderer/tests/desktopApiMock'
import { installOratorioTestHost } from '../../../renderer/tests/oratorioPluginTestHost'
import { oratorioClient } from './oratorio-client'
import type { ItemDetailResponse, ItemKind } from './oratorio-contracts'
import { mapItemDetail } from './oratorio-mappers'
import { OratorioTaskDetail } from './OratorioTaskDetail'

function detail(kind: ItemKind = 'issue'): ItemDetailResponse {
  return {
    item: {
      itemId: 'task-1', workspaceId: 'workspace', source: 'gitlab', externalId: '42', kind,
      title: 'Investigate behavior', repository: 'gitlab:gitlab.example.com/team/project',
      assignee: null, branch: null, labels: [], state: 'discovered', currentRound: 1,
      checkState: 'notConfigured', latestSummary: null, createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z', description: null, currentRunId: null, lastSourceSyncAt: null,
    },
    runs: [], timeline: [], discussionTurns: [],
  }
}

function openTask(response = detail()): HTMLTextAreaElement {
  render(<OratorioTaskDetail task={mapItemDetail(response)} onBack={vi.fn()} onOpenThread={vi.fn()} onTaskChange={vi.fn()} />)
  return screen.getByRole('textbox')
}

beforeEach(() => {
  vi.restoreAllMocks()
  installDesktopApiMock({})
  installOratorioTestHost()
})

describe('Oratorio discussion composer', () => {
  it.each<ItemKind>(['issue', 'pullRequest'])('asks Oratorio on Enter and keeps Add comment explicit for %s', async (kind) => {
    const response = detail(kind)
    const ask = vi.spyOn(oratorioClient, 'askAgent').mockResolvedValue(response)
    const comment = vi.spyOn(oratorioClient, 'addComment').mockResolvedValue(response)
    const input = openTask(response)

    fireEvent.change(input, { target: { value: '  Explain this finding  ' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(ask).toHaveBeenCalledExactlyOnceWith('task-1', 'Explain this finding', 1)
    expect(comment).not.toHaveBeenCalled()
    await waitFor(() => expect(input).toHaveValue(''))

    fireEvent.change(input, { target: { value: 'Feedback for later' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add comment' }))
    expect(comment).toHaveBeenCalledExactlyOnceWith('task-1', 'Feedback for later', 1)
    await waitFor(() => expect(input).toHaveValue(''))
  })

  it('does not start another inquiry while one is pending', () => {
    const response = detail()
    response.discussionTurns = [{
      discussionTurnId: 'discussion-1', itemId: 'task-1', roundId: null, questionCommentId: 'question-1',
      replyCommentId: null, replySourceWriteId: null, origin: null, runId: null, threadId: null,
      turnId: null, status: 'pending', errorCode: null, errorMessage: null,
      createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', startedAt: null, completedAt: null,
    }]
    const ask = vi.spyOn(oratorioClient, 'askAgent')
    const input = openTask(response)

    fireEvent.change(input, { target: { value: 'Another question' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    fireEvent.click(screen.getByRole('button', { name: 'Ask Oratorio' }))
    expect(ask).not.toHaveBeenCalled()
    expect(input).toHaveValue('Another question')
  })

  it('blocks keyboard and button submission while an action is in flight', async () => {
    const response = detail()
    let resolve!: (value: ItemDetailResponse) => void
    const pending = new Promise<ItemDetailResponse>((done) => { resolve = done })
    const comment = vi.spyOn(oratorioClient, 'addComment').mockReturnValue(pending)
    const ask = vi.spyOn(oratorioClient, 'askAgent')
    const input = openTask(response)

    fireEvent.change(input, { target: { value: 'Feedback for later' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add comment' }))
    fireEvent.keyDown(input, { key: 'Enter' })
    fireEvent.click(screen.getByRole('button', { name: 'Ask Oratorio' }))
    expect(comment).toHaveBeenCalledTimes(1)
    expect(ask).not.toHaveBeenCalled()
    await act(async () => resolve(response))
  })
})

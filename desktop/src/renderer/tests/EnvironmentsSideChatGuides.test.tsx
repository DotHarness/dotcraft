import { StrictMode } from 'react'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { normalizeFeatureGuides } from '../../shared/featureGuides'
import { EnvironmentsFeatureGuide } from '../components/featureGuides/EnvironmentsFeatureGuide'
import { SideChatFeatureGuide, resetSideChatGuideSignal } from '../components/featureGuides/SideChatFeatureGuide'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useFeatureGuideStore } from '../stores/featureGuideStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { QueuedTurnInput } from '../types/conversation'
import type { ThreadSummary } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'

const PROJECT = '/work/app'

function thread(id: string, worktree: boolean): ThreadSummary {
  return {
    id,
    workspacePath: PROJECT,
    status: 'active',
    ...(worktree ? { worktree: { branchName: 'dotcraft/x', path: '/work/app-x' } } : {})
  } as ThreadSummary
}

function queued(...ids: string[]): QueuedTurnInput[] {
  return ids.map((id) => ({ id, threadId: 't', displayText: id, status: 'queued', createdAt: '2026-11-28T10:00:00Z' }) as QueuedTurnInput)
}

function wrap(node: JSX.Element): JSX.Element {
  return <StrictMode><LocaleProvider loadSettings={false}>{node}</LocaleProvider></StrictMode>
}

beforeEach(() => {
  installDesktopApiMock({ initialLocale: 'en', settings: { set: vi.fn().mockResolvedValue({}) } })
  useFeatureGuideStore.setState({ settings: normalizeFeatureGuides({}), initializedThisSession: false })
  useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: PROJECT })
  useThreadStore.setState({ threadList: [thread('earlier', true)] })
  resetSideChatGuideSignal()
})

afterEach(() => {
  cleanup()
  useFeatureGuideStore.setState(useFeatureGuideStore.getInitialState())
  useWorkspaceProjectsStore.getState().reset()
  useThreadStore.setState({ threadList: [] })
})

it('offers an environment on a second worktree chat and opens setup', async () => {
  const onSetUp = vi.fn()
  render(wrap(<EnvironmentsFeatureGuide workspacePath={PROJECT} available environmentCount={0} onSetUp={onSetUp} />))
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Set up environment' })))
  expect(onSetUp).toHaveBeenCalledTimes(1)
  expect(useFeatureGuideStore.getState().settings?.guides.environments).toMatchObject({ settled: true })
})

it('waits for a chat in the project to have used a worktree', () => {
  useThreadStore.setState({ threadList: [thread('local', false)] })
  render(wrap(<EnvironmentsFeatureGuide workspacePath={PROJECT} available environmentCount={0} onSetUp={vi.fn()} />))
  expect(screen.queryByRole('dialog')).toBeNull()
})

it('settles once the project has an environment', () => {
  render(wrap(<EnvironmentsFeatureGuide workspacePath={PROJECT} available environmentCount={1} onSetUp={vi.fn()} />))
  expect(screen.queryByRole('dialog')).toBeNull()
  expect(useFeatureGuideStore.getState().settings?.guides.environments).toMatchObject({ settled: true })
})

it('offers a side chat on the second queued follow-up and sends the newest one', async () => {
  const send = vi.fn()
  const view = render(wrap(<SideChatFeatureGuide queuedInputs={queued('q1')} onSendInSideChat={send} />))
  expect(screen.queryByRole('dialog')).toBeNull()
  view.rerender(wrap(<SideChatFeatureGuide queuedInputs={queued('q1', 'q2')} onSendInSideChat={send} />))
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Ask in side chat' })))
  expect(send).toHaveBeenCalledWith('q2')
  expect(useFeatureGuideStore.getState().settings?.guides.sideChat).toMatchObject({ settled: true })
})

it('does not offer a side chat when side chats are unavailable', () => {
  render(wrap(<SideChatFeatureGuide queuedInputs={queued('q1', 'q2')} />))
  expect(screen.queryByRole('dialog')).toBeNull()
})

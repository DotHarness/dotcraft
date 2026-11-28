import { StrictMode } from 'react'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { normalizeFeatureGuides } from '../../shared/featureGuides'
import { WorktreesFeatureGuide } from '../components/featureGuides/WorktreesFeatureGuide'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useFeatureGuideStore } from '../stores/featureGuideStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { ThreadSummary } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'

const PROJECT = '/work/app'

function thread(id: string, running: boolean, worktree = false): ThreadSummary {
  return {
    id,
    workspacePath: PROJECT,
    status: 'active',
    runtime: { running, waitingOnApproval: false, waitingOnPlanConfirmation: false },
    ...(worktree ? { worktree: { branchName: 'dotcraft/x', path: '/work/app-x' } } : {})
  } as ThreadSummary
}

function renderGuide(props: Partial<Parameters<typeof WorktreesFeatureGuide>[0]> = {}): ReturnType<typeof render> {
  return render(<StrictMode><LocaleProvider loadSettings={false}>
    <WorktreesFeatureGuide workspacePath={PROJECT} available worktreeMode={false} onUseWorktree={vi.fn()} {...props} />
  </LocaleProvider></StrictMode>)
}

beforeEach(() => {
  installDesktopApiMock({ initialLocale: 'en', settings: { set: vi.fn().mockResolvedValue({}) } })
  useFeatureGuideStore.setState({ settings: normalizeFeatureGuides({}), initializedThisSession: false })
  useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: PROJECT })
  useThreadStore.setState({ threadList: [thread('busy', true)], hiddenThreadIds: new Set(), runningTurnThreadIds: new Set(), runtimeSnapshots: new Map() })
})

afterEach(() => {
  cleanup()
  useFeatureGuideStore.setState(useFeatureGuideStore.getInitialState())
  useWorkspaceProjectsStore.getState().reset()
  useThreadStore.setState({ threadList: [] })
})

it('shows above Work locally while another chat in the project runs', () => {
  renderGuide()
  expect(screen.getByRole('dialog')).toBeInTheDocument()
  expect(useFeatureGuideStore.getState().settings?.guides.worktrees).toMatchObject({ shown: 1 })
})

it('stays hidden when no other chat is running', () => {
  useThreadStore.setState({ threadList: [thread('idle', false)] })
  renderGuide()
  expect(screen.queryByRole('dialog')).toBeNull()
})

it('settles once the project has used a worktree', () => {
  useThreadStore.setState({ threadList: [thread('busy', true), thread('tree', false, true)] })
  renderGuide()
  expect(screen.queryByRole('dialog')).toBeNull()
  expect(useFeatureGuideStore.getState().settings?.guides.worktrees).toMatchObject({ settled: true })
})

it('switches the new chat to a worktree and settles', async () => {
  const onUseWorktree = vi.fn()
  renderGuide({ onUseWorktree })
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Use a worktree' })))
  expect(onUseWorktree).toHaveBeenCalledTimes(1)
  expect(useFeatureGuideStore.getState().settings?.guides.worktrees).toMatchObject({ settled: true })
})

it('releases the slot without settling when the other chat finishes', async () => {
  renderGuide()
  await act(async () => useThreadStore.setState({ threadList: [thread('busy', false)] }))
  expect(screen.queryByRole('dialog')).toBeNull()
  const record = useFeatureGuideStore.getState().settings?.guides.worktrees
  expect(record?.settled).toBeUndefined()
  expect(record?.lastClosedAt).toEqual(expect.any(Number))
})

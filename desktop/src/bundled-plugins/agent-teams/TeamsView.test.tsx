import '../../renderer/tests/setupPluginRuntime'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ButtonHTMLAttributes, InputHTMLAttributes, TextareaHTMLAttributes } from 'react'
import type { DesktopPluginHost } from '@dotcraft/plugin'

vi.mock('@dotcraft/plugin', () => ({
  Button: ({ loading: _loading, variant: _variant, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & {
    loading?: boolean
    variant?: string
  }) => <button {...props} />,
  Input: (props: InputHTMLAttributes<HTMLInputElement>) => <input {...props} />,
  Textarea: (props: TextareaHTMLAttributes<HTMLTextAreaElement>) => <textarea {...props} />
}))

import { TeamsView } from './src/TeamsView'
import type { TeamView } from './src/TeamsView.types'

const sendRequest = vi.fn()
const unsubscribe = vi.fn()
const confirmDialog = vi.fn()
const openThread = vi.fn()
const animateMock = vi.fn()
const notificationHandlers = new Map<string, () => void>()
let resizeObserverCallback: ResizeObserverCallback | undefined
let boardViewportRect = makeRect(0, 0, 1376, 736)
let animationFinishedPromise: Promise<void>
let hostLocale = 'en'

const host = {
  environment: {
    get locale() {
      return hostLocale
    },
    theme: 'light'
  },
  navigation: {
    openMainView: vi.fn(),
    openSettingsPage: vi.fn(),
    openExternal: vi.fn(),
    openThread
  },
  ui: {
    showToast: vi.fn(),
    confirm: confirmDialog
  },
  appServer: {
    request: sendRequest,
    onNotification: vi.fn((method: string, listener: () => void) => {
      notificationHandlers.set(method, listener)
      return unsubscribe
    })
  }
} as unknown as DesktopPluginHost

function notify(method: string): void {
  notificationHandlers.get(method)?.()
}

function makeRect(left: number, top: number, width: number, height: number): DOMRect {
  return {
    left,
    top,
    width,
    height,
    right: left + width,
    bottom: top + height,
    x: left,
    y: top,
    toJSON: () => ({ left, top, width, height })
  } as DOMRect
}

function numericStyle(value: string | undefined, fallback: number): number {
  const parsed = Number.parseFloat(value || '')
  return Number.isFinite(parsed) ? parsed : fallback
}

const teamView: TeamView = {
  team: {
    teamId: 'default',
    createdAt: '2026-05-23T00:00:00Z',
    updatedAt: '2026-05-23T00:00:00Z'
  },
  stats: {
    runningMembers: 1,
    queuedInputs: 0,
    totalTasks: 1,
    completedTasks: 0,
    inputTokens: 0,
    outputTokens: 0,
    cachedInputTokens: 0,
    totalTokens: 0
  },
  members: [
    {
      memberId: 'leader',
      role: 'leader',
      displayName: 'Team Leader',
      description: 'Plans missions.',
      avatarAccent: '#4f7cf6',
      status: 'running',
      deskX: 0,
      deskY: 0
    },
    {
      memberId: 'builder',
      role: 'builder',
      displayName: 'Builder',
      description: 'Builds things.',
      avatarAccent: '#7c3aed',
      status: 'running',
      currentTaskId: 'task-1',
      deskX: 0,
      deskY: 0
    },
    {
      memberId: 'reviewer',
      role: 'reviewer',
      displayName: 'Reviewer',
      description: 'Reviews things.',
      avatarAccent: '#22a45a',
      status: 'idle',
      deskX: 0,
      deskY: 0
    }
  ],
  messages: [],
  missions: [
    {
      missionId: 'mission-1',
      title: 'Ship Teams',
      prompt: 'Ship the feature.',
      status: 'planning',
      leaderThreadId: 'thread-leader',
      createdAt: '2026-05-23T00:00:00Z',
      updatedAt: '2026-05-23T00:00:00Z'
    }
  ],
  archivedMissions: [
    {
      missionId: 'mission-old',
      title: 'Old Mission',
      prompt: 'A completed mission.',
      status: 'done',
      completionSummary: 'Finished.',
      createdAt: '2026-05-20T00:00:00Z',
      updatedAt: '2026-05-20T01:00:00Z',
      archivedAt: '2026-05-20T02:00:00Z'
    }
  ],
  missionThreads: [
    {
      missionId: 'mission-1',
      memberId: 'leader',
      threadId: 'thread-leader',
      status: 'running',
      createdAt: '2026-05-23T00:00:00Z',
      updatedAt: '2026-05-23T00:00:00Z',
      running: true,
      queuedInputCount: 0
    },
    {
      missionId: 'mission-1',
      memberId: 'builder',
      threadId: 'thread-builder',
      status: 'queued',
      currentTaskId: 'task-1',
      createdAt: '2026-05-23T00:00:00Z',
      updatedAt: '2026-05-23T00:00:00Z',
      queuedInputCount: 1
    }
  ],
  tasks: [
    {
      taskId: 'task-1',
      missionId: 'mission-1',
      assigneeMemberId: 'builder',
      title: 'Build card board',
      prompt: 'Implement the card board.',
      status: 'queued',
      createdAt: '2026-05-23T00:00:00Z'
    }
  ],
  artifacts: [],
  mailboxDigests: []
}

const completedTeamView = {
  ...teamView,
  stats: {
    ...teamView.stats,
    runningMembers: 0,
    totalTasks: 1,
    completedTasks: 1
  },
  members: teamView.members.map((member) => member.memberId === 'builder'
    ? { ...member, status: 'idle', currentTaskId: 'task-1' }
    : { ...member, status: 'idle' }),
  missions: teamView.missions.map((mission) => ({
    ...mission,
    status: 'done',
    finalResponse: 'Final response for the user.',
    completedAt: '2026-05-23T01:00:00Z'
  })),
  missionThreads: teamView.missionThreads.map((thread) => ({
    ...thread,
    status: 'done',
    running: false,
    queuedInputCount: 0
  })),
  tasks: teamView.tasks.map((task) => ({
    ...task,
    status: 'done',
    digest: 'A very long digest that should stay clamped on the card face instead of stretching the entire tabletop card until it clips into other cards.'
  }))
}

const cancelledTeamView = {
  ...teamView,
  stats: {
    ...teamView.stats,
    runningMembers: 0,
    queuedInputs: 0
  },
  members: teamView.members.map((member) => ({ ...member, status: 'idle', currentTaskId: null })),
  missions: teamView.missions.map((mission) => ({
    ...mission,
    status: 'cancelled'
  })),
  missionThreads: teamView.missionThreads.map((thread) => ({
    ...thread,
    status: 'idle',
    running: false,
    queuedInputCount: 0,
    currentTaskId: null
  })),
  tasks: teamView.tasks.map((task) => ({
    ...task,
    status: 'cancelled'
  }))
}

const emptyTeamView = {
  ...teamView,
  stats: {
    ...teamView.stats,
    runningMembers: 0,
    totalTasks: 0,
    completedTasks: 0
  },
  members: teamView.members.map((member) => ({ ...member, status: 'idle', currentTaskId: null })),
  missions: [],
  missionThreads: [],
  tasks: []
}

const awaitingLeaderReviewTeamView = {
  ...teamView,
  stats: {
    ...teamView.stats,
    runningMembers: 0,
    totalTasks: 1,
    completedTasks: 1
  },
  members: teamView.members.map((member) => ({ ...member, status: 'idle', currentTaskId: null })),
  missions: teamView.missions.map((mission) => ({
    ...mission,
    status: 'awaitingLeaderReview'
  })),
  missionThreads: teamView.missionThreads.map((thread) => ({
    ...thread,
    status: 'idle',
    running: false,
    queuedInputCount: 0
  })),
  tasks: teamView.tasks.map((task) => ({
    ...task,
    status: 'done',
    digest: 'Builder is done.'
  }))
}

const missionOnlyTeamView = {
  ...teamView,
  stats: {
    ...teamView.stats,
    totalTasks: 0,
    completedTasks: 0
  },
  members: teamView.members.map((member) => member.memberId === 'builder'
    ? { ...member, status: 'idle', currentTaskId: null }
    : member),
  missions: teamView.missions.map((mission) => ({ ...mission, status: 'active' })),
  missionThreads: teamView.missionThreads
    .filter((thread) => thread.memberId === 'leader')
    .map((thread) => ({
      ...thread,
      status: 'running',
      running: true,
      queuedInputCount: 0
    })),
  tasks: []
}

const taskStatusShowcaseTeamView = {
  ...teamView,
  stats: {
    ...teamView.stats,
    runningMembers: 0,
    totalTasks: 3,
    completedTasks: 0
  },
  members: teamView.members.map((member) => ({ ...member, status: 'idle', currentTaskId: null })),
  missions: teamView.missions.map((mission) => ({ ...mission, status: 'active' })),
  missionThreads: teamView.missionThreads.filter((thread) => thread.memberId === 'leader').map((thread) => ({
    ...thread,
    status: 'idle',
    running: false,
    queuedInputCount: 0
  })),
  tasks: [
    {
      taskId: 'task-wait',
      missionId: 'mission-1',
      assigneeMemberId: 'builder',
      title: 'Waiting task',
      prompt: 'Wait for dependencies.',
      status: 'waitingDependencies',
      dependsOnTaskIds: ['task-upstream'],
      createdAt: '2026-05-23T00:00:00Z'
    },
    {
      taskId: 'task-blocked',
      missionId: 'mission-1',
      assigneeMemberId: 'reviewer',
      title: 'Blocked task',
      prompt: 'Needs a decision.',
      status: 'blocked',
      blockedReason: 'Needs a decision.',
      createdAt: '2026-05-23T00:01:00Z'
    },
    {
      taskId: 'task-review',
      missionId: 'mission-1',
      assigneeMemberId: 'reviewer',
      title: 'Review task',
      prompt: 'Review the output.',
      status: 'review',
      kind: 'review',
      createdAt: '2026-05-23T00:02:00Z'
    }
  ]
}

let currentTeamView = teamView

function renderTeamsView() {
  return render(<TeamsView host={host} contributionId="teams" />)
}

describe('TeamsView mission-thread model', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    notificationHandlers.clear()
    currentTeamView = teamView
    hostLocale = 'en'
    Object.defineProperty(window, 'ResizeObserver', {
      configurable: true,
      value: class {
        constructor(callback: ResizeObserverCallback) {
          resizeObserverCallback = callback
        }

        observe(): void {}
        disconnect(): void {}
      }
    })
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: () => ({ matches: false })
    })
    confirmDialog.mockResolvedValue(true)
    HTMLElement.prototype.getBoundingClientRect = function getBoundingClientRect() {
      const element = this as HTMLElement
      if (element.classList.contains('teams-card-board-viewport')) return boardViewportRect
      if (element.classList.contains('teams-card-board-stage-shell')) return makeRect(0, 0, 1360, 720)
      if (element.classList.contains('teams-discard-pile')) return makeRect(1190, 198, 160, 150)
      if (element.classList.contains('teams-mission-create-zone')) {
        return makeRect(
          numericStyle(element.style.left, 470),
          numericStyle(element.style.top, 250),
          numericStyle(element.style.width, 420),
          numericStyle(element.style.minHeight, 220)
        )
      }
      if (element.dataset.cardKey === 'draft') return makeRect(83, 28, 128, 168)
      if (element.dataset.cardKey === 'mission:mission-1') return makeRect(528, 100, 128, 168)
      if (element.dataset.cardKey?.startsWith('member:')) {
        return makeRect(Number.parseFloat(element.style.left || '0'), Number.parseFloat(element.style.top || '0'), 128, 168)
      }
      return makeRect(0, 0, 128, 168)
    }
    HTMLElement.prototype.animate = function animate(frames: Keyframe[] | PropertyIndexedKeyframes | null) {
      animateMock({ key: (this as HTMLElement).dataset.cardKey, frames })
      return {
      cancel: vi.fn(),
      finished: animationFinishedPromise
      } as unknown as Animation
    }
    sendRequest.mockImplementation((method: string, params: unknown) => {
      if (method === 'teams/team/view') return Promise.resolve(currentTeamView)
      if (method === 'teams/mission/cancel') return Promise.resolve(currentTeamView)
      if (method === 'teams/mission/archive') return Promise.resolve(currentTeamView)
      if (method === 'teams/member/openThread') {
        if ((params as { taskId?: string }).taskId === 'task-1') return Promise.resolve({ threadId: 'thread-builder' })
        if ((params as { missionId?: string; memberId?: string }).missionId === 'mission-1') return Promise.resolve({ threadId: 'thread-leader' })
      }
      return Promise.resolve(teamView)
    })
    boardViewportRect = makeRect(0, 0, 1376, 736)
    animationFinishedPromise = Promise.resolve()
    resizeObserverCallback = undefined
  })

  async function submitMissionDraft(title: string): Promise<{ draft: HTMLElement; createdCard: HTMLElement }> {
    const draft = await screen.findByRole('button', { name: 'New Mission' })
    fireEvent.pointerDown(draft, { button: 0, clientX: 100, clientY: 80, pointerId: 1 })
    await screen.findByTestId('teams-mission-create-zone')
    fireEvent.pointerMove(draft, { clientX: 640, clientY: 340, pointerId: 1 })
    fireEvent.pointerUp(draft, { clientX: 640, clientY: 340, pointerId: 1 })

    fireEvent.change(await screen.findByLabelText('Mission title'), {
      target: { value: title }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Create Mission' }))

    return {
      draft,
      createdCard: await screen.findByRole('button', { name: title })
    }
  }

  it('uses the current Host locale after a rerender', async () => {
    const rendered = renderTeamsView()
    expect(await screen.findByRole('button', { name: 'New Mission' })).toBeInTheDocument()

    hostLocale = 'zh-Hans'
    rendered.rerender(<TeamsView host={host} contributionId="teams" />)

    expect(await screen.findByRole('button', { name: '新任务' })).toBeInTheDocument()
  })

  it('opens task threads with taskId instead of memberId fallback', async () => {
    renderTeamsView()

    fireEvent.keyDown(await screen.findByRole('button', { name: 'Build card board' }), { key: 'Enter' })
    fireEvent.click(await screen.findByRole('button', { name: 'Open assignee thread' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('teams/member/openThread', { taskId: 'task-1' })
      expect(openThread).toHaveBeenCalledWith('thread-builder')
    })
  })

  it('shows task detail content in the paper rail', async () => {
    renderTeamsView()

    fireEvent.keyDown(await screen.findByRole('button', { name: 'Build card board' }), { key: 'Enter' })

    const rail = screen.getByLabelText('Selected card details')
    expect(within(rail).getByText('Implement the card board.')).toBeInTheDocument()
  })

  it('opens a stacked leader card with missionId and memberId', async () => {
    renderTeamsView()

    fireEvent.keyDown(await screen.findByRole('button', { name: 'Team Leader' }), { key: 'Enter' })
    fireEvent.click(await screen.findByRole('button', { name: 'Open member thread' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('teams/member/openThread', {
        missionId: 'mission-1',
        memberId: 'leader'
      })
    })
  })

  it('does not show an open-thread action for idle roster cards', async () => {
    renderTeamsView()

    fireEvent.keyDown(await screen.findByRole('button', { name: 'Reviewer' }), { key: 'Enter' })

    expect(screen.queryByRole('button', { name: 'Open member thread' })).not.toBeInTheDocument()
  })

  it('opens a completed teammate task thread from the hand card', async () => {
    currentTeamView = completedTeamView
    renderTeamsView()

    fireEvent.keyDown(await screen.findByRole('button', { name: 'Builder' }), { key: 'Enter' })
    fireEvent.click(await screen.findByRole('button', { name: 'Open member thread' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('teams/member/openThread', { taskId: 'task-1' })
    })
  })

  it('opens a completed leader mission thread from the hand card', async () => {
    currentTeamView = completedTeamView
    renderTeamsView()

    fireEvent.keyDown(await screen.findByRole('button', { name: 'Team Leader' }), { key: 'Enter' })
    fireEvent.click(await screen.findByRole('button', { name: 'Open member thread' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('teams/member/openThread', {
        missionId: 'mission-1',
        memberId: 'leader'
      })
    })
  })

  it('does not render the old Card and Stack Context rail sections', async () => {
    renderTeamsView()

    await screen.findByText('Status')

    expect(screen.queryByText('Stack Context')).not.toBeInTheDocument()
    expect(screen.queryByText('Write the next objective in the paper rail. Creating it turns this draft into a Mission card and hands it to the Leader.')).not.toBeInTheDocument()
  })

  it('shows the center draft drop zone instead of a rail form for Mission Draft', async () => {
    renderTeamsView()

    const draft = await screen.findByRole('button', { name: 'New Mission' })
    expect(screen.queryByTestId('teams-mission-create-zone')).toBeNull()

    fireEvent.keyDown(draft, { key: 'Enter' })

    expect(screen.queryByTestId('teams-mission-create-zone')).toBeNull()
    const rail = screen.getByLabelText('Selected card details')
    expect(within(rail).queryByLabelText('Mission title')).not.toBeInTheDocument()
    expect(within(rail).queryByText('Draft Template')).not.toBeInTheDocument()
    expect(within(rail).queryByRole('button', { name: 'Drag into + zone' })).not.toBeInTheDocument()

    fireEvent.mouseEnter(draft)
    expect(await screen.findByTestId('teams-mission-create-zone')).toBeInTheDocument()

    fireEvent.mouseLeave(draft)
    await waitFor(() => {
      expect(screen.queryByTestId('teams-mission-create-zone')).toBeNull()
    })
  })

  it('nudges the draft drop zone toward the visual board center when the viewport is taller', async () => {
    renderTeamsView()

    const draft = await screen.findByRole('button', { name: 'New Mission' })
    boardViewportRect = makeRect(0, 0, 1600, 980)
    await act(async () => {
      resizeObserverCallback?.([], {} as ResizeObserver)
    })

    fireEvent.mouseEnter(draft)
    const zone = await screen.findByTestId('teams-mission-create-zone')

    await waitFor(() => {
      expect(Number.parseFloat(zone.style.left)).toBeGreaterThan(540)
      expect(Number.parseFloat(zone.style.top)).toBeGreaterThan(350)
      expect(Number.parseFloat(zone.style.top)).toBeLessThan(390)
    })
  })

  it('opens the mission setup overlay when dropping the draft into the center zone and returns it on cancel', async () => {
    renderTeamsView()

    const draft = await screen.findByRole('button', { name: 'New Mission' })
    fireEvent.pointerDown(draft, { button: 0, clientX: 100, clientY: 80, pointerId: 1 })
    await screen.findByTestId('teams-mission-create-zone')

    fireEvent.pointerMove(draft, { clientX: 640, clientY: 340, pointerId: 1 })
    fireEvent.pointerUp(draft, { clientX: 640, clientY: 340, pointerId: 1 })

    expect(await screen.findByRole('dialog', { name: 'Create Mission' })).toBeInTheDocument()
    expect(Number.parseFloat(draft.style.left)).toBeGreaterThan(600)
    expect(Number.parseFloat(draft.style.top)).toBeGreaterThan(270)

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Create Mission' })).toBeNull()
      expect(draft).toHaveStyle({ left: '83px', top: '28px' })
    })
  })

  it('creates a mission from the draft overlay and hides the drop zone', async () => {
    const createdMission = {
      missionId: 'mission-created',
      title: 'Customer Launch',
      prompt: 'Customer Launch',
      status: 'planning',
      leaderThreadId: 'thread-created',
      createdAt: '2026-05-24T00:00:00Z',
      updatedAt: '2026-05-24T00:00:00Z'
    }
    const createdTeamView = {
      ...emptyTeamView,
      missions: [createdMission]
    }
    currentTeamView = emptyTeamView
    sendRequest.mockImplementation((method: string, params: unknown) => {
      if (method === 'teams/team/view') return Promise.resolve(currentTeamView)
      if (method === 'teams/mission/create') {
        expect(params).toEqual({
          title: 'Customer Launch',
          prompt: 'Customer Launch'
        })
        return Promise.resolve({ mission: createdMission, team: createdTeamView })
      }
      return Promise.resolve(currentTeamView)
    })
    renderTeamsView()

    const { draft, createdCard } = await submitMissionDraft('Customer Launch')
    expect(createdCard).toHaveClass('selected')
    expect(createdCard).toHaveStyle({ zIndex: '30' })
    expect(screen.queryByRole('dialog', { name: 'Create Mission' })).toBeNull()
    expect(screen.queryByTestId('teams-mission-create-zone')).toBeNull()
    expect(draft).toHaveStyle({ left: '83px', top: '28px' })

    const leader = await screen.findByRole('button', { name: 'Team Leader' })
    await waitFor(() => {
      expect(leader).toHaveClass('working')
    })
    expect(Number.parseInt(leader.style.zIndex, 10)).toBeGreaterThan(Number.parseInt(createdCard.style.zIndex, 10))
  })

  it('suppresses automatic hover elevation after mission creation and allows deliberate hover later', async () => {
    const createdMission = {
      missionId: 'mission-created',
      title: 'Customer Launch',
      prompt: 'Customer Launch',
      status: 'planning',
      leaderThreadId: 'thread-created',
      createdAt: '2026-05-24T00:00:00Z',
      updatedAt: '2026-05-24T00:00:00Z'
    }
    const createdTeamView = {
      ...emptyTeamView,
      missions: [createdMission]
    }
    currentTeamView = emptyTeamView
    sendRequest.mockImplementation((method: string) => {
      if (method === 'teams/team/view') return Promise.resolve(currentTeamView)
      if (method === 'teams/mission/create') return Promise.resolve({ mission: createdMission, team: createdTeamView })
      return Promise.resolve(currentTeamView)
    })
    renderTeamsView()

    const { createdCard } = await submitMissionDraft('Customer Launch')
    const leader = await screen.findByRole('button', { name: 'Team Leader' })
    await waitFor(() => {
      expect(leader).toHaveClass('working')
    })

    fireEvent.mouseEnter(createdCard)
    expect(createdCard).toHaveStyle({ zIndex: '30' })

    fireEvent.pointerMove(createdCard, { clientX: 650, clientY: 300, pointerId: 1 })
    await waitFor(() => {
      expect(createdCard).toHaveStyle({ zIndex: '900' })
    })

    fireEvent.mouseLeave(createdCard)
    expect(createdCard).not.toHaveStyle({ zIndex: '900' })

    fireEvent.mouseEnter(createdCard)
    expect(createdCard).toHaveStyle({ zIndex: '900' })
  })

  it('renders archived missions through the history camera pile', async () => {
    renderTeamsView()

    fireEvent.click(await screen.findByRole('button', { name: /Mission History/ }))

    expect(await screen.findByRole('button', { name: 'Old Mission' })).toBeInTheDocument()
    expect(screen.getByText('Page 1 / 1')).toBeInTheDocument()
  })

  it('shows the final response on done mission cards', async () => {
    currentTeamView = completedTeamView
    renderTeamsView()

    expect(await screen.findByText('Final response for the user.')).toBeInTheDocument()
  })

  it('keeps awaiting leader review missions out of terminal archive mode', async () => {
    currentTeamView = awaitingLeaderReviewTeamView
    renderTeamsView()

    const mission = await screen.findByRole('button', { name: 'Ship Teams' })
    fireEvent.keyDown(mission, { key: 'Enter' })

    expect(screen.getAllByText('Leader review').length).toBeGreaterThan(0)
    expect(screen.getByTestId('teams-discard-pile').className).toContain('cancel-mode')
    expect(screen.getByTestId('teams-discard-pile').className).not.toContain('archive-mode')
  })

  it('renders scheduler task status labels', async () => {
    currentTeamView = taskStatusShowcaseTeamView
    renderTeamsView()

    expect(await screen.findByText('Waiting on deps')).toBeInTheDocument()
    expect(screen.getByText('Blocked')).toBeInTheDocument()
    expect(screen.getByText('Review')).toBeInTheDocument()
  })

  it('temporarily raises a stacked mission on hover and restores the canonical order', async () => {
    renderTeamsView()

    const mission = await screen.findByRole('button', { name: 'Ship Teams' })
    fireEvent.mouseEnter(mission)
    expect(mission).toHaveStyle({ zIndex: '900' })

    fireEvent.mouseLeave(mission)
    expect(mission).not.toHaveStyle({ zIndex: '900' })
  })

  it('returns teammate cards to the hand when their mission and task are complete', async () => {
    currentTeamView = completedTeamView
    const { container } = renderTeamsView()

    expect(await screen.findByRole('button', { name: 'Team Leader' })).toHaveStyle({ left: '376px', top: '538px' })
    expect(await screen.findByRole('button', { name: 'Builder' })).toHaveStyle({ left: '536px', top: '562px' })
    expect(container.querySelector('.teams-card-task .teams-card-progress')).toBeNull()
  })

  it('walks the leader from home before entering planning work on a new mission', async () => {
    currentTeamView = emptyTeamView
    renderTeamsView()

    expect(await screen.findByRole('button', { name: 'Team Leader' })).toHaveStyle({ left: '376px', top: '538px' })

    currentTeamView = teamView
    await act(async () => {
      notify('teams/team/changed')
    })

    const leader = await screen.findByRole('button', { name: 'Team Leader' })
    await waitFor(() => {
      expect(animateMock).toHaveBeenCalled()
      expect(leader).not.toHaveClass('working')
    })
    await waitFor(() => {
      expect(leader).toHaveClass('working')
    })
  })

  it('retargets a traveling leader when its mission card moves', async () => {
    currentTeamView = emptyTeamView
    renderTeamsView()

    expect(await screen.findByRole('button', { name: 'Team Leader' })).toHaveStyle({ left: '376px', top: '538px' })
    animationFinishedPromise = new Promise(() => undefined)
    currentTeamView = teamView
    await act(async () => {
      notify('teams/team/changed')
    })

    const mission = await screen.findByRole('button', { name: 'Ship Teams' })
    const leader = await screen.findByRole('button', { name: 'Team Leader' })
    await waitFor(() => {
      expect(animateMock).toHaveBeenCalled()
      expect(leader).toHaveStyle({ left: '552px', top: '232px' })
    })

    fireEvent.pointerDown(mission, { button: 0, clientX: 560, clientY: 120, pointerId: 1 })
    fireEvent.pointerMove(mission, { clientX: 780, clientY: 380, pointerId: 1 })
    fireEvent.pointerUp(mission, { clientX: 780, clientY: 380, pointerId: 1 })

    await waitFor(() => {
      expect(Number.parseFloat(mission.style.left)).toBeGreaterThan(700)
      expect(Number.parseFloat(leader.style.left)).toBeCloseTo(Number.parseFloat(mission.style.left) + 24, 0)
      expect(Number.parseFloat(leader.style.top)).toBeCloseTo(Number.parseFloat(mission.style.top) + 42, 0)
    })
  })

  it('walks a newly assigned teammate from its home position to the task', async () => {
    currentTeamView = {
      ...emptyTeamView,
      missions: teamView.missions.map((mission) => ({ ...mission, status: 'active' }))
    }
    renderTeamsView()

    expect(await screen.findByRole('button', { name: 'Builder' })).toHaveStyle({ left: '536px', top: '562px' })
    animateMock.mockClear()

    currentTeamView = {
      ...teamView,
      missions: teamView.missions.map((mission) => ({ ...mission, status: 'active' })),
      missionThreads: teamView.missionThreads.filter((thread) => thread.memberId === 'builder'),
      members: teamView.members.map((member) => member.memberId === 'builder'
        ? member
        : { ...member, status: 'idle' })
    }
    await act(async () => {
      notify('teams/team/changed')
    })

    await waitFor(() => {
      expect(animateMock).toHaveBeenCalled()
    })
    const builderCall = animateMock.mock.calls.map((call) => call[0] as { key?: string; frames?: Keyframe[] })
      .find((call) => call.key === 'member:builder')
    expect(builderCall).toBeTruthy()
    const firstFrames = builderCall?.frames as Keyframe[]
    expect(String(firstFrames[0]?.transform)).toContain('translate(')
  })

  it('does not replay existing team messages on initial load', async () => {
    currentTeamView = {
      ...teamView,
      messages: [
        {
          messageId: 'msg-existing',
          missionId: 'mission-1',
          fromMemberId: 'leader',
          toMemberId: 'builder',
          content: 'Existing handoff.',
          requiresAction: true,
          status: 'recorded',
          createdAt: '2026-05-23T00:01:00Z'
        }
      ]
    }
    renderTeamsView()

    await screen.findByRole('button', { name: 'Team Leader' })

    expect(screen.queryByTestId('teams-meeting-marker')).toBeNull()
    expect(screen.queryByText('Existing handoff.')).toBeNull()
  })

  it('plays a two-person exchange for new team messages and then clears the marker', async () => {
    renderTeamsView()
    await screen.findByRole('button', { name: 'Team Leader' })

    currentTeamView = {
      ...teamView,
      messages: [
        {
          messageId: 'msg-new',
          missionId: 'mission-1',
          fromMemberId: 'leader',
          toMemberId: 'builder',
          content: 'Please fold the latest risk notes into the checklist.',
          requiresAction: true,
          status: 'recorded',
          createdAt: '2026-05-23T00:02:00Z'
        }
      ]
    }
    await act(async () => {
      notify('teams/team/changed')
    })

    expect(await screen.findByTestId('teams-meeting-marker')).toBeInTheDocument()
    const leader = screen.getByRole('button', { name: 'Team Leader' })
    const builder = screen.getByRole('button', { name: 'Builder' })
    expect(Math.abs(Number.parseFloat(builder.style.left) - Number.parseFloat(leader.style.left))).toBeGreaterThanOrEqual(160)
    expect(await screen.findByText('Please fold the latest risk notes into the checklist.')).toBeInTheDocument()
    expect(await screen.findByText('Got it.', {}, { timeout: 1600 })).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.queryByTestId('teams-meeting-marker')).toBeNull()
    }, { timeout: 2600 })
    expect(screen.getByRole('button', { name: 'Builder' })).toHaveStyle({ left: '984px', top: '227px' })
  })

  it('coalesces same-pair team messages into one exchange bubble', async () => {
    renderTeamsView()
    await screen.findByRole('button', { name: 'Team Leader' })

    currentTeamView = {
      ...teamView,
      messages: [
        {
          messageId: 'msg-a',
          missionId: 'mission-1',
          fromMemberId: 'leader',
          toMemberId: 'builder',
          content: 'First note.',
          requiresAction: true,
          status: 'recorded',
          createdAt: '2026-05-23T00:02:00Z'
        },
        {
          messageId: 'msg-b',
          missionId: 'mission-1',
          fromMemberId: 'leader',
          toMemberId: 'builder',
          content: 'Second note.',
          requiresAction: true,
          status: 'recorded',
          createdAt: '2026-05-23T00:02:01Z'
        }
      ]
    }
    await act(async () => {
      notify('teams/team/changed')
    })

    expect(await screen.findByTestId('teams-meeting-marker')).toBeInTheDocument()
    expect(await screen.findByText('Second note. +1 more')).toBeInTheDocument()
  })

  it('cancels active mission exchanges and returns teammates to rest when the mission is interrupted', async () => {
    sendRequest.mockImplementation((method: string) => {
      if (method === 'teams/team/view') return Promise.resolve(currentTeamView)
      if (method === 'teams/mission/cancel') return Promise.resolve(cancelledTeamView)
      return Promise.resolve(teamView)
    })
    renderTeamsView()
    await screen.findByRole('button', { name: 'Team Leader' })

    currentTeamView = {
      ...teamView,
      messages: [
        {
          messageId: 'msg-interrupted',
          missionId: 'mission-1',
          fromMemberId: 'leader',
          toMemberId: 'builder',
          content: 'Pause this and regroup.',
          requiresAction: true,
          status: 'recorded',
          createdAt: '2026-05-23T00:02:00Z'
        }
      ]
    }
    await act(async () => {
      notify('teams/team/changed')
    })

    expect(await screen.findByTestId('teams-meeting-marker')).toBeInTheDocument()
    expect(await screen.findByText('Pause this and regroup.')).toBeInTheDocument()

    const mission = screen.getByRole('button', { name: 'Ship Teams' })
    fireEvent.pointerDown(mission, { button: 0, clientX: 560, clientY: 120, pointerId: 1 })
    fireEvent.pointerMove(mission, { clientX: 1240, clientY: 238, pointerId: 1 })
    fireEvent.pointerUp(mission, { clientX: 1240, clientY: 238, pointerId: 1 })

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('teams/mission/cancel', { missionId: 'mission-1' })
      expect(screen.queryByTestId('teams-meeting-marker')).toBeNull()
    })
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Team Leader' })).toHaveStyle({ left: '376px', top: '538px' })
      expect(screen.getByRole('button', { name: 'Builder' })).toHaveStyle({ left: '536px', top: '562px' })
    })
    expect(screen.queryByText('Pause this and regroup.')).toBeNull()
  })

  it('keeps the leader in the exchange while the assignee acknowledges a new task dispatch', async () => {
    currentTeamView = missionOnlyTeamView
    renderTeamsView()
    const leader = await screen.findByRole('button', { name: 'Team Leader' })

    currentTeamView = {
      ...missionOnlyTeamView,
      stats: {
        ...missionOnlyTeamView.stats,
        totalTasks: 1
      },
      members: missionOnlyTeamView.members.map((member) => member.memberId === 'builder'
        ? { ...member, status: 'queued', currentTaskId: 'task-1' }
        : member),
      missionThreads: [
        ...missionOnlyTeamView.missionThreads,
        teamView.missionThreads.find((thread) => thread.memberId === 'builder')!
      ],
      tasks: teamView.tasks
    }
    await act(async () => {
      notify('teams/team/changed')
    })

    expect(await screen.findByTestId('teams-meeting-marker')).toBeInTheDocument()
    expect(await screen.findByText('Implement the card board.')).toBeInTheDocument()
    expect(await screen.findByText('On it.', {}, { timeout: 1600 })).toBeInTheDocument()
    expect(leader).not.toHaveStyle({ left: '376px', top: '538px' })
  })

  it('walks a teammate back to the hand when an active task completes', async () => {
    renderTeamsView()

    expect(await screen.findByRole('button', { name: 'Builder' })).toHaveStyle({ left: '984px', top: '227px' })
    animateMock.mockClear()

    currentTeamView = completedTeamView
    await act(async () => {
      notify('teams/team/changed')
    })

    await waitFor(() => {
      expect(animateMock).toHaveBeenCalled()
    })
    const builderCall = animateMock.mock.calls.map((call) => call[0] as { key?: string; frames?: Keyframe[] })
      .find((call) => call.key === 'member:builder')
    expect(builderCall).toBeTruthy()
    const firstFrames = builderCall?.frames as Keyframe[]
    expect(String(firstFrames[0]?.transform)).toContain('translate(')
    expect(await screen.findByRole('button', { name: 'Builder' })).toHaveStyle({ left: '536px', top: '562px' })
  })

  it('snaps idle teammate cards during viewport resize without starting walk animations', async () => {
    currentTeamView = emptyTeamView
    renderTeamsView()

    const builder = await screen.findByRole('button', { name: 'Builder' })
    expect(builder).toHaveStyle({ left: '536px', top: '562px' })
    animateMock.mockClear()

    boardViewportRect = makeRect(0, 0, 1600, 980)
    await act(async () => {
      resizeObserverCallback?.([], {} as ResizeObserver)
    })

    await waitFor(() => {
      expect(Number.parseFloat(builder.style.top)).toBeGreaterThan(700)
    })
    expect(animateMock).not.toHaveBeenCalled()

    boardViewportRect = makeRect(0, 0, 1376, 736)
    await act(async () => {
      resizeObserverCallback?.([], {} as ResizeObserver)
    })

    await waitFor(() => {
      expect(builder).toHaveStyle({ left: '536px', top: '562px' })
    })
    expect(animateMock).not.toHaveBeenCalled()
  })

  it('keeps manually dragged idle teammate cards at their dropped home', async () => {
    currentTeamView = emptyTeamView
    renderTeamsView()

    const builder = await screen.findByRole('button', { name: 'Builder' })
    expect(builder).toHaveStyle({ left: '536px', top: '562px' })

    fireEvent.pointerDown(builder, { button: 0, clientX: 600, clientY: 600, pointerId: 1 })
    fireEvent.pointerMove(builder, { clientX: 720, clientY: 660, pointerId: 1 })
    fireEvent.pointerUp(builder, { clientX: 720, clientY: 660, pointerId: 1 })

    await waitFor(() => {
      expect(Number.parseFloat(builder.style.left)).toBeGreaterThan(650)
      expect(Number.parseFloat(builder.style.top)).toBeGreaterThan(610)
    })
    expect(animateMock).not.toHaveBeenCalled()
  })

  it('walks a dragged working teammate back to its task target', async () => {
    renderTeamsView()

    const builder = await screen.findByRole('button', { name: 'Builder' })
    expect(builder).toHaveStyle({ left: '984px', top: '227px' })
    animateMock.mockClear()

    fireEvent.pointerDown(builder, { button: 0, clientX: 1000, clientY: 250, pointerId: 1 })
    fireEvent.pointerMove(builder, { clientX: 720, clientY: 660, pointerId: 1 })
    fireEvent.pointerUp(builder, { clientX: 720, clientY: 660, pointerId: 1 })

    await waitFor(() => {
      expect(animateMock).toHaveBeenCalled()
    })
    const builderCall = animateMock.mock.calls.map((call) => call[0] as { key?: string; frames?: Keyframe[] })
      .find((call) => call.key === 'member:builder')
    expect(builderCall).toBeTruthy()
  })

  it('arms the discard pile for active missions without making it a click action', async () => {
    renderTeamsView()

    fireEvent.keyDown(await screen.findByRole('button', { name: 'Ship Teams' }), { key: 'Enter' })
    const discardPile = screen.getByTestId('teams-discard-pile')

    expect(discardPile.className).toContain('armed')
    fireEvent.click(discardPile)

    expect(sendRequest).not.toHaveBeenCalledWith('teams/mission/cancel', expect.anything())
  })

  it('cancels a mission only after dragging it into the discard pile and confirming', async () => {
    renderTeamsView()

    const mission = await screen.findByRole('button', { name: 'Ship Teams' })
    fireEvent.pointerDown(mission, { button: 0, clientX: 560, clientY: 120, pointerId: 1 })
    fireEvent.pointerMove(mission, { clientX: 1240, clientY: 238, pointerId: 1 })
    fireEvent.pointerUp(mission, { clientX: 1240, clientY: 238, pointerId: 1 })

    await waitFor(() => {
      expect(confirmDialog).toHaveBeenCalled()
      expect(sendRequest).toHaveBeenCalledWith('teams/mission/cancel', { missionId: 'mission-1' })
    })
  })

  it('archives a terminal mission by dragging it into the discard pile without cancel confirmation', async () => {
    currentTeamView = completedTeamView
    renderTeamsView()

    const mission = await screen.findByRole('button', { name: 'Ship Teams' })
    fireEvent.keyDown(mission, { key: 'Enter' })
    expect(screen.getByTestId('teams-discard-pile').className).toContain('archive-mode')

    fireEvent.pointerDown(mission, { button: 0, clientX: 560, clientY: 120, pointerId: 1 })
    fireEvent.pointerMove(mission, { clientX: 1240, clientY: 238, pointerId: 1 })
    fireEvent.pointerUp(mission, { clientX: 1240, clientY: 238, pointerId: 1 })

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('teams/mission/archive', { missionId: 'mission-1' })
    })
    expect(confirmDialog).not.toHaveBeenCalled()
  })
})

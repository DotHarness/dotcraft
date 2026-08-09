import { translate, type AppLocale } from '../../../shared/locales'
import {
  HAND_BASE_Y,
  HAND_BOTTOM_PAD,
  HAND_LAYOUTS,
  HAND_ZONE_BOTTOM_PAD,
  HAND_ZONE_HEIGHT,
  MISSION_LAYOUTS,
  ROLE_ACCENTS,
  TASK_LAYOUTS,
  TEAM_AVATAR_URLS,
  TEAM_BOARD_EDGE_PAD,
  TEAM_BOARD_FIT_HEIGHT,
  TEAM_BOARD_HEIGHT,
  TEAM_BOARD_WIDTH,
  TEAM_CARD_MIN_HEIGHT,
  TEAM_CARD_WIDTH
} from './TeamsView.layout'
import type {
  ActorPhase,
  ActorState,
  ActorTarget,
  ActorTargetKind,
  BoardCard,
  BoardModel,
  CardOverride,
  IntentKind,
  Mission,
  MissionThread,
  OpenThreadParams,
  SelectedCard,
  TeamMember,
  TeamTask,
  TeamView
} from './TeamsView.types'

export function intentForActor(
  phase: ActorPhase,
  roleKey: string,
  hasTarget: boolean
): IntentKind | undefined {
  switch (phase) {
    case 'meeting':
      return 'talk'
    case 'traveling':
      return hasTarget ? 'task' : 'rest'
    case 'settling':
      return hasTarget ? 'task' : 'rest'
    case 'working':
      return roleKey === 'leader' ? 'plan' : 'work'
    case 'idle':
      return hasTarget ? undefined : 'rest'
    default:
      return undefined
  }
}

export const EMPTY_TEAM: TeamView = {
  team: {
    teamId: '',
    createdAt: '',
    updatedAt: ''
  },
  stats: {
    runningMembers: 0,
    queuedInputs: 0,
    totalTasks: 0,
    completedTasks: 0,
    inputTokens: 0,
    outputTokens: 0,
    cachedInputTokens: 0,
    totalTokens: 0
  },
  members: [],
  missions: [],
  archivedMissions: [],
  missionThreads: [],
  tasks: [],
  messages: [],
  artifacts: [],
  mailboxDigests: []
}

export function normalizeTeamView(raw: unknown): TeamView {
  const value = (raw ?? {}) as Partial<TeamView>
  return {
    team: value.team ?? EMPTY_TEAM.team,
    stats: {
      ...EMPTY_TEAM.stats,
      ...(value.stats ?? {})
    },
    members: Array.isArray(value.members) ? value.members : [],
    missions: Array.isArray(value.missions) ? value.missions : [],
    archivedMissions: Array.isArray(value.archivedMissions) ? value.archivedMissions : [],
    missionThreads: Array.isArray(value.missionThreads) ? value.missionThreads : [],
    tasks: Array.isArray(value.tasks) ? value.tasks : [],
    messages: Array.isArray(value.messages) ? value.messages : [],
    artifacts: Array.isArray(value.artifacts) ? value.artifacts : [],
    mailboxDigests: Array.isArray(value.mailboxDigests) ? value.mailboxDigests : []
  }
}

export function buildBoardModel({
  locale,
  members,
  missions,
  tasks,
  missionThreads,
  actorStates,
  cardOverrides,
  boardVisibleLogicalHeight
}: {
  locale: AppLocale
  members: TeamMember[]
  missions: Mission[]
  tasks: TeamTask[]
  missionThreads: MissionThread[]
  actorStates: Record<string, ActorState>
  cardOverrides: Record<string, CardOverride>
  boardVisibleLogicalHeight: number
}): BoardModel {
  const missionLayoutById = new Map<string, { x: number; y: number; rotation: number }>()
  const taskLayoutById = new Map<string, { x: number; y: number; rotation: number }>()
  const tasksByMission = new Map<string, TeamTask[]>()
  const taskById = new Map(tasks.map((task, index) => {
    const baseLayout = TASK_LAYOUTS[index] ?? fallbackLayout(index, 430, 260)
    const override = cardOverrides[`task:${task.taskId}`]
    taskLayoutById.set(task.taskId, override ? { x: override.x, y: override.y, rotation: override.rotation } : baseLayout)
    const missionTasks = tasksByMission.get(task.missionId) ?? []
    missionTasks.push(task)
    tasksByMission.set(task.missionId, missionTasks)
    return [task.taskId, task]
  }))
  const missionById = new Map(missions.map((mission) => [mission.missionId, mission]))
  const actorTargets: Record<string, ActorTarget> = {}
  const cards: BoardCard[] = [
    {
      key: 'draft',
      kind: 'draft',
      title: translate(locale, 'teams.newMission'),
      status: translate(locale, 'teams.status.draft'),
      body: translate(locale, 'teams.draftPromptPreview'),
      x: 83,
      y: 28,
      rotation: -3,
      z: 20,
      stripLabel: translate(locale, 'teams.missionDraft'),
      stripMeta: '+',
      accent: '#4f7cf6'
    }
  ]

  for (const [index, mission] of missions.entries()) {
    const baseLayout = MISSION_LAYOUTS[index] ?? fallbackLayout(index, 330, 170)
    const override = cardOverrides[`mission:${mission.missionId}`]
    const layout = override ? { x: override.x, y: override.y, rotation: override.rotation } : baseLayout
    missionLayoutById.set(mission.missionId, layout)
    const missionTasks = tasksByMission.get(mission.missionId) ?? []
    const planning = isPlanningMissionStatus(mission.status)
    const active = isActiveMissionStatus(mission.status)
    const done = isDoneStatus(mission.status)
    const cancelled = isCancelledStatus(mission.status)
    const doneTasks = missionTasks.filter((task) => isDoneStatus(task.status)).length
    const taskProgress = missionTasks.length > 0 ? Math.round((doneTasks / missionTasks.length) * 100) : undefined
    const body = missionBody(mission, locale)
    cards.push({
      key: `mission:${mission.missionId}`,
      kind: 'mission',
      id: mission.missionId,
      missionId: mission.missionId,
      title: mission.title || translate(locale, 'teams.missions'),
      status: workflowStatusLabel(locale, mission.status),
      body,
      x: layout.x,
      y: layout.y,
      rotation: layout.rotation,
      z: 30 + index,
      stripLabel: translate(locale, 'teams.mission'),
      stripMeta: String(index + 1),
      note: planning
        ? translate(locale, 'teams.leaderPlanning')
        : done
          ? body
          : cancelled
            ? translate(locale, 'teams.missionCancelled')
            : translate(locale, 'teams.missionActive'),
      progress: active && typeof taskProgress === 'number' ? taskProgress : undefined,
      statusChip: done
        ? { label: translate(locale, 'teams.status.done'), tone: 'done' }
        : cancelled
          ? { label: translate(locale, 'teams.status.cancelled'), tone: 'cancelled' }
          : planning
            ? { label: translate(locale, 'teams.status.planning'), tone: 'live' }
            : active
              ? { label: workflowStatusLabel(locale, mission.status), tone: 'live' }
              : undefined,
      completed: done,
      spawned: index > 0,
      discardAction: done || cancelled ? 'archive' : 'cancel',
      accent: done ? '#22a45a' : cancelled ? '#64748b' : '#4f7cf6'
    })
  }

  for (const [index, task] of tasks.entries()) {
    const layout = taskLayoutById.get(task.taskId) ?? fallbackLayout(index, 430, 260)
    const completed = isDoneStatus(task.status)
    const missionThread = findMissionThreadForTask(task, missionThreads)
    const running = isActiveTaskStatus(task.status) || isMissionThreadBusy(missionThread)
    // Anchor the flip-out source at the mission stack the task came from (leader's stack).
    // Completed tasks have already landed once; suppress the source so the still-present
    // .teams-card-spawned class doesn't re-fling them on every re-render.
    const missionLayout = missionLayoutById.get(task.missionId)
    const spawnFlip = completed || !missionLayout
      ? undefined
      : {
        fromX: missionLayout.x + 24 - layout.x,
        fromY: missionLayout.y + 42 - layout.y,
        arcX: missionLayout.x < layout.x ? 60 : -60
      }
    cards.push({
      key: `task:${task.taskId}`,
      kind: 'task',
      id: task.taskId,
      taskId: task.taskId,
      missionId: task.missionId,
      title: task.title || translate(locale, 'teams.task'),
      status: threadOrTaskStatusLabel(locale, missionThread, task.status || 'queued'),
      body: task.prompt || task.digest || translate(locale, 'teams.noTasks'),
      x: layout.x,
      y: layout.y,
      rotation: layout.rotation,
      z: 24 + index,
      stripLabel: translate(locale, 'teams.task'),
      stripMeta: String(index + 1),
      note: task.digest || task.status,
      progress: undefined,
      statusChip: completed
        ? { label: translate(locale, 'teams.status.done'), tone: 'done' }
        : running
          ? { label: threadOrTaskStatusLabel(locale, missionThread, task.status || 'queued'), tone: 'live' }
          : { label: threadOrTaskStatusLabel(locale, missionThread, task.status || 'queued'), tone: 'queued' },
      completed,
      spawned: !completed,
      spawnFlip,
      openThreadParams: { taskId: task.taskId },
      accent: completed ? '#22a45a' : '#475569'
    })
  }

  for (const [index, member] of members.entries()) {
    const roleKey = roleKeyForMember(member)
    const { task, missionThread: taskMissionThread } = resolveMemberTask(member, taskById, tasks, missionThreads)
    const conversationOpenThreadParams = resolveMemberOpenThreadParams(member, roleKey, task, tasks, missions, missionThreads)
    const taskLayout = task ? taskLayoutById.get(task.taskId) : undefined
    const leaderMissionThread = roleKey === 'leader'
      ? resolveLeaderMissionThread(missions, missionThreads)
      : undefined
    const leaderMissionCandidate = roleKey === 'leader'
      ? leaderMissionThread
        ? missionById.get(leaderMissionThread.missionId)
        : missions.find((mission) => isPlanningMissionStatus(mission.status))
      : undefined
    const leaderMission = leaderMissionCandidate &&
      !isTerminalMissionStatus(leaderMissionCandidate.status) &&
      (isPlanningMissionStatus(leaderMissionCandidate.status) || isMissionThreadBusy(leaderMissionThread))
      ? leaderMissionCandidate
      : undefined
    const missionLayout = leaderMission
      ? missionLayoutById.get(leaderMission.missionId)
      : undefined
    const handLayout = resolveHandLayout(member, index, boardVisibleLogicalHeight)
    const taskTargetWorking = Boolean(taskLayout && (isMissionThreadBusy(taskMissionThread) || (task && isActiveTaskStatus(task.status))))
    const missionTargetWorking = Boolean(missionLayout && leaderMission && (isPlanningMissionStatus(leaderMission.status) || isMissionThreadBusy(leaderMissionThread)))
    const target: ActorTarget | undefined = task && taskLayout
      ? {
        key: `task:${task.taskId}`,
        kind: 'task',
        id: task.taskId,
        x: taskLayout.x + 24,
        y: taskLayout.y + 42,
        rotation: handLayout.rotation * -1,
        missionId: task.missionId,
        taskId: task.taskId,
        missionThread: taskMissionThread,
        status: threadOrTaskStatusLabel(locale, taskMissionThread, task.status || 'queued'),
        stripMeta: taskMissionThread?.queuedInputCount ? String(taskMissionThread.queuedInputCount) : '●',
        working: taskTargetWorking,
        openThreadParams: { taskId: task.taskId }
      }
      : leaderMission && missionLayout
        ? {
          key: `mission:${leaderMission.missionId}`,
          kind: 'mission',
          id: leaderMission.missionId,
          x: missionLayout.x + 24,
          y: missionLayout.y + 42,
          rotation: 2.4,
          missionId: leaderMission.missionId,
          missionThread: leaderMissionThread,
          status: statusLabel(locale, leaderMissionThread?.status ?? member.status),
          stripMeta: leaderMissionThread?.queuedInputCount ? String(leaderMissionThread.queuedInputCount) : '●',
          working: missionTargetWorking,
          openThreadParams: { missionId: leaderMission.missionId, memberId: member.memberId }
        }
        : undefined
    if (target) {
      actorTargets[member.memberId] = target
    }
    const actor = actorStates[member.memberId] ?? (target
      ? createActorStateAtTarget(member.memberId, handLayout, target)
      : createActorStateAtHome(member.memberId, handLayout))
    const atTarget = Boolean(
      target &&
      actor.targetKey === target.key &&
      actor.phase !== 'traveling' &&
      actor.phase !== 'meeting'
    )
    const actorStatus = actor.phase === 'traveling' || actor.phase === 'meeting'
      ? translate(locale, actor.phase === 'meeting' ? 'teams.status.meeting' : 'teams.status.arriving')
      : atTarget && target
        ? target.status
        : statusLabel(locale, member.status)
    const hasActiveTarget = Boolean(target)
    const isTraveling = actor.phase === 'traveling' || actor.phase === 'meeting'
    cards.push({
      key: `member:${member.memberId}`,
      kind: 'member',
      id: member.memberId,
      memberId: member.memberId,
      taskId: atTarget ? target?.taskId : undefined,
      missionId: atTarget ? target?.missionId : undefined,
      title: member.displayName || member.memberId,
      status: actorStatus,
      body: member.description || translate(locale, 'teams.member'),
      x: actor.x,
      y: actor.y,
      rotation: actor.rotation,
      z: isTraveling ? 70 + index : atTarget ? 45 + index : 36 + index,
      stripLabel: member.displayName || roleKey,
      stripMeta: atTarget && target ? target.stripMeta : '●',
      settling: actor.phase === 'settling',
      working: actor.phase === 'working',
      actorPhase: actor.phase,
      actorTargetKey: actor.targetKey,
      openThreadParams: target?.openThreadParams ?? conversationOpenThreadParams,
      roleKey,
      avatarSrc: TEAM_AVATAR_URLS[roleKey] ?? TEAM_AVATAR_URLS.leader,
      accent: member.avatarAccent || ROLE_ACCENTS[roleKey] || '#4f7cf6',
      intent: actor.exchangeId ? 'talk' : intentForActor(actor.phase, roleKey, hasActiveTarget),
      dialog: actor.phase === 'meeting' ? actor.meetingDialog : undefined
    })
  }

  return { cards, actorTargets }
}

export function resolveSelectedDetail(
  locale: AppLocale,
  selectedCard: SelectedCard,
  teamView: TeamView,
  cards: BoardCard[]
): {
  kindLabel: string
  title: string
  status: string
  accent: string
  avatarSrc?: string
  memberDescription?: string
  detailBody?: string
  actionOpenThread?: OpenThreadParams
  actionMission?: Mission
  canArchiveMission?: boolean
} {
  if (selectedCard.kind === 'archivePile') {
    const archivedCount = teamView.archivedMissions.length
    return {
      kindLabel: translate(locale, 'teams.archivePile'),
      title: translate(locale, 'teams.archivedMissions'),
      status: translate(locale, 'teams.archivedCount', { count: String(archivedCount) }),
      accent: '#64748b'
    }
  }

  if (selectedCard.kind === 'historyMission') {
    const mission = teamView.archivedMissions.find((candidate) => candidate.missionId === selectedCard.id)
    if (mission) {
      const body = missionBody(mission, locale)
      return {
        kindLabel: translate(locale, 'teams.archivedMission'),
        title: mission.title,
        status: workflowStatusLabel(locale, mission.status),
        accent: isCancelledStatus(mission.status) ? '#64748b' : '#22a45a',
        detailBody: body
      }
    }
  }

  if (selectedCard.kind === 'member') {
    const member = teamView.members.find((candidate) => candidate.memberId === selectedCard.id)
    const card = cards.find((candidate) => candidate.key === selectedCardToKey(selectedCard))
    if (member && card) {
      return {
        kindLabel: translate(locale, 'teams.member'),
        title: member.displayName,
        status: card.status || statusLabel(locale, member.status),
        accent: card.accent || member.avatarAccent || '#4f7cf6',
        avatarSrc: card.avatarSrc,
        memberDescription: member.description,
        actionOpenThread: card.openThreadParams
      }
    }
  }

  if (selectedCard.kind === 'mission') {
    const mission = teamView.missions.find((candidate) => candidate.missionId === selectedCard.id)
    const card = cards.find((candidate) => candidate.key === selectedCardToKey(selectedCard))
    if (mission && card) {
      const body = missionBody(mission, locale)
      return {
        kindLabel: translate(locale, 'teams.mission'),
        title: mission.title,
        status: workflowStatusLabel(locale, mission.status),
        accent: card.accent || '#4f7cf6',
        detailBody: body,
        actionMission: mission,
        canArchiveMission: isTerminalMissionStatus(mission.status)
      }
    }
  }

  if (selectedCard.kind === 'task') {
    const task = teamView.tasks.find((candidate) => candidate.taskId === selectedCard.id)
    const card = cards.find((candidate) => candidate.key === selectedCardToKey(selectedCard))
    if (task && card) {
      const assignee = teamView.members.find((member) => member.memberId === task.assigneeMemberId)
      const missionThread = findMissionThreadForTask(task, teamView.missionThreads)
      return {
        kindLabel: translate(locale, 'teams.task'),
        title: task.title,
        status: threadOrTaskStatusLabel(locale, missionThread, task.status),
        accent: card.accent || '#475569',
        detailBody: task.digest || task.prompt || task.blockedReason || card.body,
        actionOpenThread: assignee ? { taskId: task.taskId } : undefined
      }
    }
  }

  return {
    kindLabel: translate(locale, 'teams.missionDraft'),
    title: translate(locale, 'teams.newMission'),
    status: translate(locale, 'teams.status.draft'),
    accent: '#4f7cf6'
  }
}

export function selectedCardToKey(card: SelectedCard): string {
  if (card.kind === 'draft') return 'draft'
  if (card.kind === 'archivePile') return 'archivePile'
  if (card.kind === 'historyMission') return `historyMission:${card.id}`
  return `${card.kind}:${card.id}`
}

export function actorMemberIdFromCardKey(key: string | undefined): string | undefined {
  if (!key?.startsWith('member:')) return undefined
  return key.slice('member:'.length)
}

export function getCanonicalMemberHome(member: TeamMember, index: number): { x: number; y: number; rotation: number } {
  return resolveHandLayout(member, index, TEAM_BOARD_FIT_HEIGHT)
}

export function resolveHandLayout(member: TeamMember, index: number, visibleLogicalHeight: number): { x: number; y: number; rotation: number } {
  const roleKey = roleKeyForMember(member)
  const base = HAND_LAYOUTS[roleKey] ?? fallbackLayout(index, 376 + index * 160, 548)
  const handBaseY = clamp(
    visibleLogicalHeight - TEAM_CARD_MIN_HEIGHT - HAND_BOTTOM_PAD,
    HAND_BASE_Y,
    TEAM_BOARD_HEIGHT - TEAM_CARD_MIN_HEIGHT - TEAM_BOARD_EDGE_PAD
  )
  return {
    ...base,
    y: handBaseY + (base.y - HAND_BASE_Y)
  }
}

export function resolveHandZoneY(visibleLogicalHeight: number): number {
  return clamp(
    visibleLogicalHeight - HAND_ZONE_HEIGHT - HAND_ZONE_BOTTOM_PAD,
    HAND_BASE_Y - 18,
    TEAM_BOARD_HEIGHT - HAND_ZONE_HEIGHT - TEAM_BOARD_EDGE_PAD
  )
}

export function resolveHistoryOffsetY(visibleLogicalHeight: number): number {
  return clamp(
    84 + (visibleLogicalHeight - TEAM_BOARD_FIT_HEIGHT) * 0.32,
    84,
    260
  )
}

export function resolveHistoryReturnY(visibleLogicalHeight: number): number {
  return clamp(
    visibleLogicalHeight - 126,
    580,
    TEAM_BOARD_HEIGHT - 96
  )
}

export function createActorStateAtHome(memberId: string, home: { x?: number; y?: number; rotation?: number; homeX?: number; homeY?: number; homeRotation?: number; travelId?: number }): ActorState {
  const homeX = home.homeX ?? home.x ?? 0
  const homeY = home.homeY ?? home.y ?? 0
  const homeRotation = home.homeRotation ?? home.rotation ?? 0
  return {
    memberId,
    x: homeX,
    y: homeY,
    rotation: homeRotation,
    homeX,
    homeY,
    homeRotation,
    phase: 'idle',
    travelId: home.travelId ?? 0
  }
}

export function createActorStateAtTarget(memberId: string, home: { x?: number; y?: number; rotation?: number; homeX?: number; homeY?: number; homeRotation?: number; travelId?: number }, target: ActorTarget): ActorState {
  const homeX = home.homeX ?? home.x ?? 0
  const homeY = home.homeY ?? home.y ?? 0
  const homeRotation = home.homeRotation ?? home.rotation ?? 0
  return {
    memberId,
    x: target.x,
    y: target.y,
    rotation: target.rotation,
    homeX,
    homeY,
    homeRotation,
    phase: 'working',
    targetKey: target.key,
    targetKind: target.kind,
    targetId: target.id,
    travelId: home.travelId ?? 0
  }
}

export function createActorTravelState(
  existing: ActorState,
  from: { x: number; y: number; rotation: number },
  destination: { x: number; y: number; rotation: number; key?: string; kind?: ActorTargetKind; id?: string }
): ActorState {
  return {
    ...existing,
    x: destination.x,
    y: destination.y,
    rotation: destination.rotation,
    phase: 'traveling',
    targetKey: destination.key,
    targetKind: destination.kind,
    targetId: destination.id,
    travelId: existing.travelId + 1,
    travelFromX: from.x,
    travelFromY: from.y,
    travelFromRotation: from.rotation,
    exchangeId: undefined,
    meetingWith: undefined,
    meetingDialog: undefined
  }
}

export function createActorExchangeTravelState(
  existing: ActorState,
  from: { x: number; y: number; rotation: number },
  destination: { x: number; y: number; rotation: number },
  exchangeId: string,
  meetingWith: string
): ActorState {
  return {
    ...existing,
    x: destination.x,
    y: destination.y,
    rotation: destination.rotation,
    phase: 'traveling',
    travelId: existing.travelId + 1,
    travelFromX: from.x,
    travelFromY: from.y,
    travelFromRotation: from.rotation,
    exchangeId,
    meetingWith,
    meetingDialog: undefined
  }
}

export type ActorExchangeLayout = {
  markerX: number
  markerY: number
  first: { x: number; y: number; rotation: number }
  second: { x: number; y: number; rotation: number }
}

export function resolveExchangeLayout(
  first: { x: number; y: number },
  second: { x: number; y: number },
  visibleLogicalHeight: number
): ActorExchangeLayout {
  const separation = 168
  const firstOnLeft = first.x <= second.x
  const maxY = Math.max(TEAM_BOARD_EDGE_PAD, visibleLogicalHeight - TEAM_CARD_MIN_HEIGHT - TEAM_BOARD_EDGE_PAD)
  const y = clamp((first.y + second.y) / 2, TEAM_BOARD_EDGE_PAD, maxY)
  const midX = clamp(
    (first.x + second.x) / 2,
    TEAM_BOARD_EDGE_PAD + separation / 2,
    TEAM_BOARD_WIDTH - TEAM_CARD_WIDTH - TEAM_BOARD_EDGE_PAD - separation / 2
  )
  const firstX = clamp(
    midX + (firstOnLeft ? -separation / 2 : separation / 2),
    TEAM_BOARD_EDGE_PAD,
    TEAM_BOARD_WIDTH - TEAM_CARD_WIDTH - TEAM_BOARD_EDGE_PAD
  )
  const secondX = clamp(
    midX + (firstOnLeft ? separation / 2 : -separation / 2),
    TEAM_BOARD_EDGE_PAD,
    TEAM_BOARD_WIDTH - TEAM_CARD_WIDTH - TEAM_BOARD_EDGE_PAD
  )
  const markerMaxY = Math.max(TEAM_BOARD_EDGE_PAD + 60, visibleLogicalHeight - TEAM_BOARD_EDGE_PAD - 60)

  return {
    markerX: clamp((firstX + secondX) / 2 + TEAM_CARD_WIDTH / 2, TEAM_BOARD_EDGE_PAD + 60, TEAM_BOARD_WIDTH - TEAM_BOARD_EDGE_PAD - 60),
    markerY: clamp(y + TEAM_CARD_MIN_HEIGHT / 2, TEAM_BOARD_EDGE_PAD + 60, markerMaxY),
    first: {
      x: firstX,
      y,
      rotation: firstOnLeft ? -1.8 : 1.8
    },
    second: {
      x: secondX,
      y,
      rotation: firstOnLeft ? 1.8 : -1.8
    }
  }
}

export function distanceBetween(a: { x: number; y: number }, b: { x: number; y: number }): number {
  return Math.hypot(a.x - b.x, a.y - b.y)
}

export function findStackTopCard(baseCard: BoardCard, cards: BoardCard[]): BoardCard | undefined {
  if (baseCard.kind === 'mission' && baseCard.missionId) {
    return cards.find((card) => card.kind === 'member' && card.missionId === baseCard.missionId && !card.taskId)
  }
  if (baseCard.kind === 'task' && baseCard.taskId) {
    return cards.find((card) => card.kind === 'member' && card.taskId === baseCard.taskId)
  }
  return undefined
}

export function findStackDragPartner(card: BoardCard, cards: BoardCard[]): BoardCard | undefined {
  if (card.kind === 'member') {
    if (card.taskId) {
      return cards.find((candidate) => candidate.kind === 'task' && candidate.taskId === card.taskId)
    }
    if (card.missionId) {
      return cards.find((candidate) => candidate.kind === 'mission' && candidate.missionId === card.missionId)
    }
    return undefined
  }
  return findStackTopCard(card, cards)
}

export function roleKeyForMember(member: TeamMember): string {
  const source = `${member.memberId} ${member.role}`.toLowerCase()
  if (source.includes('leader')) return 'leader'
  if (source.includes('explorer')) return 'explorer'
  if (source.includes('builder')) return 'builder'
  if (source.includes('reviewer')) return 'reviewer'
  if (source.includes('operator')) return 'operator'
  return member.memberId.toLowerCase()
}

export function resolveMemberTask(
  member: TeamMember,
  visibleTaskById: Map<string, TeamTask>,
  allTasks: TeamTask[],
  missionThreads: MissionThread[]
): { task?: TeamTask; missionThread?: MissionThread } {
  if (member.currentTaskId) {
    const visibleTask = visibleTaskById.get(member.currentTaskId)
    if (visibleTask && !isDoneStatus(visibleTask.status) && !isCancelledStatus(visibleTask.status)) {
      return { task: visibleTask, missionThread: findMissionThreadForTask(visibleTask, missionThreads) }
    }
  }
  const memberTasks = allTasks.filter((task) => task.assigneeMemberId === member.memberId && !isDoneStatus(task.status) && !isCancelledStatus(task.status))
  const busyTask = memberTasks.find((task) => isMissionThreadBusy(findMissionThreadForTask(task, missionThreads)))
  const task = busyTask ?? memberTasks[0]
  return { task, missionThread: task ? findMissionThreadForTask(task, missionThreads) : undefined }
}

export function findMissionThreadForTask(task: TeamTask, missionThreads: MissionThread[]): MissionThread | undefined {
  return missionThreads.find((thread) => thread.currentTaskId === task.taskId) ??
    missionThreads.find((thread) => thread.missionId === task.missionId && thread.memberId === task.assigneeMemberId)
}

export function resolveMemberOpenThreadParams(
  member: TeamMember,
  roleKey: string,
  activeTask: TeamTask | undefined,
  visibleTasks: TeamTask[],
  visibleMissions: Mission[],
  missionThreads: MissionThread[]
): OpenThreadParams | undefined {
  if (activeTask && findMissionThreadForTask(activeTask, missionThreads)) {
    return { taskId: activeTask.taskId }
  }

  const completedTask = visibleTasks
    .filter((task) => task.assigneeMemberId === member.memberId && isDoneStatus(task.status) && findMissionThreadForTask(task, missionThreads))
    .sort(compareUpdatedDescending)[0]
  if (completedTask) {
    return { taskId: completedTask.taskId }
  }

  const visibleMissionIds = new Set(visibleMissions.map((mission) => mission.missionId))
  const missionThread = missionThreads
    .filter((thread) =>
      thread.memberId === member.memberId &&
      visibleMissionIds.has(thread.missionId) &&
      Boolean(thread.threadId))
    .sort(compareUpdatedDescending)[0]
  if (!missionThread) return undefined

  return {
    missionId: missionThread.missionId,
    memberId: roleKey === 'leader' ? 'leader' : member.memberId
  }
}

export function resolveLeaderMissionThread(missions: Mission[], missionThreads: MissionThread[]): MissionThread | undefined {
  const missionIds = new Set(missions.map((mission) => mission.missionId))
  const leaderThreads = missionThreads.filter((thread) => thread.memberId === 'leader' && missionIds.has(thread.missionId))
  return leaderThreads.find((thread) => isMissionThreadBusy(thread)) ?? leaderThreads[0]
}

export function compareUpdatedDescending(
  a: { updatedAt?: string; createdAt?: string },
  b: { updatedAt?: string; createdAt?: string }
): number {
  return timestampValue(b.updatedAt ?? b.createdAt) - timestampValue(a.updatedAt ?? a.createdAt)
}

export function timestampValue(value?: string): number {
  if (!value) return 0
  const parsed = Date.parse(value)
  return Number.isFinite(parsed) ? parsed : 0
}

export function statusLabel(locale: AppLocale, status: string): string {
  switch (status) {
    case 'running':
      return translate(locale, 'teams.status.running')
    case 'queued':
      return translate(locale, 'teams.status.queued')
    case 'approval':
      return translate(locale, 'teams.status.approval')
    case 'input':
      return translate(locale, 'teams.status.input')
    case 'pending':
      return translate(locale, 'teams.status.pending')
    case 'waitingdependencies':
      return translate(locale, 'teams.status.waitingDependencies')
    case 'ready':
      return translate(locale, 'teams.status.ready')
    case 'blocked':
      return translate(locale, 'teams.status.blocked')
    case 'review':
      return translate(locale, 'teams.status.review')
    case 'failed':
      return translate(locale, 'teams.status.failed')
    case 'done':
      return translate(locale, 'teams.status.done')
    default:
      return translate(locale, 'teams.status.idle')
  }
}

export function workflowStatusLabel(locale: AppLocale, status: string): string {
  switch (status.toLowerCase()) {
    case 'pending':
      return translate(locale, 'teams.status.pending')
    case 'waitingdependencies':
      return translate(locale, 'teams.status.waitingDependencies')
    case 'ready':
      return translate(locale, 'teams.status.ready')
    case 'blocked':
      return translate(locale, 'teams.status.blocked')
    case 'review':
      return translate(locale, 'teams.status.review')
    case 'failed':
      return translate(locale, 'teams.status.failed')
    case 'planning':
      return translate(locale, 'teams.status.planning')
    case 'active':
      return translate(locale, 'teams.status.active')
    case 'awaitingleaderreview':
      return translate(locale, 'teams.status.awaitingLeaderReview')
    case 'done':
      return translate(locale, 'teams.status.done')
    case 'cancelled':
      return translate(locale, 'teams.status.cancelled')
    default:
      return status || translate(locale, 'teams.status.idle')
  }
}

export function isMissionThreadBusy(thread: MissionThread | undefined): boolean {
  if (!thread) return false
  return Boolean(thread.running || thread.waitingOnApproval || thread.waitingOnInput || (thread.queuedInputCount ?? 0) > 0) ||
    ['running', 'queued', 'approval', 'input'].includes(thread.status.toLowerCase())
}

export function threadOrTaskStatusLabel(locale: AppLocale, thread: MissionThread | undefined, fallbackStatus: string): string {
  if (thread) {
    const threadStatus = statusLabel(locale, thread.status)
    return threadStatus === translate(locale, 'teams.status.idle') ? workflowStatusLabel(locale, fallbackStatus) : threadStatus
  }
  return workflowStatusLabel(locale, fallbackStatus)
}

export function isActiveTaskStatus(status: string): boolean {
  return ['running', 'queued', 'approval', 'input', 'planning', 'working', 'ready'].includes(status.toLowerCase())
}

export function isPlanningMissionStatus(status: string): boolean {
  return status.toLowerCase() === 'planning'
}

export function isActiveMissionStatus(status: string): boolean {
  return ['active', 'awaitingleaderreview'].includes(status.toLowerCase())
}

export function isDoneStatus(status: string): boolean {
  return status.toLowerCase() === 'done'
}

export function isCancelledStatus(status: string): boolean {
  return status.toLowerCase() === 'cancelled'
}

export function isTerminalMissionStatus(status: string): boolean {
  return isDoneStatus(status) || isCancelledStatus(status)
}

export function missionBody(mission: Mission, locale: AppLocale): string {
  if (isDoneStatus(mission.status)) {
    return mission.finalResponse || mission.completionSummary || mission.prompt || mission.plan || translate(locale, 'teams.noMissions')
  }
  return mission.prompt || mission.plan || mission.completionSummary || translate(locale, 'teams.noMissions')
}

export function sortableTime(value: string | undefined): number {
  const time = value ? Date.parse(value) : Number.NaN
  return Number.isFinite(time) ? time : 0
}

export function fallbackLayout(index: number, x: number, y: number): { x: number; y: number; rotation: number } {
  return {
    x: clamp(x + (index % 3) * 104, TEAM_BOARD_EDGE_PAD, TEAM_BOARD_WIDTH - TEAM_CARD_WIDTH - TEAM_BOARD_EDGE_PAD),
    y: clamp(y + Math.floor(index / 3) * 96, TEAM_BOARD_EDGE_PAD, TEAM_BOARD_HEIGHT - TEAM_CARD_MIN_HEIGHT - TEAM_BOARD_EDGE_PAD),
    rotation: index % 2 === 0 ? -2.2 : 2.4
  }
}

export function formatCount(value: number | undefined): string {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(value ?? 0)
}

export function formatRelativeArchiveTime(value: string | undefined | null): string {
  if (!value) return ''
  const time = Date.parse(value)
  if (!Number.isFinite(time)) return ''
  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  }).format(time)
}

export function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value))
}

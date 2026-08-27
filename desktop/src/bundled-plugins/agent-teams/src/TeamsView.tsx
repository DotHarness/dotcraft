import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import type { KeyboardEvent, MutableRefObject, PointerEvent, WheelEvent } from 'react'
import { Archive, ChevronLeft, ChevronRight, ExternalLink, Loader2, Plus, XCircle } from 'lucide-react'
import { Button, Input, Textarea, type DesktopPluginViewProps } from '@dotcraft/plugin'
import { normalizeLocale, translate, type AppLocale } from '../../../shared/locales'
import { ArchivePile, BoardCardView, DiscardPile, HistoryMissionCard, Metric } from './TeamsView.cards'
import {
  HISTORY_PAGE_SIZE,
  MISSION_LAYOUTS,
  TASK_LAYOUTS,
  TEAM_BOARD_EDGE_PAD,
  TEAM_BOARD_FIT_HEIGHT,
  TEAM_BOARD_HEIGHT,
  TEAM_BOARD_MAX_SCALE,
  TEAM_BOARD_MIN_SCALE,
  TEAM_BOARD_WIDTH,
  TEAM_CARD_MIN_HEIGHT,
  TEAM_CARD_WIDTH
} from './TeamsView.layout'
import {
  EMPTY_TEAM,
  actorMemberIdFromCardKey,
  buildBoardModel,
  clamp,
  createActorExchangeTravelState,
  createActorStateAtHome,
  createActorStateAtTarget,
  createActorTravelState,
  distanceBetween,
  findStackDragPartner,
  findStackTopCard,
  formatCount,
  isTerminalMissionStatus,
  normalizeTeamView,
  resolveExchangeLayout,
  resolveHandLayout,
  resolveHandZoneY,
  resolveHistoryReturnY,
  resolveSelectedDetail,
  selectedCardToKey,
  sortableTime
} from './TeamsView.model'
import type {
  ActorState,
  ActorTarget,
  BoardCard,
  CardOverride,
  DragState,
  Mission,
  OpenThreadParams,
  SelectedCard,
  TeamMember,
  TeamMessage,
  TeamTask,
  TeamView
} from './TeamsView.types'
import './TeamsView.css'

const HISTORY_PAGE_COLLECT_MS = 340
const EXCHANGE_SENDER_MS = 900
const EXCHANGE_TOTAL_MS = 1900
const MISSION_CREATE_ZONE_WIDTH = 420
const MISSION_CREATE_ZONE_HEIGHT = 220
const MISSION_CREATE_ZONE_WINDOWED_SHIFT_X = 92
const MISSION_CREATE_ZONE_WINDOWED_SHIFT_Y = -34
const MISSION_CARD_BASE_Z = 30

type TeamExchange = {
  id: string
  missionId: string
  fromMemberId: string
  toMemberId: string
  senderDialog: string
  receiverDialog: string
}

type ActiveTeamExchange = TeamExchange & {
  markerX: number
  markerY: number
  stage: 'traveling' | 'dialog'
}

function missionCardZ(teamView: TeamView, missionId: string): number {
  const index = teamView.missions.findIndex((mission) => mission.missionId === missionId)
  return MISSION_CARD_BASE_Z + Math.max(index, 0)
}

export function TeamsView({ host }: DesktopPluginViewProps): JSX.Element {
  const locale = normalizeLocale(host.environment.locale)
  const [teamView, setTeamView] = useState<TeamView>(EMPTY_TEAM)
  const [selectedCard, setSelectedCard] = useState<SelectedCard>({ kind: 'draft' })
  const [title, setTitle] = useState('')
  const [prompt, setPrompt] = useState('')
  const [loading, setLoading] = useState(false)
  const [creating, setCreating] = useState(false)
  const [missionCreateOpen, setMissionCreateOpen] = useState(false)
  const [draftCreateHover, setDraftCreateHover] = useState(false)
  const [teamViewLoaded, setTeamViewLoaded] = useState(false)
  const [archivingMissionId, setArchivingMissionId] = useState<string | null>(null)
  const [cancellingMissionId, setCancellingMissionId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [cardOverrides, setCardOverrides] = useState<Record<string, CardOverride>>({})
  const [actorStates, setActorStates] = useState<Record<string, ActorState>>({})
  const [exchangeQueue, setExchangeQueue] = useState<TeamExchange[]>([])
  const [activeExchange, setActiveExchange] = useState<ActiveTeamExchange | null>(null)
  const [draggingKey, setDraggingKey] = useState<string | null>(null)
  const [hoveredCardKey, setHoveredCardKey] = useState<string | null>(null)
  const [discardHoverKey, setDiscardHoverKey] = useState<string | null>(null)
  const [boardScale, setBoardScale] = useState(1)
  const [boardVisibleLogicalHeight, setBoardVisibleLogicalHeight] = useState(TEAM_BOARD_FIT_HEIGHT)
  const [viewMode, setViewMode] = useState<'active' | 'history'>('active')
  const [historyPage, setHistoryPage] = useState(0)
  const [historyLeaving, setHistoryLeaving] = useState(false)
  const [historyDealNonce, setHistoryDealNonce] = useState(0)
  const boardViewportRef = useRef<HTMLDivElement | null>(null)
  const boardShellRef = useRef<HTMLDivElement | null>(null)
  const boardStageRef = useRef<HTMLDivElement | null>(null)
  const discardPileRef = useRef<HTMLDivElement | null>(null)
  const missionCreateZoneRef = useRef<HTMLDivElement | null>(null)
  const cardRefs = useRef(new Map<string, HTMLDivElement>())
  const activeWalkAnimationsRef = useRef(new Map<string, Animation>())
  const activeWalkTravelIdsRef = useRef(new Map<string, number>())
  const settleTimersRef = useRef(new Map<string, number>())
  const exchangeTimersRef = useRef<number[]>([])
  const historyTimersRef = useRef<number[]>([])
  const cameraTimerRef = useRef<number | null>(null)
  const cameraTransitioningRef = useRef(false)
  const actorTargetSignatureRef = useRef<string | null>(null)
  const actorLayoutHeightRef = useRef<number | null>(null)
  const dragStateRef = useRef<DragState | null>(null)
  const topLayerRef = useRef(500)
  const skipNextFlipRef = useRef(false)
  const lastBoardScaleRef = useRef(1)
  const cardOverridesRef = useRef<Record<string, CardOverride>>({})
  const actorStatesRef = useRef<Record<string, ActorState>>({})
  const actorTargetsRef = useRef<Record<string, ActorTarget>>({})
  const teamMembersRef = useRef<TeamMember[]>([])
  const suppressedHoverCardRef = useRef<{ key: string; ignored: boolean } | null>(null)
  const boardVisibleLogicalHeightRef = useRef(TEAM_BOARD_FIT_HEIGHT)
  const seenMessageIdsRef = useRef(new Set<string>())
  const messageBaselineReadyRef = useRef(false)
  const seenTaskDispatchIdsRef = useRef(new Set<string>())
  const seenTaskDispatchMissionIdsRef = useRef(new Set<string>())
  const taskDispatchBaselineReadyRef = useRef(false)
  const exchangeMemberLockCountsRef = useRef(new Map<string, number>())

  function clearExchangeTimers(): void {
    for (const timer of exchangeTimersRef.current) {
      window.clearTimeout(timer)
    }
    exchangeTimersRef.current = []
  }

  function lockExchangeMembers(exchange: TeamExchange): void {
    for (const memberId of [exchange.fromMemberId, exchange.toMemberId]) {
      exchangeMemberLockCountsRef.current.set(memberId, (exchangeMemberLockCountsRef.current.get(memberId) ?? 0) + 1)
    }
  }

  function releaseExchangeMembers(exchange: TeamExchange): void {
    for (const memberId of [exchange.fromMemberId, exchange.toMemberId]) {
      const nextCount = (exchangeMemberLockCountsRef.current.get(memberId) ?? 0) - 1
      if (nextCount > 0) {
        exchangeMemberLockCountsRef.current.set(memberId, nextCount)
      } else {
        exchangeMemberLockCountsRef.current.delete(memberId)
      }
    }
  }

  function isMemberLockedForExchange(memberId: string): boolean {
    return (exchangeMemberLockCountsRef.current.get(memberId) ?? 0) > 0
  }

  function enqueueExchanges(exchanges: TeamExchange[]): void {
    if (exchanges.length === 0) return
    for (const exchange of exchanges) {
      lockExchangeMembers(exchange)
    }
    setExchangeQueue((current) => [...current, ...exchanges])
  }

  function sendExchangeMembersHome(exchange: TeamExchange): void {
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const memberIds = [exchange.fromMemberId, exchange.toMemberId]
    for (const memberId of memberIds) {
      cancelActorMotion(memberId)
    }
    setActorStates((current) => {
      let changed = false
      const next: Record<string, ActorState> = { ...current }
      for (const memberId of memberIds) {
        const home = homeDestinationForMember(memberId)
        if (!home) continue
        const latest = current[memberId]
        if (!latest) {
          next[memberId] = createActorStateAtHome(memberId, home)
          changed = true
          continue
        }
        const from = materializeActorPosition(memberId, latest)
        const source = {
          ...latest,
          homeX: home.x,
          homeY: home.y,
          homeRotation: home.rotation,
          exchangeId: undefined,
          meetingWith: undefined,
          meetingDialog: undefined
        }
        next[memberId] = reduceMotion
          ? createActorStateAtHome(memberId, home)
          : createActorTravelState(source, from, home)
        changed = true
      }
      return changed ? next : current
    })
  }

  function abortMissionExchanges(missionId: string): void {
    if (activeExchange?.missionId === missionId) {
      clearExchangeTimers()
      releaseExchangeMembers(activeExchange)
      sendExchangeMembersHome(activeExchange)
      setActiveExchange(null)
    }
    setExchangeQueue((current) => {
      const kept: TeamExchange[] = []
      let changed = false
      for (const exchange of current) {
        if (exchange.missionId === missionId) {
          releaseExchangeMembers(exchange)
          changed = true
        } else {
          kept.push(exchange)
        }
      }
      return changed ? kept : current
    })
  }

  async function refresh(): Promise<void> {
    setLoading(true)
    setError(null)
    try {
      const result = await host.appServer.request('teams/team/view', {})
      setTeamView(normalizeTeamView(result))
      setTeamViewLoaded(true)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }

  async function createMission(): Promise<void> {
    const missionTitle = title.trim()
    const missionPrompt = prompt.trim() || missionTitle
    if (!missionTitle) return
    setCreating(true)
    setError(null)
    try {
      const result = await host.appServer.request('teams/mission/create', {
        title: missionTitle,
        prompt: missionPrompt
      })
      const next = normalizeTeamView((result as { team?: unknown })?.team ?? result)
      const createdMission = (result as { mission?: Mission })?.mission
      const createdMissionKey = createdMission?.missionId ? `mission:${createdMission.missionId}` : undefined
      const createdOverride = createdMission?.missionId
        ? missionCreateCardOverride(missionCardZ(next, createdMission.missionId))
        : undefined
      setTeamView(next)
      setTitle('')
      setPrompt('')
      setMissionCreateOpen(false)
      setDraftCreateHover(false)
      setCardOverrides((current) => {
        const nextOverrides = { ...current }
        delete nextOverrides.draft
        if (createdMissionKey && createdOverride) {
          nextOverrides[createdMissionKey] = createdOverride
        }
        return nextOverrides
      })
      if (createdMission?.missionId && createdMissionKey) {
        suppressedHoverCardRef.current = { key: createdMissionKey, ignored: false }
        setSelectedCard({ kind: 'mission', id: createdMission.missionId })
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setCreating(false)
    }
  }

  async function archiveMission(mission: Mission): Promise<void> {
    if (!mission.missionId) return
    setArchivingMissionId(mission.missionId)
    setError(null)
    try {
      const result = await host.appServer.request('teams/mission/archive', {
        missionId: mission.missionId
      })
      setTeamView(normalizeTeamView(result))
      setSelectedCard({ kind: 'archivePile' })
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setArchivingMissionId(null)
    }
  }

  async function cancelMissionFromDiscard(card: BoardCard, dragState: DragState): Promise<void> {
    if (!card.missionId) return
    setDiscardHoverKey(null)
    const confirmed = await host.ui.confirm({
      title: translate(locale, 'teams.stopMissionTitle'),
      message: translate(locale, 'teams.stopMissionDescription', { title: card.title }),
      confirmLabel: translate(locale, 'teams.stopMission'),
      cancelLabel: translate(locale, 'teams.keepMission'),
      danger: true
    })
    if (!confirmed) {
      restoreDragState(dragState)
      return
    }

    abortMissionExchanges(card.missionId)
    setCancellingMissionId(card.missionId)
    setError(null)
    try {
      const result = await host.appServer.request('teams/mission/cancel', {
        missionId: card.missionId
      })
      setTeamView(normalizeTeamView(result))
      setSelectedCard({ kind: 'mission', id: card.missionId })
      removeCardOverrides([card.key, dragState.stackTopKey].filter(Boolean) as string[])
    } catch (err) {
      restoreDragState(dragState)
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setCancellingMissionId(null)
    }
  }

  async function archiveMissionFromDiscard(card: BoardCard, dragState: DragState): Promise<void> {
    if (!card.missionId) return
    setDiscardHoverKey(null)
    setArchivingMissionId(card.missionId)
    setError(null)
    try {
      const result = await host.appServer.request('teams/mission/archive', {
        missionId: card.missionId
      })
      setTeamView(normalizeTeamView(result))
      setSelectedCard({ kind: 'archivePile' })
      removeCardOverrides([card.key, dragState.stackTopKey].filter(Boolean) as string[])
    } catch (err) {
      restoreDragState(dragState)
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setArchivingMissionId(null)
    }
  }

  async function openThread(params: OpenThreadParams): Promise<void> {
    if (!params.taskId && (!params.missionId || !params.memberId)) return
    setError(null)
    try {
      const result = await host.appServer.request('teams/member/openThread', params)
      const threadId = result.threadId
      if (!threadId) return
      await host.navigation.openThread(threadId)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  useEffect(() => {
    void refresh()
    const refreshTeam = () => void refresh()
    const subscriptions = [
      host.appServer.onNotification('teams/team/changed', refreshTeam),
      host.appServer.onNotification('thread/queue/updated', refreshTeam),
      host.appServer.onNotification('thread/runtimeChanged', refreshTeam),
      host.appServer.onNotification('turn/started', refreshTeam),
      host.appServer.onNotification('turn/completed', refreshTeam),
      host.appServer.onNotification('turn/cancelled', refreshTeam),
      host.appServer.onNotification('turn/failed', refreshTeam)
    ]
    return () => subscriptions.forEach((dispose) => dispose())
  }, [])

  useEffect(() => {
    return () => {
      for (const animation of activeWalkAnimationsRef.current.values()) {
        animation.cancel()
      }
      for (const timer of settleTimersRef.current.values()) {
        window.clearTimeout(timer)
      }
      clearExchangeTimers()
      for (const timer of historyTimersRef.current) {
        window.clearTimeout(timer)
      }
      if (cameraTimerRef.current) {
        window.clearTimeout(cameraTimerRef.current)
      }
    }
  }, [])

  useEffect(() => {
    cardOverridesRef.current = cardOverrides
  }, [cardOverrides])

  useEffect(() => {
    if (selectedCard.kind === 'member' && !teamView.members.some((member) => member.memberId === selectedCard.id)) {
      setSelectedCard({ kind: 'draft' })
    } else if (selectedCard.kind === 'mission' && !teamView.missions.some((mission) => mission.missionId === selectedCard.id)) {
      setSelectedCard({ kind: 'draft' })
    } else if (selectedCard.kind === 'task' && !teamView.tasks.some((task) => task.taskId === selectedCard.id)) {
      setSelectedCard({ kind: 'draft' })
    } else if (selectedCard.kind === 'historyMission' && !teamView.archivedMissions.some((mission) => mission.missionId === selectedCard.id)) {
      setSelectedCard({ kind: 'archivePile' })
    }
  }, [teamView.archivedMissions, teamView.members, teamView.missions, teamView.tasks, selectedCard])

  useEffect(() => {
    if (selectedCard.kind === 'draft' || missionCreateOpen) return
    setDraftCreateHover(false)
  }, [missionCreateOpen, selectedCard.kind])

  useLayoutEffect(() => {
    const viewport = boardViewportRef.current
    if (!viewport) return

    const updateScale = (): void => {
      const rect = viewport.getBoundingClientRect()
      if (rect.width <= 0 || rect.height <= 0) return
      const availableWidth = Math.max(1, rect.width - 16)
      const availableHeight = Math.max(1, rect.height - 16)
      const fitScale = Math.min(availableWidth / TEAM_BOARD_WIDTH, availableHeight / TEAM_BOARD_FIT_HEIGHT)
      const nextScale = fitScale < TEAM_BOARD_MIN_SCALE
        ? Math.max(0.52, fitScale)
        : clamp(fitScale, TEAM_BOARD_MIN_SCALE, TEAM_BOARD_MAX_SCALE)
      setBoardScale((current) => Math.abs(current - nextScale) > 0.01 ? nextScale : current)
      setBoardVisibleLogicalHeight((current) => {
        const nextHeight = clamp(availableHeight / Math.max(nextScale, 0.01), TEAM_BOARD_FIT_HEIGHT, TEAM_BOARD_HEIGHT)
        return Math.abs(current - nextHeight) > 1 ? nextHeight : current
      })
    }

    updateScale()
    const observer = new ResizeObserver(updateScale)
    observer.observe(viewport)
    return () => observer.disconnect()
  }, [])

  const visibleMissions = useMemo(
    () => [...teamView.missions]
      .sort((a, b) => sortableTime(b.createdAt || b.updatedAt) - sortableTime(a.createdAt || a.updatedAt))
      .slice(0, MISSION_LAYOUTS.length),
    [teamView.missions]
  )

  const visibleMissionIds = useMemo(
    () => new Set(visibleMissions.map((mission) => mission.missionId)),
    [visibleMissions]
  )

  const visibleTasks = useMemo(
    () => [...teamView.tasks]
      .filter((task) => visibleMissionIds.size === 0 || visibleMissionIds.has(task.missionId))
      .sort((a, b) => sortableTime(a.createdAt) - sortableTime(b.createdAt))
      .slice(0, TASK_LAYOUTS.length),
    [teamView.tasks, visibleMissionIds]
  )

  const visibleArchivedMissions = useMemo(
    () => [...teamView.archivedMissions]
      .sort((a, b) => sortableTime(b.archivedAt || b.updatedAt || b.createdAt) - sortableTime(a.archivedAt || a.updatedAt || a.createdAt)),
    [teamView.archivedMissions]
  )

  const historyPageCount = Math.max(1, Math.ceil(visibleArchivedMissions.length / HISTORY_PAGE_SIZE))
  const safeHistoryPage = clamp(historyPage, 0, historyPageCount - 1)
  const historyPageMissions = visibleArchivedMissions.slice(
    safeHistoryPage * HISTORY_PAGE_SIZE,
    safeHistoryPage * HISTORY_PAGE_SIZE + HISTORY_PAGE_SIZE
  )

  const boardModel = useMemo(
    () => buildBoardModel({
      locale,
      members: teamView.members,
      missions: visibleMissions,
      tasks: visibleTasks,
      missionThreads: teamView.missionThreads,
      actorStates,
      cardOverrides,
      boardVisibleLogicalHeight
    }),
    [actorStates, boardVisibleLogicalHeight, cardOverrides, locale, teamView.members, teamView.missionThreads, visibleMissions, visibleTasks]
  )

  const boardCards = boardModel.cards
  const actorTargets = boardModel.actorTargets
  actorStatesRef.current = actorStates
  actorTargetsRef.current = actorTargets
  teamMembersRef.current = teamView.members
  boardVisibleLogicalHeightRef.current = boardVisibleLogicalHeight
  const actorTargetSignature = useMemo(
    () => createActorTargetSignature(actorTargets),
    [actorTargets]
  )

  const stackBaseKeys = useMemo(() => {
    const keys = new Set<string>()
    for (const card of boardCards) {
      if (findStackTopCard(card, boardCards)) {
        keys.add(card.key)
      }
    }
    return keys
  }, [boardCards])

  const selectedDetail = useMemo(
    () => resolveSelectedDetail(locale, selectedCard, teamView, boardCards),
    [boardCards, locale, teamView, selectedCard]
  )

  function getActorSnapshot(memberId: string): ActorState | undefined {
    const existing = actorStatesRef.current[memberId]
    if (existing) return existing
    const index = teamMembersRef.current.findIndex((member) => member.memberId === memberId)
    const member = teamMembersRef.current[index]
    if (!member) return undefined
    return createActorStateAtHome(memberId, resolveHandLayout(member, index, boardVisibleLogicalHeightRef.current))
  }

  function homeDestinationForMember(memberId: string): { x: number; y: number; rotation: number } | undefined {
    const index = teamMembersRef.current.findIndex((member) => member.memberId === memberId)
    const member = teamMembersRef.current[index]
    if (!member) return undefined
    return resolveHandLayout(member, index, boardVisibleLogicalHeightRef.current)
  }

  function startExchange(exchange: TeamExchange): void {
    const fromActor = getActorSnapshot(exchange.fromMemberId)
    const toActor = getActorSnapshot(exchange.toMemberId)
    if (!fromActor || !toActor) {
      releaseExchangeMembers(exchange)
      return
    }

    const fromPosition = materializeActorPosition(exchange.fromMemberId, fromActor)
    const toPosition = materializeActorPosition(exchange.toMemberId, toActor)
    const layout = resolveExchangeLayout(fromPosition, toPosition, boardVisibleLogicalHeightRef.current)
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const active: ActiveTeamExchange = {
      ...exchange,
      markerX: layout.markerX,
      markerY: layout.markerY,
      stage: 'traveling'
    }

    clearExchangeTimers()
    setActiveExchange(active)
    setActorStates((current) => {
      const latestFrom = current[exchange.fromMemberId] ?? fromActor
      const latestTo = current[exchange.toMemberId] ?? toActor
      return {
        ...current,
        [exchange.fromMemberId]: reduceMotion
          ? {
            ...latestFrom,
            x: layout.first.x,
            y: layout.first.y,
            rotation: layout.first.rotation,
            phase: 'meeting',
            exchangeId: exchange.id,
            meetingWith: exchange.toMemberId,
            meetingDialog: undefined,
            travelFromX: undefined,
            travelFromY: undefined,
            travelFromRotation: undefined
          }
          : createActorExchangeTravelState(
            latestFrom,
            materializeActorPosition(exchange.fromMemberId, latestFrom),
            layout.first,
            exchange.id,
            exchange.toMemberId
          ),
        [exchange.toMemberId]: reduceMotion
          ? {
            ...latestTo,
            x: layout.second.x,
            y: layout.second.y,
            rotation: layout.second.rotation,
            phase: 'meeting',
            exchangeId: exchange.id,
            meetingWith: exchange.fromMemberId,
            meetingDialog: undefined,
            travelFromX: undefined,
            travelFromY: undefined,
            travelFromRotation: undefined
          }
          : createActorExchangeTravelState(
            latestTo,
            materializeActorPosition(exchange.toMemberId, latestTo),
            layout.second,
            exchange.id,
            exchange.fromMemberId
          )
      }
    })
  }

  function finishExchange(exchange: TeamExchange): void {
    clearExchangeTimers()
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const fromState = actorStatesRef.current[exchange.fromMemberId]
    const toState = actorStatesRef.current[exchange.toMemberId]
    const fromPosition = fromState ? materializeActorPosition(exchange.fromMemberId, fromState) : undefined
    const toPosition = toState ? materializeActorPosition(exchange.toMemberId, toState) : undefined

    setActorStates((current) => {
      const next: Record<string, ActorState> = { ...current }
      for (const memberId of [exchange.fromMemberId, exchange.toMemberId]) {
        const latest = current[memberId]
        if (!latest || latest.exchangeId !== exchange.id) continue
        const target = actorTargetsRef.current[memberId]
        const home = homeDestinationForMember(memberId)
        if (!home) continue
        const from = memberId === exchange.fromMemberId
          ? fromPosition ?? latest
          : toPosition ?? latest
        const source = {
          ...latest,
          homeX: home.x,
          homeY: home.y,
          homeRotation: home.rotation,
          exchangeId: undefined,
          meetingWith: undefined,
          meetingDialog: undefined
        }
        next[memberId] = target
          ? reduceMotion
            ? createActorStateAtTarget(memberId, source, target)
            : createActorTravelState(source, from, target)
          : reduceMotion
            ? createActorStateAtHome(memberId, home)
            : createActorTravelState(source, from, home)
      }
      return next
    })
    releaseExchangeMembers(exchange)
    setActiveExchange((current) => current?.id === exchange.id ? null : current)
  }

  useEffect(() => {
    if (historyPage !== safeHistoryPage) {
      setHistoryPage(safeHistoryPage)
    }
  }, [historyPage, safeHistoryPage])

  useEffect(() => {
    if (!teamViewLoaded) return
    const memberIds = new Set(teamView.members.map((member) => member.memberId))
    const visibleMissionIdSet = new Set(visibleMissions.map((mission) => mission.missionId))
    const activeVisibleMissionIdSet = new Set(visibleMissions
      .filter((mission) => !isTerminalMissionStatus(mission.status))
      .map((mission) => mission.missionId))
    const taskDispatches = visibleTasks.filter((task) =>
      activeVisibleMissionIdSet.has(task.missionId) &&
      memberIds.has(task.assigneeMemberId) &&
      task.assigneeMemberId !== 'leader' &&
      !['done', 'cancelled'].includes(task.status.toLowerCase()))

    if (!taskDispatchBaselineReadyRef.current) {
      for (const task of taskDispatches) {
        seenTaskDispatchIdsRef.current.add(task.taskId)
      }
      for (const missionId of visibleMissionIdSet) {
        seenTaskDispatchMissionIdsRef.current.add(missionId)
      }
      taskDispatchBaselineReadyRef.current = true
      return
    }

    const exchanges: TeamExchange[] = []
    for (const task of taskDispatches) {
      if (seenTaskDispatchIdsRef.current.has(task.taskId)) continue
      seenTaskDispatchIdsRef.current.add(task.taskId)
      if (!seenTaskDispatchMissionIdsRef.current.has(task.missionId)) continue
      exchanges.push({
        id: `task:${task.taskId}`,
        missionId: task.missionId,
        fromMemberId: 'leader',
        toMemberId: task.assigneeMemberId,
        senderDialog: summarizeTaskDispatch(task, locale),
        receiverDialog: translate(locale, 'teams.dialog.taskDispatch')
      })
    }
    for (const missionId of visibleMissionIdSet) {
      seenTaskDispatchMissionIdsRef.current.add(missionId)
    }
    enqueueExchanges(exchanges)
  }, [locale, teamView.members, teamViewLoaded, visibleMissions, visibleTasks])

  useEffect(() => {
    if (!teamViewLoaded) return
    const messages = [...teamView.messages].sort((a, b) => sortableTime(a.createdAt) - sortableTime(b.createdAt))
    const activeMissionIds = new Set(teamView.missions
      .filter((mission) => !isTerminalMissionStatus(mission.status))
      .map((mission) => mission.missionId))
    if (!messageBaselineReadyRef.current) {
      for (const message of messages) {
        seenMessageIdsRef.current.add(message.messageId)
      }
      messageBaselineReadyRef.current = true
      return
    }

    const memberIds = new Set(teamView.members.map((member) => member.memberId))
    const grouped = new Map<string, TeamMessage[]>()
    for (const message of messages) {
      if (seenMessageIdsRef.current.has(message.messageId)) continue
      seenMessageIdsRef.current.add(message.messageId)
      if (
        !activeMissionIds.has(message.missionId) ||
        !memberIds.has(message.fromMemberId) ||
        !memberIds.has(message.toMemberId) ||
        message.fromMemberId === message.toMemberId
      ) {
        continue
      }
      const key = `${message.missionId}:${message.fromMemberId}:${message.toMemberId}`
      const group = grouped.get(key) ?? []
      group.push(message)
      grouped.set(key, group)
    }

    const exchanges = [...grouped.values()].map((group) => {
      const latest = group[group.length - 1]
      return {
        id: `message:${group.map((message) => message.messageId).join(':')}`,
        missionId: latest.missionId,
        fromMemberId: latest.fromMemberId,
        toMemberId: latest.toMemberId,
        senderDialog: summarizeMessageGroup(group, locale),
        receiverDialog: translate(locale, 'teams.dialog.messageAck')
      }
    })
    enqueueExchanges(exchanges)
  }, [locale, teamView.members, teamView.messages, teamView.missions, teamViewLoaded])

  useEffect(() => {
    const activeMissionIds = new Set(teamView.missions
      .filter((mission) => !isTerminalMissionStatus(mission.status))
      .map((mission) => mission.missionId))
    const staleMissionIds = new Set<string>()
    if (activeExchange && !activeMissionIds.has(activeExchange.missionId)) {
      staleMissionIds.add(activeExchange.missionId)
    }
    for (const exchange of exchangeQueue) {
      if (!activeMissionIds.has(exchange.missionId)) {
        staleMissionIds.add(exchange.missionId)
      }
    }
    for (const missionId of staleMissionIds) {
      abortMissionExchanges(missionId)
    }
  }, [activeExchange, exchangeQueue, teamView.missions])

  useEffect(() => {
    if (activeExchange || draggingKey || viewMode !== 'active' || exchangeQueue.length === 0) return
    const [nextExchange, ...remaining] = exchangeQueue
    setExchangeQueue(remaining)
    startExchange(nextExchange)
  }, [activeExchange, draggingKey, exchangeQueue, viewMode])

  useEffect(() => {
    if (draggingKey) return
    const targetSignatureChanged = actorTargetSignatureRef.current !== actorTargetSignature
    actorTargetSignatureRef.current = actorTargetSignature
    const previousLayoutHeight = actorLayoutHeightRef.current
    const layoutHeightChanged = previousLayoutHeight !== null && Math.abs(previousLayoutHeight - boardVisibleLogicalHeight) > 1
    actorLayoutHeightRef.current = boardVisibleLogicalHeight
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    setActorStates((current) => {
      let changed = false
      const next: Record<string, ActorState> = { ...current }
      const memberIds = new Set(teamView.members.map((member) => member.memberId))

      for (const key of Object.keys(next)) {
        if (!memberIds.has(key)) {
          delete next[key]
          changed = true
        }
      }

      for (const [index, member] of teamView.members.entries()) {
        const home = resolveHandLayout(member, index, boardVisibleLogicalHeight)
        const target = actorTargets[member.memberId]
        const existing = next[member.memberId]
        const desiredKey = target?.key
        if (!existing) {
          next[member.memberId] = target
            ? createActorStateAtTarget(member.memberId, home, target)
            : createActorStateAtHome(member.memberId, home)
          changed = true
          continue
        }

        if (isMemberLockedForExchange(member.memberId)) {
          const synced = syncActorHomeMetadata(existing, home)
          if (synced !== existing) {
            next[member.memberId] = synced
            changed = true
          }
          continue
        }

        if (!targetSignatureChanged) {
          if (
            target &&
            existing.targetKey === target.key &&
            existing.phase !== 'traveling' &&
            existing.phase !== 'meeting' &&
            distanceBetween(existing, target) > 1
          ) {
            // Same task, target position shifted slightly (e.g. board resize) — no meet detour.
            const from = materializeActorPosition(member.memberId, existing)
            next[member.memberId] = reduceMotion
              ? createActorStateAtTarget(member.memberId, existing, target)
              : createActorTravelState(existing, from, target)
            changed = true
            continue
          }
          if (!layoutHeightChanged) continue
          const snapped = syncActorHomeForLayout(existing, home)
          if (snapped !== existing) {
            next[member.memberId] = snapped
            changed = true
          }
          continue
        }

        if (target) {
          const sameTarget = existing.targetKey === desiredKey
          const targetMoved = sameTarget &&
            existing.phase !== 'meeting' &&
            (distanceBetween(existing, target) > 1 || Math.abs(existing.rotation - target.rotation) > 0.5)
          if (!sameTarget || targetMoved || existing.phase === 'idle') {
            const from = materializeActorPosition(member.memberId, existing)
            next[member.memberId] = reduceMotion
              ? createActorStateAtTarget(member.memberId, existing, target)
              : createActorTravelState(existing, from, target)
            changed = true
          }
          continue
        }

        if (existing.phase === 'traveling' && !existing.targetKey) {
          continue
        }

        const returningFromTarget = Boolean(existing.targetKey)
        const homeDestination = returningFromTarget
          ? layoutHeightChanged
            ? home
            : { x: existing.homeX, y: existing.homeY, rotation: existing.homeRotation }
          : home
        if (!returningFromTarget && existing.phase === 'idle') {
          if (!layoutHeightChanged) continue
          const snapped = syncActorHomeForLayout(existing, homeDestination)
          if (snapped !== existing) {
            next[member.memberId] = snapped
            changed = true
          }
          continue
        }
        const needsHome = returningFromTarget || existing.phase !== 'idle' || distanceBetween(existing, homeDestination) > 1
        if (needsHome) {
          const from = materializeActorPosition(member.memberId, existing)
          const source = returningFromTarget
            ? {
              ...existing,
              homeX: homeDestination.x,
              homeY: homeDestination.y,
              homeRotation: homeDestination.rotation
            }
            : existing
          next[member.memberId] = reduceMotion
            ? createActorStateAtHome(member.memberId, homeDestination)
            : createActorTravelState(source, from, homeDestination)
          changed = true
        }
      }

      return changed ? next : current
    })
  }, [actorTargetSignature, boardVisibleLogicalHeight, draggingKey, locale, teamView.members])

  useLayoutEffect(() => {
    if (viewMode !== 'active' || cameraTransitioningRef.current) return
    for (const actor of Object.values(actorStates)) {
      const key = `member:${actor.memberId}`
      if (actor.phase !== 'traveling') {
        if (activeWalkTravelIdsRef.current.has(key)) {
          activeWalkAnimationsRef.current.get(key)?.cancel()
          activeWalkAnimationsRef.current.delete(key)
          activeWalkTravelIdsRef.current.delete(key)
        }
        continue
      }
      if (activeWalkTravelIdsRef.current.get(key) === actor.travelId) {
        continue
      }
      const element = cardRefs.current.get(key)
      if (!element) continue
      const fromX = actor.travelFromX ?? actor.x
      const fromY = actor.travelFromY ?? actor.y
      const fromRotation = actor.travelFromRotation ?? actor.rotation
      activeWalkTravelIdsRef.current.set(key, actor.travelId)
      runPivotWalk(
        key,
        element,
        fromX - actor.x,
        fromY - actor.y,
        fromRotation,
        actor.rotation,
        activeWalkAnimationsRef,
        () => {
          setActorStates((current) => {
            const latest = current[actor.memberId]
            if (!latest || latest.travelId !== actor.travelId || latest.phase !== 'traveling') return current
            // Exchange travel pauses at the marker so both speech bubbles can render.
            const nextPhase: ActorState['phase'] = latest.meetingWith
              ? 'meeting'
              : latest.targetKey ? 'settling' : 'idle'
            return {
              ...current,
              [actor.memberId]: {
                ...latest,
                phase: nextPhase,
                travelFromX: undefined,
                travelFromY: undefined,
                travelFromRotation: undefined
              }
            }
          })
        }
      )
    }
  }, [actorStates, viewMode])

  useEffect(() => {
    for (const [memberId, actor] of Object.entries(actorStates)) {
      const existingTimer = settleTimersRef.current.get(memberId)
      if (actor.phase !== 'settling') {
        if (existingTimer) {
          window.clearTimeout(existingTimer)
          settleTimersRef.current.delete(memberId)
        }
        continue
      }
      if (existingTimer) continue
      const timer = window.setTimeout(() => {
        settleTimersRef.current.delete(memberId)
        setActorStates((current) => {
          const latest = current[memberId]
          if (!latest || latest.phase !== 'settling') return current
          return {
            ...current,
            [memberId]: {
              ...latest,
              phase: latest.targetKey ? 'working' : 'idle'
            }
          }
        })
      }, 165)
      settleTimersRef.current.set(memberId, timer)
    }
  }, [actorStates])

  useEffect(() => {
    if (!activeExchange || activeExchange.stage !== 'traveling') return
    const fromActor = actorStates[activeExchange.fromMemberId]
    const toActor = actorStates[activeExchange.toMemberId]
    if (
      fromActor?.exchangeId !== activeExchange.id ||
      toActor?.exchangeId !== activeExchange.id ||
      fromActor.phase !== 'meeting' ||
      toActor.phase !== 'meeting'
    ) {
      return
    }

    setActiveExchange((current) => current?.id === activeExchange.id ? { ...current, stage: 'dialog' } : current)
    setActorStates((current) => {
      const latestFrom = current[activeExchange.fromMemberId]
      const latestTo = current[activeExchange.toMemberId]
      if (!latestFrom || !latestTo) return current
      return {
        ...current,
        [activeExchange.fromMemberId]: {
          ...latestFrom,
          meetingDialog: activeExchange.senderDialog
        },
        [activeExchange.toMemberId]: {
          ...latestTo,
          meetingDialog: undefined
        }
      }
    })

    const receiverTimer = window.setTimeout(() => {
      setActorStates((current) => {
        const latestFrom = current[activeExchange.fromMemberId]
        const latestTo = current[activeExchange.toMemberId]
        if (latestFrom?.exchangeId !== activeExchange.id || latestTo?.exchangeId !== activeExchange.id) return current
        return {
          ...current,
          [activeExchange.fromMemberId]: {
            ...latestFrom,
            meetingDialog: undefined
          },
          [activeExchange.toMemberId]: {
            ...latestTo,
            meetingDialog: activeExchange.receiverDialog
          }
        }
      })
    }, EXCHANGE_SENDER_MS)
    const finishTimer = window.setTimeout(() => {
      finishExchange(activeExchange)
    }, EXCHANGE_TOTAL_MS)
    exchangeTimersRef.current.push(receiverTimer, finishTimer)
  }, [activeExchange, actorStates])

  useEffect(() => {
    if (Math.abs(lastBoardScaleRef.current - boardScale) <= 0.01) return
    lastBoardScaleRef.current = boardScale
    skipNextFlipRef.current = true
  }, [boardScale])

  const selectedKey = selectedCardToKey(selectedCard)
  const draftActionLabel = translate(locale, 'teams.createMission')
  const draftActionDisabled = creating || loading || !title.trim()
  const actionOpenThread = selectedDetail.actionOpenThread
  const actionMission = selectedDetail.actionMission
  const selectedMissionForDiscard = selectedCard.kind === 'mission'
    ? teamView.missions.find((mission) => mission.missionId === selectedCard.id)
    : undefined
  const selectedDiscardAction = selectedMissionForDiscard
    ? isTerminalMissionStatus(selectedMissionForDiscard.status) ? 'archive' : 'cancel'
    : undefined
  const discardArmed = viewMode === 'active' && Boolean(selectedDiscardAction)
  const discardBusy = Boolean(cancellingMissionId || archivingMissionId)
  const draftCreateZoneVisible = viewMode === 'active' && (
    missionCreateOpen ||
    hoveredCardKey === 'draft' ||
    draggingKey === 'draft'
  )
  const missionCreateZoneLayout = useMemo(() => {
    const width = Math.min(MISSION_CREATE_ZONE_WIDTH, TEAM_BOARD_WIDTH - TEAM_BOARD_EDGE_PAD * 2)
    const height = Math.min(
      MISSION_CREATE_ZONE_HEIGHT,
      Math.max(160, boardVisibleLogicalHeight - TEAM_BOARD_EDGE_PAD * 2)
    )
    const windowedProgress = clamp((boardVisibleLogicalHeight - TEAM_BOARD_FIT_HEIGHT) / 320, 0, 1)
    const maxY = Math.max(TEAM_BOARD_EDGE_PAD, boardVisibleLogicalHeight - height - TEAM_BOARD_EDGE_PAD)
    const centerX = (TEAM_BOARD_WIDTH - width) / 2 + MISSION_CREATE_ZONE_WINDOWED_SHIFT_X * windowedProgress
    const centerY = (boardVisibleLogicalHeight - height) / 2 + MISSION_CREATE_ZONE_WINDOWED_SHIFT_Y * windowedProgress
    return {
      x: Math.round(clamp(centerX, TEAM_BOARD_EDGE_PAD, TEAM_BOARD_WIDTH - width - TEAM_BOARD_EDGE_PAD)),
      y: Math.round(clamp(centerY, TEAM_BOARD_EDGE_PAD, maxY)),
      width,
      height
    }
  }, [boardVisibleLogicalHeight])

  function setHistoryOpen(open: boolean): void {
    skipNextFlipRef.current = true
    cameraTransitioningRef.current = true
    if (cameraTimerRef.current) {
      window.clearTimeout(cameraTimerRef.current)
    }
    setViewMode(open ? 'history' : 'active')
    setSelectedCard({ kind: 'archivePile' })
    setDraftCreateHover(false)
    if (open) {
      setHistoryLeaving(false)
      setHistoryDealNonce((current) => current + 1)
    }
    cameraTimerRef.current = window.setTimeout(() => {
      cameraTransitioningRef.current = false
      skipNextFlipRef.current = true
      cameraTimerRef.current = null
    }, 760)
  }

  function turnHistoryPage(direction: number): void {
    if (viewMode !== 'history' || historyLeaving) return
    const nextPage = clamp(safeHistoryPage + direction, 0, historyPageCount - 1)
    if (nextPage === safeHistoryPage) return

    setHistoryLeaving(true)
    const collectTimer = window.setTimeout(() => {
      setHistoryLeaving(false)
      setHistoryPage(nextPage)
      setHistoryDealNonce((current) => current + 1)
      setSelectedCard({ kind: 'archivePile' })
    }, HISTORY_PAGE_COLLECT_MS)
    historyTimersRef.current.push(collectTimer)
  }

  function selectBoardCard(card: BoardCard): void {
    if (card.kind === 'draft') {
      setSelectedCard({ kind: 'draft' })
      setDraftCreateHover(false)
    } else if (card.id) {
      setSelectedCard({ kind: card.kind, id: card.id } as SelectedCard)
      setDraftCreateHover(false)
    }
  }

  function shouldSuppressCardHover(cardKey: string): boolean {
    const suppressed = suppressedHoverCardRef.current
    if (suppressed?.key !== cardKey) return false
    if (suppressed.ignored) {
      suppressedHoverCardRef.current = null
      return false
    }
    suppressedHoverCardRef.current = { key: cardKey, ignored: true }
    return true
  }

  function activateSuppressedCardHover(cardKey: string): void {
    if (suppressedHoverCardRef.current?.key !== cardKey) return
    suppressedHoverCardRef.current = null
    setHoveredCardKey(cardKey)
  }

  function clearSuppressedCardHover(cardKey: string): void {
    if (suppressedHoverCardRef.current?.key === cardKey) {
      suppressedHoverCardRef.current = null
    }
  }

  function removeCardOverrides(keys: string[]): void {
    setCardOverrides((current) => {
      const next = { ...current }
      for (const key of keys) {
        delete next[key]
      }
      return next
    })
  }

  function restoreDragState(dragState: DragState): void {
    setCardOverrides((current) => {
      const next = {
        ...current,
        [dragState.key]: dragState.startOverride
      }
      if (dragState.stackTopKey && dragState.stackTopStartOverride) {
        next[dragState.stackTopKey] = dragState.stackTopStartOverride
      }
      return next
    })
  }

  function pointerIsOverDiscardPile(clientX: number, clientY: number): boolean {
    const rect = discardPileRef.current?.getBoundingClientRect()
    if (!rect) return false
    return clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom
  }

  function pointerIsOverMissionCreateZone(clientX: number, clientY: number): boolean {
    const rect = missionCreateZoneRef.current?.getBoundingClientRect()
    if (!rect || !draftCreateZoneVisible || viewMode !== 'active') return false
    return clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom
  }

  function missionCreateCardOverride(z: number): CardOverride | undefined {
    const zoneRect = missionCreateZoneRef.current?.getBoundingClientRect()
    const shellRect = boardShellRef.current?.getBoundingClientRect()
    if (!zoneRect || !shellRect) return undefined
    const scale = Math.max(boardScale, 0.01)
    return {
      x: (zoneRect.left - shellRect.left) / scale + ((zoneRect.width / scale) - TEAM_CARD_WIDTH) / 2,
      y: (zoneRect.top - shellRect.top) / scale + ((zoneRect.height / scale) - TEAM_CARD_MIN_HEIGHT) / 2,
      rotation: -2.4,
      z
    }
  }

  function draftCreateZoneOverride(element: HTMLElement): CardOverride | undefined {
    const zoneRect = missionCreateZoneRef.current?.getBoundingClientRect()
    const shellRect = boardShellRef.current?.getBoundingClientRect()
    if (!zoneRect || !shellRect) return undefined
    const scale = Math.max(boardScale, 0.01)
    const width = element.offsetWidth || 128
    const height = element.offsetHeight || 168
    return {
      x: (zoneRect.left - shellRect.left) / scale + ((zoneRect.width / scale) - width) / 2,
      y: (zoneRect.top - shellRect.top) / scale + ((zoneRect.height / scale) - height) / 2,
      rotation: -1.2,
      z: topLayerRef.current++
    }
  }

  function openMissionCreateOverlay(element: HTMLElement): void {
    const override = draftCreateZoneOverride(element)
    if (override) {
      setCardOverrides((current) => ({
        ...current,
        draft: override
      }))
    }
    setTitle(translate(locale, 'teams.newMission'))
    setPrompt('')
    setDraftCreateHover(false)
    setMissionCreateOpen(true)
  }

  function cancelMissionCreate(): void {
    setMissionCreateOpen(false)
    setDraftCreateHover(false)
    setTitle('')
    setPrompt('')
    removeCardOverrides(['draft'])
    setSelectedCard({ kind: 'draft' })
  }

  function materializeCardOverride(card: BoardCard, element: HTMLElement, shell: HTMLElement): CardOverride {
    const rect = element.getBoundingClientRect()
    const boardRect = shell.getBoundingClientRect()
    const scale = Math.max(boardScale, 0.01)
    const override = cardOverrides[card.key]
    return {
      x: (rect.left - boardRect.left) / scale,
      y: (rect.top - boardRect.top) / scale,
      rotation: override?.rotation ?? card.rotation,
      z: override?.z ?? card.z
    }
  }

  function materializeActorPosition(memberId: string, fallback: ActorState): { x: number; y: number; rotation: number } {
    const shell = boardShellRef.current
    const element = cardRefs.current.get(`member:${memberId}`)
    if (!shell || !element) {
      return { x: fallback.x, y: fallback.y, rotation: fallback.rotation }
    }
    const rect = element.getBoundingClientRect()
    const boardRect = shell.getBoundingClientRect()
    const scale = Math.max(boardScale, 0.01)
    return {
      x: (rect.left - boardRect.left) / scale,
      y: (rect.top - boardRect.top) / scale,
      rotation: fallback.rotation
    }
  }

  function cancelActorMotion(memberId: string): void {
    const key = `member:${memberId}`
    activeWalkAnimationsRef.current.get(key)?.cancel()
    activeWalkAnimationsRef.current.delete(key)
    activeWalkTravelIdsRef.current.delete(key)
    const settleTimer = settleTimersRef.current.get(memberId)
    if (settleTimer) {
      window.clearTimeout(settleTimer)
      settleTimersRef.current.delete(memberId)
    }
  }

  function materializeActorOverride(card: BoardCard, override: CardOverride): void {
    if (card.kind !== 'member' || !card.memberId) return
    const memberId = card.memberId
    cancelActorMotion(memberId)
    setActorStates((current) => {
      const existing = current[memberId]
      if (!existing) return current
      return {
        ...current,
        [memberId]: {
          ...existing,
          x: override.x,
          y: override.y,
          rotation: override.rotation,
          phase: existing.targetKey ? existing.phase : 'idle',
          travelFromX: undefined,
          travelFromY: undefined,
          travelFromRotation: undefined
        }
      }
    })
  }

  function commitActorOverride(cardKey: string | undefined, override: CardOverride | undefined, options?: { updateHome?: boolean }): void {
    const memberId = actorMemberIdFromCardKey(cardKey)
    if (!memberId || !override) return
    cancelActorMotion(memberId)
    setActorStates((current) => {
      const existing = current[memberId]
      if (!existing) return current
      const updateHome = Boolean(options?.updateHome) && !existing.targetKey
      return {
        ...current,
        [memberId]: {
          ...existing,
          x: override.x,
          y: override.y,
          rotation: override.rotation,
          homeX: updateHome ? override.x : existing.homeX,
          homeY: updateHome ? override.y : existing.homeY,
          homeRotation: updateHome ? override.rotation : existing.homeRotation,
          travelFromX: undefined,
          travelFromY: undefined,
          travelFromRotation: undefined
        }
      }
    })
  }

  function handlePointerDown(event: PointerEvent<HTMLDivElement>, card: BoardCard): void {
    if (event.button !== 0) return
    if (viewMode !== 'active') return
    if (card.kind === 'member' && card.memberId && isMemberLockedForExchange(card.memberId)) return
    clearSuppressedCardHover(card.key)
    const shell = boardShellRef.current
    if (!shell) return
    const rect = event.currentTarget.getBoundingClientRect()
    const scale = Math.max(boardScale, 0.01)
    const startOverride = materializeCardOverride(card, event.currentTarget, shell)
    const stackTop = card.kind === 'member' ? undefined : findStackDragPartner(card, boardCards)
    if (stackTop?.kind === 'member' && stackTop.memberId && isMemberLockedForExchange(stackTop.memberId)) return
    const stackTopElement = stackTop ? cardRefs.current.get(stackTop.key) : undefined
    const stackTopStartOverride = stackTop && stackTopElement
      ? materializeCardOverride(stackTop, stackTopElement, shell)
      : undefined
    materializeActorOverride(card, startOverride)
    if (stackTop && stackTopStartOverride) {
      materializeActorOverride(stackTop, stackTopStartOverride)
    }
    dragStateRef.current = {
      key: card.key,
      grabX: (event.clientX - rect.left) / scale,
      grabY: (event.clientY - rect.top) / scale,
      moved: false,
      startOverride,
      stackTopKey: stackTopStartOverride ? stackTop?.key : undefined,
      stackTopStartOverride,
      lastClientX: event.clientX,
      lastClientY: event.clientY
    }
    skipNextFlipRef.current = true
    setHoveredCardKey(null)
    setDraggingKey(card.key)
    setCardOverrides((current) => {
      const next = {
        ...current,
        [card.key]: startOverride
      }
      if (stackTop?.key && stackTopStartOverride) {
        next[stackTop.key] = stackTopStartOverride
      }
      return next
    })
    event.currentTarget.setPointerCapture?.(event.pointerId)
    selectBoardCard(card)
  }

  function handlePointerMove(event: PointerEvent<HTMLDivElement>, card: BoardCard): void {
    if (!draggingKey) {
      activateSuppressedCardHover(card.key)
    }
    const dragState = dragStateRef.current
    const shell = boardShellRef.current
    if (!dragState || dragState.key !== card.key || !shell) return
    if (!dragState.moved) {
      dragState.dragZ = topLayerRef.current++
      dragState.stackTopDragZ = topLayerRef.current++
    }
    dragState.moved = true
    dragState.lastClientX = event.clientX
    dragState.lastClientY = event.clientY
    const boardRect = shell.getBoundingClientRect()
    const scale = Math.max(boardScale, 0.01)
    const nextX = clamp(
      (event.clientX - boardRect.left) / scale - dragState.grabX,
      TEAM_BOARD_EDGE_PAD,
      TEAM_BOARD_WIDTH - event.currentTarget.offsetWidth - TEAM_BOARD_EDGE_PAD
    )
    const nextY = clamp(
      (event.clientY - boardRect.top) / scale - dragState.grabY,
      TEAM_BOARD_EDGE_PAD,
      TEAM_BOARD_HEIGHT - event.currentTarget.offsetHeight - TEAM_BOARD_EDGE_PAD
    )
    setDiscardHoverKey(card.discardAction && pointerIsOverDiscardPile(event.clientX, event.clientY) ? card.key : null)
    setDraftCreateHover(card.kind === 'draft' && pointerIsOverMissionCreateZone(event.clientX, event.clientY))
    setCardOverrides((current) => {
      const currentOverride = current[card.key] ?? dragState.startOverride
      const next: Record<string, CardOverride> = {
        ...current,
        [card.key]: {
          ...currentOverride,
          x: nextX,
          y: nextY,
          z: dragState.dragZ ?? currentOverride.z
        }
      }
      if (dragState.stackTopKey && dragState.stackTopStartOverride) {
        const deltaX = nextX - dragState.startOverride.x
        const deltaY = nextY - dragState.startOverride.y
        const currentTopOverride = current[dragState.stackTopKey] ?? dragState.stackTopStartOverride
        next[dragState.stackTopKey] = {
          ...currentTopOverride,
          x: dragState.stackTopStartOverride.x + deltaX,
          y: dragState.stackTopStartOverride.y + deltaY,
          z: dragState.stackTopDragZ ?? currentTopOverride.z
        }
      }
      return next
    })
  }

  function handlePointerUp(event: PointerEvent<HTMLDivElement>, card: BoardCard): void {
    const dragState = dragStateRef.current
    if (!dragState || dragState.key !== card.key) return
    event.currentTarget.releasePointerCapture?.(event.pointerId)
    const wasMoved = dragState.moved
    const droppedOnDiscard = wasMoved && Boolean(card.discardAction) && pointerIsOverDiscardPile(dragState.lastClientX, dragState.lastClientY)
    const droppedOnMissionCreate = wasMoved && card.kind === 'draft' && pointerIsOverMissionCreateZone(dragState.lastClientX, dragState.lastClientY)
    dragStateRef.current = null
    skipNextFlipRef.current = true
    setDraggingKey(null)
    setDiscardHoverKey(null)
    setDraftCreateHover(false)
    if (droppedOnMissionCreate) {
      openMissionCreateOverlay(event.currentTarget)
      return
    }
    if (droppedOnDiscard) {
      if (card.discardAction === 'archive') {
        void archiveMissionFromDiscard(card, dragState)
      } else {
        void cancelMissionFromDiscard(card, dragState)
      }
      return
    }
    const finalCardOverride = cardOverridesRef.current[card.key]
    const finalStackTopOverride = dragState.stackTopKey ? cardOverridesRef.current[dragState.stackTopKey] : undefined
    commitActorOverride(card.key, finalCardOverride, {
      updateHome: card.kind === 'member' && !card.actorTargetKey && !card.missionId && !card.taskId
    })
    commitActorOverride(dragState.stackTopKey, finalStackTopOverride, { updateHome: false })
    setCardOverrides((current) => {
      const override = current[card.key]
      if (!override) return current
      const next: Record<string, CardOverride> = {
        ...current,
      }
      if (card.kind === 'member') {
        delete next[card.key]
      } else {
        next[card.key] = {
          ...override,
          rotation: wasMoved ? clamp(override.rotation + randomBetween(-1.8, 1.8), -6, 6) : override.rotation,
          z: card.z
        }
      }
      if (dragState.stackTopKey && dragState.stackTopStartOverride) {
        const stackTop = boardCards.find((candidate) => candidate.key === dragState.stackTopKey)
        const topOverride = current[dragState.stackTopKey]
        if (stackTop && topOverride) {
          if (stackTop.kind === 'member') {
            delete next[dragState.stackTopKey]
          } else {
            next[dragState.stackTopKey] = {
              ...topOverride,
              z: stackTop.z
            }
          }
        }
      }
      return next
    })
    if (!wasMoved) {
      selectBoardCard(card)
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>, card: BoardCard): void {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    selectBoardCard(card)
  }

  function handleBoardWheel(event: WheelEvent<HTMLElement>): void {
    if (viewMode !== 'history') return
    event.preventDefault()
    turnHistoryPage(event.deltaY > 0 || event.deltaX > 0 ? 1 : -1)
  }

  useEffect(() => {
    if (viewMode !== 'history') return
    const handleKey = (event: globalThis.KeyboardEvent): void => {
      if (event.key === 'Escape') {
        setHistoryOpen(false)
      } else if (event.key === 'ArrowRight') {
        turnHistoryPage(1)
      } else if (event.key === 'ArrowLeft') {
        turnHistoryPage(-1)
      }
    }
    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  }, [historyLeaving, historyPageCount, safeHistoryPage, viewMode])

  useEffect(() => {
    if (!missionCreateOpen) return
    const handleKey = (event: globalThis.KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      cancelMissionCreate()
    }
    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  }, [missionCreateOpen])

  return (
    <div className="teams-card-game">
      <section className="teams-card-board" aria-label={translate(locale, 'teams.cardBoard')} onWheel={handleBoardWheel}>
        <div ref={boardViewportRef} className="teams-card-board-viewport">
          <div
            ref={boardShellRef}
            className="teams-card-board-stage-shell"
            style={{
              width: `${TEAM_BOARD_WIDTH * boardScale}px`,
              height: `${TEAM_BOARD_HEIGHT * boardScale}px`
            }}
          >
            <div
              ref={boardStageRef}
              className={`teams-card-board-stage ${viewMode === 'history' ? 'history-mode' : ''}`}
              style={{
                width: `${TEAM_BOARD_WIDTH}px`,
                height: `${TEAM_BOARD_HEIGHT}px`,
                transform: `scale(${boardScale})`
              }}
            >
              <div className="teams-card-stage-world">
                <section className="teams-history-space" aria-hidden={viewMode !== 'history'} aria-label={translate(locale, 'teams.archivedMissions')}>
                  <div className="teams-history-heading">
                    <span className="teams-history-tab">{translate(locale, 'teams.archivedMissions')}</span>
                    <span className="teams-history-page-label">
                      {translate(locale, 'teams.historyPage', { current: String(safeHistoryPage + 1), total: String(historyPageCount) })}
                    </span>
                  </div>
                  <button
                    className="teams-history-edge-deck prev"
                    type="button"
                    onClick={() => turnHistoryPage(-1)}
                    disabled={safeHistoryPage === 0 || historyLeaving}
                    aria-label={translate(locale, 'teams.previousHistoryPage')}
                  >
                    <span className="teams-archive-stack-layer" aria-hidden="true" />
                    <span className="teams-archive-stack-layer" aria-hidden="true" />
                    <span className="teams-archive-stack-top">
                      <span className="teams-archive-stack-strip"><ChevronLeft size={13} strokeWidth={3} aria-hidden /></span>
                      <span className="teams-history-edge-label">{translate(locale, 'teams.previousHistoryPageShort')}</span>
                    </span>
                  </button>
                  <button
                    className="teams-history-edge-deck next"
                    type="button"
                    onClick={() => turnHistoryPage(1)}
                    disabled={safeHistoryPage >= historyPageCount - 1 || historyLeaving}
                    aria-label={translate(locale, 'teams.nextHistoryPage')}
                  >
                    <span className="teams-archive-stack-layer" aria-hidden="true" />
                    <span className="teams-archive-stack-layer" aria-hidden="true" />
                    <span className="teams-archive-stack-top">
                      <span className="teams-archive-stack-strip"><ChevronRight size={13} strokeWidth={3} aria-hidden /></span>
                      <span className="teams-history-edge-label">{translate(locale, 'teams.nextHistoryPageShort')}</span>
                    </span>
                  </button>
                  <button
                    className="teams-history-return-ledge"
                    type="button"
                    style={{ top: `${resolveHistoryReturnY(boardVisibleLogicalHeight)}px` }}
                    onClick={() => setHistoryOpen(false)}
                  >
                    {translate(locale, 'teams.backToBoard')}
                  </button>
                  <div className="teams-history-grid">
                    {historyPageMissions.length > 0 ? historyPageMissions.map((mission, index) => (
                      <HistoryMissionCard
                        key={`${mission.missionId}:${historyDealNonce}`}
                        mission={mission}
                        index={index}
                        boardVisibleLogicalHeight={boardVisibleLogicalHeight}
                        archivedLabel={translate(locale, 'teams.archive')}
                        cancelledLabel={translate(locale, 'teams.status.cancelled')}
                        fallbackTitle={translate(locale, 'teams.archivedMission')}
                        selected={selectedCard.kind === 'historyMission' && selectedCard.id === mission.missionId}
                        leaving={historyLeaving}
                        onSelect={() => setSelectedCard({ kind: 'historyMission', id: mission.missionId })}
                      />
                    )) : (
                      <div className="teams-history-empty">{translate(locale, 'teams.noArchivedMissions')}</div>
                    )}
                  </div>
                </section>

                <section className="teams-active-space" aria-label={translate(locale, 'teams.currentBoard')}>
                  <div
                    className="teams-card-hand-zone"
                    aria-hidden="true"
                    style={{ top: `${resolveHandZoneY(boardVisibleLogicalHeight)}px` }}
                  />
                  {draftCreateZoneVisible && viewMode === 'active' ? (
                    <div
                      ref={missionCreateZoneRef}
                      className={[
                        'teams-mission-create-zone',
                        'show',
                        draftCreateHover ? 'over' : '',
                        missionCreateOpen ? 'occupied' : ''
                      ].filter(Boolean).join(' ')}
                      data-testid="teams-mission-create-zone"
                      aria-hidden="true"
                      style={{
                        left: `${missionCreateZoneLayout.x}px`,
                        top: `${missionCreateZoneLayout.y}px`,
                        width: `${missionCreateZoneLayout.width}px`,
                        minHeight: `${missionCreateZoneLayout.height}px`
                      }}
                    >
                      <div className="teams-mission-create-content">
                        <div className="teams-mission-create-plus" aria-hidden="true">+</div>
                        <div className="teams-mission-create-hint">{translate(locale, 'teams.dragDraftHereToCreate')}</div>
                      </div>
                    </div>
                  ) : null}
                  <ArchivePile
                    count={visibleArchivedMissions.length}
                    archiveLabel={translate(locale, 'teams.archive')}
                    title={translate(locale, 'teams.missionHistory')}
                    meta={translate(locale, 'teams.clickBrowsePages')}
                    selected={selectedCard.kind === 'archivePile'}
                    expanded={viewMode === 'history'}
                    onClick={() => setHistoryOpen(viewMode !== 'history')}
                  />
                  <DiscardPile
                    refCallback={(element) => { discardPileRef.current = element }}
                    armed={discardArmed}
                    over={Boolean(discardHoverKey)}
                    busy={discardBusy}
                    action={selectedDiscardAction}
                    title={translate(locale, selectedDiscardAction === 'archive' ? 'teams.archiveDropPile' : 'teams.discardPile')}
                    meta={translate(locale, selectedDiscardAction === 'archive' ? 'teams.dropMissionToArchive' : 'teams.dropMissionToDiscard')}
                    busyLabel={translate(locale, archivingMissionId ? 'teams.archivingMission' : 'teams.stoppingMission')}
                  />
                  {activeExchange ? (
                    <div
                      className="teams-meeting-marker show"
                      data-testid="teams-meeting-marker"
                      aria-hidden="true"
                      style={{
                        left: `${activeExchange.markerX}px`,
                        top: `${activeExchange.markerY}px`
                      }}
                    />
                  ) : null}
                  {boardCards.map((card) => (
                    <BoardCardView
                      key={card.key}
                      card={card}
                      selected={card.key === selectedKey}
                      dragging={draggingKey === card.key}
                      hovering={hoveredCardKey === card.key}
                      elevated={hoveredCardKey === card.key && stackBaseKeys.has(card.key)}
                      override={cardOverrides[card.key]}
                      refCallback={(element) => {
                        if (element) {
                          cardRefs.current.set(card.key, element)
                        } else {
                          cardRefs.current.delete(card.key)
                        }
                      }}
                      onPointerDown={handlePointerDown}
                      onPointerMove={handlePointerMove}
                      onPointerUp={handlePointerUp}
                      onKeyDown={handleKeyDown}
                      onMouseEnter={(hoveredCard) => {
                        if (!draggingKey && !shouldSuppressCardHover(hoveredCard.key)) {
                          setHoveredCardKey(hoveredCard.key)
                        }
                      }}
                      onMouseLeave={(hoveredCard) => {
                        clearSuppressedCardHover(hoveredCard.key)
                        if (hoveredCardKey === hoveredCard.key) {
                          setHoveredCardKey(null)
                        }
                      }}
                    />
                  ))}
                </section>
              </div>
            </div>
          </div>
        </div>
      </section>

      <aside className="teams-paper-rail dc-scrollbar-stable" aria-label={translate(locale, 'teams.cardDetails')}>
        <div className="teams-rail-heading">
          <div className="teams-rail-identity">
            <div className="teams-rail-tab">{selectedDetail.kindLabel}</div>
            <h1 className="teams-rail-title">{selectedDetail.title}</h1>
            <div className="teams-rail-meta">
              <span className="teams-rail-dot" style={{ background: selectedDetail.accent }} />
              <span>{selectedDetail.status}</span>
            </div>
          </div>
          {selectedDetail.avatarSrc ? (
            <div className="teams-rail-avatar" aria-hidden="true">
              <img src={selectedDetail.avatarSrc} alt="" draggable={false} />
            </div>
          ) : null}
        </div>
        {selectedDetail.memberDescription ? (
          <p className="teams-rail-member-description">{selectedDetail.memberDescription}</p>
        ) : null}
        {selectedDetail.detailBody ? (
          <p className="teams-rail-member-description">{selectedDetail.detailBody}</p>
        ) : null}

        {selectedCard.kind === 'draft' && (
          <section className="teams-rail-section">
            <h2>{translate(locale, 'teams.createMission')}</h2>
            <p>{translate(locale, 'teams.draftDropInstructions')}</p>
          </section>
        )}

        <section className="teams-rail-section">
          <h2>{translate(locale, 'teams.tableStats')}</h2>
          <div className="teams-rail-stats">
            <Metric value={formatCount(teamView.stats.totalTokens)} label={translate(locale, 'teams.stats.tokens')} />
            <Metric value={formatCount(teamView.stats.queuedInputs)} label={translate(locale, 'teams.stats.queued')} />
            <Metric value={`${teamView.stats.completedTasks}/${teamView.stats.totalTasks}`} label={translate(locale, 'teams.stats.tasks')} />
            <Metric value={formatCount(teamView.stats.runningMembers)} label={translate(locale, 'teams.stats.running')} />
          </div>
        </section>

        {actionMission && selectedDetail.canArchiveMission ? (
          <button
            className="teams-rail-action"
            type="button"
            onClick={() => void archiveMission(actionMission)}
            disabled={archivingMissionId === actionMission.missionId}
          >
            {archivingMissionId === actionMission.missionId ? <Loader2 className="teams-card-loading" size={15} strokeWidth={2} aria-hidden /> : <Archive size={15} strokeWidth={2} aria-hidden />}
            <span>{translate(locale, archivingMissionId === actionMission.missionId ? 'teams.archivingMission' : 'teams.archiveMission')}</span>
          </button>
        ) : actionOpenThread ? (
          <button className="teams-rail-action" type="button" onClick={() => void openThread(actionOpenThread)}>
            <ExternalLink size={15} strokeWidth={2} aria-hidden />
            <span>{translate(locale, selectedCard.kind === 'task' ? 'teams.openAssigneeThread' : 'teams.openThread')}</span>
          </button>
        ) : selectedCard.kind === 'archivePile' || selectedCard.kind === 'historyMission' ? (
          <button className="teams-rail-action" type="button" onClick={() => setHistoryOpen(viewMode !== 'history')}>
            <Archive size={15} strokeWidth={2} aria-hidden />
            <span>{translate(locale, viewMode === 'history' ? 'teams.backToBoard' : 'teams.browseHistory')}</span>
          </button>
        ) : null}

        {error && (
          <div className="teams-rail-error">
            <XCircle size={15} strokeWidth={2} aria-hidden />
            <span>{error}</span>
          </div>
        )}
      </aside>
      {missionCreateOpen ? (
        <div
          className="teams-mission-create-overlay"
          data-testid="teams-mission-create-overlay"
          onPointerDown={(event) => {
            if (event.target === event.currentTarget && !creating) {
              cancelMissionCreate()
            }
          }}
        >
          <form
            className="teams-mission-create-card"
            role="dialog"
            aria-modal="true"
            aria-labelledby="teams-mission-create-title"
            onSubmit={(event) => {
              event.preventDefault()
              void createMission()
            }}
          >
            <h2 id="teams-mission-create-title">{translate(locale, 'teams.createMission')}</h2>
            <p>{translate(locale, 'teams.missionCreateOverlayDescription')}</p>
            <div className="teams-rail-field">
              <label htmlFor="teams-mission-create-title-input">{translate(locale, 'teams.missionTitlePlaceholder')}</label>
              <Input
                id="teams-mission-create-title-input"
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                placeholder={translate(locale, 'teams.missionTitlePlaceholder')}
                autoFocus
                required
              />
            </div>
            <div className="teams-rail-field">
              <label htmlFor="teams-mission-create-prompt-input">{translate(locale, 'teams.missionPromptPlaceholder')}</label>
              <Textarea
                id="teams-mission-create-prompt-input"
                value={prompt}
                onChange={(event) => setPrompt(event.target.value)}
                placeholder={translate(locale, 'teams.missionPromptPlaceholder')}
                rows={4}
              />
            </div>
            {error ? (
              <div className="teams-mission-create-error">
                <XCircle size={15} strokeWidth={2} aria-hidden />
                <span>{error}</span>
              </div>
            ) : null}
            <div className="teams-mission-create-actions">
              <Button variant="secondary" onClick={cancelMissionCreate} disabled={creating}>
                {translate(locale, 'common.cancel')}
              </Button>
              <Button type="submit" variant="primary" disabled={draftActionDisabled} loading={creating || loading}>
                {creating || loading ? null : <Plus size={15} strokeWidth={2} aria-hidden />}
                <span>{draftActionLabel}</span>
              </Button>
            </div>
          </form>
        </div>
      ) : null}
    </div>
  )
}

function randomBetween(min: number, max: number): number {
  return min + Math.random() * (max - min)
}

function createActorTargetSignature(actorTargets: Record<string, ActorTarget>): string {
  return Object.entries(actorTargets)
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([memberId, target]) => [
      memberId,
      target.key,
      Math.round(target.x * 100) / 100,
      Math.round(target.y * 100) / 100,
      Math.round(target.rotation * 100) / 100,
      target.working ? 1 : 0
    ].join(':'))
    .join('|')
}

function syncActorHomeForLayout(
  actor: ActorState,
  home: { x: number; y: number; rotation: number }
): ActorState {
  const shouldSnapPosition = actor.phase === 'idle' && !actor.targetKey
  const next = {
    ...actor,
    x: shouldSnapPosition ? home.x : actor.x,
    y: shouldSnapPosition ? home.y : actor.y,
    rotation: shouldSnapPosition ? home.rotation : actor.rotation,
    homeX: home.x,
    homeY: home.y,
    homeRotation: home.rotation
  }

  return next.x === actor.x &&
    next.y === actor.y &&
    next.rotation === actor.rotation &&
    next.homeX === actor.homeX &&
    next.homeY === actor.homeY &&
    next.homeRotation === actor.homeRotation
    ? actor
    : next
}

function syncActorHomeMetadata(
  actor: ActorState,
  home: { x: number; y: number; rotation: number }
): ActorState {
  if (actor.homeX === home.x && actor.homeY === home.y && actor.homeRotation === home.rotation) {
    return actor
  }
  return {
    ...actor,
    homeX: home.x,
    homeY: home.y,
    homeRotation: home.rotation
  }
}

function summarizeTaskDispatch(task: TeamTask, locale: AppLocale): string {
  return truncateDialogText(task.prompt || task.title || translate(locale, 'teams.task'), 82)
}

function summarizeMessageGroup(messages: TeamMessage[], locale: AppLocale): string {
  const latest = messages[messages.length - 1]
  const base = truncateDialogText(latest?.content || translate(locale, 'teams.dialog.teamMessage'), 82)
  if (messages.length <= 1) return base
  return `${base} ${translate(locale, 'teams.dialog.moreMessages', { count: messages.length - 1 })}`
}

function truncateDialogText(value: string, maxLength: number): string {
  const compact = value.replace(/\s+/g, ' ').trim()
  if (compact.length <= maxLength) return compact
  return `${compact.slice(0, Math.max(0, maxLength - 3)).trimEnd()}...`
}

function runPivotWalk(
  key: string,
  element: HTMLElement,
  dx: number,
  dy: number,
  fromRotation: number,
  landingRotation: number,
  activeWalkAnimationsRef: MutableRefObject<Map<string, Animation>>,
  onDone: () => void
): void {
  activeWalkAnimationsRef.current.get(key)?.cancel()
  const distance = Math.hypot(dx, dy)
  const steps = clamp(Math.round(distance / 48), 7, 22)
  const frames: Keyframe[] = []
  for (let i = 0; i <= steps; i += 1) {
    const p = i / steps
    const direction = i % 2 === 0 ? 1 : -1
    const pivotAngle = direction * 8.5
    frames.push({
      offset: p,
      transform: i === steps
        ? `translate(0, 0) rotate(${landingRotation}deg)`
        : `translate(${dx * (1 - p)}px, ${dy * (1 - p)}px) rotate(${fromRotation + pivotAngle}deg)`
    })
  }

  element.classList.add('walking')
  const animation = element.animate(frames, {
    duration: clamp(steps * 347, 3060, 6400),
    easing: 'linear',
    fill: 'forwards'
  })
  activeWalkAnimationsRef.current.set(key, animation)
  void animation.finished.catch(() => undefined).then(() => {
    if (activeWalkAnimationsRef.current.get(key) !== animation) return
    animation.cancel()
    activeWalkAnimationsRef.current.delete(key)
    element.classList.remove('walking')
    onDone()
  })
}

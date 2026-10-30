import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ScrollView, StyleSheet, View, type NativeScrollEvent, type NativeSyntheticEvent } from 'react-native'
import { useComputerLink, useComputerSelector } from '../../app-state/SessionContext'
import type { Decision } from '../../core/decisions'
import { shallowEqual } from '../../core/selection'
import { computerStatus, isReachable, NO_SIGNALS, projectById, type ComputerState } from '../../core/state'
import { createTranscriptProjector } from '../../core/transcript'
import type { ChangesProjector } from '../../core/turnChanges'
import { useI18n } from '../../i18n'
import { useRefreshControl } from '../layout'
import { MascotNote } from '../mascot/Mascot'
import { Notice, PhoneButton, ReadOnlyNotice } from '../parts'
import { projectTitle } from '../rows'
import { RunningShimmer } from '../RunningShimmer'
import { metrics, type, useTheme } from '../theme'
import { TasksPill } from './BackgroundTasks'
import { ChangesOpenerContext } from './Changes'
import { CatchingUpRows } from './CatchingUpRows'
import { BAR_HEIGHT } from './ChatBar'
import { FileViewerContext } from './Chips'
import type { DecisionActions } from './DecisionBodies'
import { DecisionCard } from './DecisionCard'
import { ImageReaderContext, type ImageReader } from './Images'
import { ScrollToBottom } from './ScrollToBottom'
import { LiveSignals, RetryContext, ThreadLinksContext, type ThreadLinks } from './TimelineMarkers'
import { TranscriptLine, useKeptWorkingLine } from './Transcript'

const OLDER_THRESHOLD = 800

export const ChatTranscript = memo(function ChatTranscript({
  chatKey,
  projectId,
  inset,
  docked,
  live,
  ready,
  working,
  decision,
  decisionCount,
  actions,
  runningTasks,
  onTasks,
  openFile,
  imageReader,
  openChanges,
  changesProjector,
  threadLinks,
  retry,
}: {
  chatKey: string
  projectId: string
  inset: number
  docked: boolean
  live: boolean
  ready: boolean
  working: boolean
  decision: Decision | null
  decisionCount: number
  actions: DecisionActions
  runningTasks: number
  onTasks: () => void
  openFile: ((path: string) => void) | null
  imageReader: ImageReader
  openChanges: (turnId: string) => void
  changesProjector: ChangesProjector
  threadLinks: ThreadLinks
  retry: (() => Promise<void>) | null
}) {
  const session = useComputerLink()
  const { t } = useI18n()
  const { colors } = useTheme()
  const selectTranscript = useCallback(
    (state: ComputerState) => {
      const detail = state.details[chatKey]
      return {
        history: detail?.history,
        loading: detail?.loading,
        loadingOlder: detail?.loadingOlder,
        workspacePath: detail?.effectiveWorkspacePath ?? null,
        profile: state.chats[chatKey]?.profileId ? (detail?.profileName ?? state.chats[chatKey].profileId) : null,
        signals: state.signals[chatKey],
        spawnedFrom: state.chats[chatKey]?.spawnedFrom ?? null,
        maintenance: state.chats[chatKey]?.runtime?.maintenanceKind,
        computer: state.computer,
        status: computerStatus(state),
        project: projectById(state, projectId),
        phase: state.phases[projectId],
      }
    },
    [chatKey, projectId],
  )
  const slice = useComputerSelector(selectTranscript, shallowEqual)
  const projector = useMemo(() => createTranscriptProjector(changesProjector), [changesProjector])
  useEffect(() => () => projector.clear(), [projector])
  const transcript = useMemo(
    () =>
      slice.history
        ? projector.project(slice.history, slice.workspacePath, {
            stopped: slice.signals?.stopped,
            spawnedFrom: slice.spawnedFrom,
          })
        : [],
    [slice.history, slice.workspacePath, slice.signals?.stopped, slice.spawnedFrom, projector],
  )
  const lines = useKeptWorkingLine(transcript, chatKey)
  const shownLines = useMemo(
    () => (decision ? lines.map((entry) => (entry.kind === 'thinking' ? { ...entry, working: false } : entry)) : lines),
    [lines, decision],
  )
  const scroller = useRef<ScrollView>(null)
  const viewport = useRef(0)
  const pinned = useRef(true)
  const jumping = useRef(false)
  const frame = useRef<number | null>(null)
  const [away, setAway] = useState(false)
  const refreshControl = useRefreshControl(chatKey, BAR_HEIGHT)
  const projectName = slice.project ? projectTitle(slice.project, t) : ''
  const online = isReachable(slice.status)
  const olderCursor = slice.history?.olderCursor
  const loadOlder = useCallback(() => {
    if (olderCursor && !slice.loadingOlder && live) void session.loadOlder(chatKey).catch(() => undefined)
  }, [olderCursor, slice.loadingOlder, live, session, chatKey])
  const scrollToEnd = useCallback(() => {
    if (frame.current !== null) return
    frame.current = requestAnimationFrame(() => {
      frame.current = null
      if (pinned.current) scroller.current?.scrollToEnd({ animated: false })
    })
  }, [])
  useEffect(
    () => () => {
      if (frame.current !== null) cancelAnimationFrame(frame.current)
    },
    [],
  )
  const track = ({ nativeEvent }: NativeSyntheticEvent<NativeScrollEvent>) => {
    const { contentSize, contentOffset, layoutMeasurement } = nativeEvent
    if (contentOffset.y < OLDER_THRESHOLD) loadOlder()
    const atEnd = contentSize.height - contentOffset.y - layoutMeasurement.height < 48
    if (jumping.current && !atEnd) return
    jumping.current = false
    pinned.current = atEnd
    setAway(!atEnd)
  }
  const signals = slice.signals ?? NO_SIGNALS
  const liveSignals = !signals.compacting && slice.maintenance === 'compacting' ? { ...signals, compacting: 'manual' as const } : signals
  return (
    <>
      <ScrollView
        ref={scroller}
        style={styles.fill}
        contentContainerStyle={[styles.transcript, { paddingBottom: inset + (docked ? 32 : 16) }]}
        keyboardShouldPersistTaps="handled"
        keyboardDismissMode="on-drag"
        showsVerticalScrollIndicator={false}
        scrollEventThrottle={64}
        refreshControl={refreshControl}
        maintainVisibleContentPosition={{ minIndexForVisible: 0 }}
        onScroll={track}
        onScrollBeginDrag={() => {
          jumping.current = false
        }}
        onScrollEndDrag={track}
        onMomentumScrollEnd={track}
        onContentSizeChange={(_width, height) => {
          if (height < viewport.current + OLDER_THRESHOLD) loadOlder()
          scrollToEnd()
        }}
        onLayout={({ nativeEvent }) => {
          viewport.current = nativeEvent.layout.height
          scrollToEnd()
        }}
      >
        {slice.loadingOlder ? (
          <RunningShimmer style={styles.older} textStyle={[type.meta, { color: colors.textDimmed }]}>
            {t('chat.loadingOlder')}
          </RunningShimmer>
        ) : null}
        {!online ? <ReadOnlyNotice status={slice.status} computer={slice.computer.name} /> : null}
        {online && slice.project && !slice.project.running ? (
          <Notice
            icon="info"
            action={
              <PhoneButton
                variant="outline"
                compact
                loading={slice.phase === 'starting'}
                disabled={slice.status !== 'online'}
                onPress={() => void session.startProject(projectId)}
              >
                {t('notice.start')}
              </PhoneButton>
            }
          >
            {slice.phase === 'cantStart'
              ? t('project.cantStart', { computer: slice.computer.name, project: projectName })
              : t('notice.projectStopped', { project: projectName, computer: slice.computer.name })}
          </Notice>
        ) : null}
        {transcript.length === 0 && slice.history && !slice.loading ? (
          <MascotNote moment="greeting" profile={slice.profile}>
            {t('newChat.runsOn', { computer: slice.computer.name, project: projectName })}
          </MascotNote>
        ) : null}
        <FileViewerContext.Provider value={openFile}>
          <ImageReaderContext.Provider value={imageReader}>
            <ChangesOpenerContext.Provider value={openChanges}>
              <ThreadLinksContext.Provider value={threadLinks}>
                <RetryContext.Provider value={retry}>
                  {shownLines.map((entry, index) => (
                    <TranscriptLine key={entry.id} entry={entry} previous={shownLines[index - 1]} workspacePath={slice.workspacePath} />
                  ))}
                </RetryContext.Provider>
              </ThreadLinksContext.Provider>
            </ChangesOpenerContext.Provider>
          </ImageReaderContext.Provider>
        </FileViewerContext.Provider>
        {slice.loading && online && slice.project?.running && shownLines.length > 0 ? <CatchingUpRows /> : null}
        <LiveSignals signals={liveSignals} />
        {decision ? (
          <View style={styles.decision}>
            {runningTasks > 0 ? (
              <View style={styles.decisionPills}>
                <TasksPill count={runningTasks} onPress={onTasks} />
              </View>
            ) : null}
            <DecisionCard key={decision.requestId} decision={decision} count={decisionCount} disabled={!ready} actions={actions} />
          </View>
        ) : null}
      </ScrollView>
      <ScrollToBottom
        visible={away}
        working={working}
        bottom={inset + 16}
        onPress={() => {
          jumping.current = true
          pinned.current = true
          setAway(false)
          scroller.current?.scrollToEnd({ animated: true })
        }}
      />
    </>
  )
})

const styles = StyleSheet.create({
  fill: { flex: 1 },
  transcript: { flexGrow: 1, gap: 14, paddingTop: BAR_HEIGHT + 20, paddingHorizontal: metrics.gutter, paddingBottom: 16 },
  decision: { marginTop: 'auto' },
  older: { alignSelf: 'center' },
  decisionPills: { flexDirection: 'row', justifyContent: 'center', gap: 8, marginBottom: 12 },
})

import { useFocusEffect, useRouter } from 'expo-router'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { KeyboardAvoidingView, ScrollView, StyleSheet, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useComputerLink, useComputerSelector } from '../../app-state/SessionContext'
import { awaitsPlanConfirmation, isLive } from '../../core/chatState'
import { waitingDecisions } from '../../core/decisions'
import { plainMessage } from '../../core/draft'
import { chatTasks } from '../../core/backgroundTasks'
import { chatKey, computerStatus, isReachable, projectById, stateOf, type ComputerState, type PendingRequest } from '../../core/state'
import { shallowEqual } from '../../core/selection'
import { workspaceFile } from '../../core/links'
import { controlsOf, offersPlanMode, type ChatControls } from '../../core/threadConfig'
import { createChangesProjector } from '../../core/turnChanges'
import { useI18n } from '../../i18n'
import { TasksPill, TasksSheet } from '../chat/BackgroundTasks'
import { ChangesPill, ChangesSheet } from '../chat/Changes'
import { BarButton, ChatBar } from '../chat/ChatBar'
import { ChatMenu } from '../chat/ChatMenu'
import { ChatStatus } from '../chat/ChatStatus'
import { ChatTranscript } from '../chat/ChatTranscript'
import { FileViewerContext } from '../chat/Chips'
import { Composer } from '../chat/Composer'
import { Dock, pendingSends, useModels, useReferences } from '../chat/NewChat'
import { catalogItem, ComposerControls, type ControlChange } from '../chat/ComposerControls'
import type { DecisionActions } from '../chat/DecisionBodies'
import { FileSheet } from '../chat/FileSheet'
import { imageScope } from '../../core/imageCache'
import { ImageReaderContext } from '../chat/Images'
import type { ThreadLinks } from '../chat/TimelineMarkers'
import { Screen } from '../layout'
import { MascotTransition } from '../mascot/Mascot'
import { chatTitle, projectIcon, projectTitle } from '../rows'
import { metrics } from '../theme'
import { chatHref } from './HomeScreen'

const NO_PENDING: PendingRequest[] = []

export function ChatScreen({ projectId, threadId }: { projectId: string; threadId: string }) {
  const key = chatKey(projectId, threadId)
  const selectShell = useCallback(
    (computer: ComputerState) => {
      const detail = computer.details[key]
      return {
        chat: computer.chats[key],
        project: projectById(computer, projectId),
        computer: computer.computer,
        status: computerStatus(computer),
        phase: computer.phases[projectId],
        config: detail?.config,
        workspacePath: detail?.effectiveWorkspacePath ?? null,
        catchingUp: !detail || (detail.loading && detail.history.items.length === 0),
        hasHistory: (detail?.history.turns.length ?? 0) > 0,
        lastTurnId: detail?.history.turns.at(-1)?.id,
        pending: computer.pending[key] ?? NO_PENDING,
        taskSources: computer.tasks[key],
      }
    },
    [key, projectId],
  )
  const state = useComputerSelector(selectShell, shallowEqual)
  const session = useComputerLink()
  const router = useRouter()
  const { t } = useI18n()
  const [dismissedPlan, setDismissedPlan] = useState<string | null>(null)
  const [menuOpen, setMenuOpen] = useState(false)
  const [changesTurn, setChangesTurn] = useState<string | null>(null)
  const [openPath, setOpenPath] = useState<string | null>(null)
  const [tasksOpen, setTasksOpen] = useState(false)
  const [dockHeight, setDockHeight] = useState(0)
  const [pendingSend, setPendingSend] = useState(() => pendingSends.get(key))
  const [changesProjector] = useState(createChangesProjector)

  useEffect(() => () => changesProjector.clear(), [changesProjector])
  useEffect(() => {
    pendingSends.delete(key)
  }, [key])
  useEffect(() => {
    session.openChat(key)
    return () => session.closeChat(key)
  }, [key, session])
  useFocusEffect(
    useCallback(() => {
      session.focusChat(key)
      return () => session.blurChat(key)
    }, [key, session]),
  )

  const { chat, project, computer, status, pending, workspacePath } = state
  const online = isReachable(status)
  const live = online && (project?.running ?? false)
  const ready = state.phase === 'ready'
  const chatState = chat ? stateOf(chat) : 'done'
  const working = isLive(chatState)
  const activeTurnId = chat?.runtime?.activeTurnId ?? null
  const tasks = useMemo(() => chatTasks(state.taskSources, activeTurnId), [state.taskSources, activeTurnId])
  const catchingUp = live && state.catchingUp
  const selectApproval = useCallback((computer: ComputerState) => computer.models[projectId]?.approvalDefault ?? 'prompt', [projectId])
  const approvalDefault = useComputerSelector(selectApproval)
  const configured = controlsOf(state.config, approvalDefault)
  const models = useModels(projectId, ready, configured.providerId)
  const controls: ChatControls = { ...configured, providerId: configured.providerId ?? models?.defaultProviderId ?? null }
  const references = useReferences(projectId, ready)
  const planMode = state.config?.mode === 'plan'
  const planTurn = awaitsPlanConfirmation(chat?.runtime ?? null, state.config?.mode) ? (state.lastTurnId ?? key) : null
  const decisions = useMemo(
    () => waitingDecisions(pending, planTurn !== dismissedPlan ? planTurn : null),
    [pending, planTurn, dismissedPlan],
  )
  const decision = live ? (decisions[0] ?? null) : null
  const insets = useSafeAreaInsets()
  const canPlan = offersPlanMode(state.config, chat?.profileId)
  const selectRunningChanges = useCallback(
    (computer: ComputerState) => {
      const detail = computer.details[key]
      const summary = computer.chats[key]
      return detail && summary && isLive(stateOf(summary)) ? changesProjector.running(detail.history, detail.effectiveWorkspacePath) : null
    },
    [key, changesProjector],
  )
  const runningChanges = useComputerSelector(selectRunningChanges)
  const selectChanges = useCallback(
    (computer: ComputerState) => {
      const detail = computer.details[key]
      return detail && changesTurn ? changesProjector.project(detail.history, changesTurn, detail.effectiveWorkspacePath) : null
    },
    [key, changesTurn, changesProjector],
  )
  const changes = useComputerSelector(selectChanges)
  const openChanges = useCallback((turnId: string) => setChangesTurn(turnId), [])
  const readFile = useCallback((path: string) => session.readFile(projectId, path), [projectId, session])
  const reading = ready && models?.fileSystem === true
  const imageReader = useMemo(
    () => ({ scope: imageScope(computer.id, projectId), read: reading ? readFile : null }),
    [computer.id, projectId, readFile, reading],
  )
  const openFile = models?.fileSystem === true ? setOpenPath : null
  const retry = useMemo(
    () => (live && ready && !working && !chat?.runtime?.maintenanceKind ? () => session.retry(key) : null),
    [live, ready, working, chat?.runtime?.maintenanceKind, session, key],
  )
  const threadLinks = useMemo<ThreadLinks>(
    () => ({
      open: (id) => router.push(chatHref(computer.id, chatKey(projectId, id))),
      title: (id) => session.store.getState()?.chats[chatKey(projectId, id)]?.title ?? null,
    }),
    [computer.id, projectId, router, session],
  )
  const canCompact = ready && models?.canCompact === true && state.hasHistory && !working && !chat?.runtime?.maintenanceKind
  const docked = !catchingUp && live && !decision
  const inset = docked ? dockHeight : insets.bottom
  const title = chat ? chatTitle(chat, t('chat.untitled')) : t('chat.untitled')
  const projectName = project ? projectTitle(project, t) : ''
  const onTasks = useCallback(() => setTasksOpen(true), [])
  const actions = useMemo<DecisionActions>(
    () => ({
      decide: (requestId, choice) => session.decide(key, requestId, choice),
      answer: (requestId, answers) => session.answer(key, requestId, answers),
      dismissQuestion: (request) => session.dismissQuestion(key, request),
      implement: () => session.implementPlan(key),
      feedback: (text) => session.send(key, plainMessage(text)),
      dismissPlan: () => setDismissedPlan(planTurn),
    }),
    [session, key, planTurn],
  )
  const change = (next: ControlChange) => {
    if (next.kind === 'provider') return Promise.resolve()
    if (next.kind !== 'model') return session.updateConfig(key, next)
    return session.updateConfig(key, { ...next, catalog: catalogItem(models, next.providerId, next.model) })
  }

  return (
    <Screen>
      <KeyboardAvoidingView style={styles.fill} behavior="padding">
        <View style={styles.fill}>
          {catchingUp ? (
            <MascotTransition line={t('chat.catchingUp')} />
          ) : (
            <ChatTranscript
              key={key}
              chatKey={key}
              projectId={projectId}
              inset={inset}
              docked={docked}
              live={live}
              ready={ready}
              working={working}
              decision={decision}
              decisionCount={decisions.length}
              actions={actions}
              runningTasks={tasks.running.length}
              onTasks={onTasks}
              openFile={openFile}
              imageReader={imageReader}
              openChanges={openChanges}
              changesProjector={changesProjector}
              threadLinks={threadLinks}
              retry={retry}
            />
          )}
          <ChatBar
            title={title}
            project={project ? { title: projectName, icon: projectIcon(project) } : undefined}
            computer={computer.name}
            status={status}
            onBack={() => router.back()}
            trailing={
              <>
                <ChatStatus
                  chatKey={key}
                  threadId={threadId}
                  projectId={projectId}
                  providerId={controls.providerId}
                  folder={workspacePath}
                />
                <BarButton icon="ellipsisVertical" label={t('chat.menu')} onPress={() => setMenuOpen(true)} />
              </>
            }
          />
          {docked ? (
            <Dock
              floating
              above={
                runningChanges || tasks.running.length > 0 ? (
                  <ScrollView
                    horizontal
                    showsHorizontalScrollIndicator={false}
                    style={styles.pillScroll}
                    contentContainerStyle={[styles.pills, styles.pillRow]}
                  >
                    {runningChanges ? <ChangesPill changes={runningChanges} onPress={() => openChanges(runningChanges.turnId)} /> : null}
                    {tasks.running.length > 0 ? <TasksPill count={tasks.running.length} onPress={onTasks} /> : null}
                  </ScrollView>
                ) : null
              }
              onLayout={({ nativeEvent }) => setDockHeight(nativeEvent.layout.height)}
            >
              <Composer
                key={key}
                autoFocus={pendingSend !== undefined}
                pendingSend={pendingSend}
                onPendingSettled={() => setPendingSend(undefined)}
                running={working}
                canSend={ready}
                canAttachFiles={models?.fileSystem === true}
                canPlan={canPlan}
                references={references}
                planMode={planMode}
                onPlanMode={(on) => session.setMode(key, on ? 'plan' : 'agent')}
                onCompact={canCompact ? () => session.compact(key) : undefined}
                controls={<ComposerControls projectId={projectId} controls={controls} models={models} onChange={change} />}
                onSend={(draft) => session.send(key, draft)}
                onStop={() => void session.stop(key).catch(() => undefined)}
              />
            </Dock>
          ) : null}
        </View>
      </KeyboardAvoidingView>
      <ChatMenu
        visible={menuOpen}
        title={title}
        chatId={threadId}
        ready={live && ready}
        canFork={models?.canFork === true}
        onClose={() => setMenuOpen(false)}
        onRename={(name) => void session.rename(key, name).catch(() => undefined)}
        onFork={() =>
          void session.fork(key).then(
            (forked) => router.replace(chatHref(computer.id, forked)),
            () => undefined,
          )
        }
        onArchive={() => {
          router.back()
          void session.archive(key).catch(() => undefined)
        }}
      />
      {changes ? (
        <ChangesSheet
          changes={changes}
          visible
          onClose={() => setChangesTurn(null)}
          onOpenFile={openFile ? (path) => openFile(workspaceFile(path, workspacePath)) : null}
        />
      ) : null}
      <FileViewerContext.Provider value={openFile}>
        <ImageReaderContext.Provider value={imageReader}>
          <FileSheet path={openPath} read={readFile} onClose={() => setOpenPath(null)} />
        </ImageReaderContext.Provider>
      </FileViewerContext.Provider>
      <TasksSheet
        visible={tasksOpen}
        running={tasks.running}
        completed={tasks.completed}
        onClose={() => setTasksOpen(false)}
        onStop={(task) => session.stopTask(key, task)}
      />
    </Screen>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  pills: { flexDirection: 'row', justifyContent: 'center', gap: 8 },
  pillScroll: { flexGrow: 0, marginVertical: -6, marginHorizontal: -metrics.gutter },
  pillRow: { flexGrow: 1, paddingVertical: 6, paddingHorizontal: metrics.gutter },
})

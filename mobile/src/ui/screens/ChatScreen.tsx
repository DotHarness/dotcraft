import { useRouter } from 'expo-router'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { KeyboardAvoidingView, ScrollView, StyleSheet, View, type NativeScrollEvent, type NativeSyntheticEvent } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useComputer, useComputerLink } from '../../app-state/SessionContext'
import { awaitsPlanConfirmation, isLive, type ChatState } from '../../core/chatState'
import { usedShare } from '../../core/contextUsage'
import { waitingDecisions } from '../../core/decisions'
import { plainMessage } from '../../core/draft'
import { chatKey, computerStatus, isReachable, projectById, stateOf, type PendingRequest } from '../../core/state'
import { workspaceFile } from '../../core/links'
import { controlsOf, offersPlanMode, type ChatControls } from '../../core/threadConfig'
import { buildTranscript } from '../../core/transcript'
import { runningTurnChanges, turnChanges } from '../../core/turnChanges'
import { useI18n } from '../../i18n'
import { ChangesOpenerContext, ChangesPill, ChangesSheet } from '../chat/Changes'
import { BAR_HEIGHT, BarButton, ChatBar } from '../chat/ChatBar'
import { ChatMenu } from '../chat/ChatMenu'
import { FileViewerContext } from '../chat/Chips'
import { Composer } from '../chat/Composer'
import { Dock, useModels, useReferences } from '../chat/NewChat'
import { catalogItem, ComposerControls, type ControlChange } from '../chat/ComposerControls'
import { ContextRing } from '../chat/ContextRing'
import type { DecisionActions } from '../chat/DecisionBodies'
import { DecisionCard } from '../chat/DecisionCard'
import { FileSheet } from '../chat/FileSheet'
import { imageScope } from '../../core/imageCache'
import { ImageReaderContext } from '../chat/Images'
import { ScrollToBottom } from '../chat/ScrollToBottom'
import { StatusPopover } from '../chat/StatusPopover'
import { TranscriptLine } from '../chat/Transcript'
import { Screen } from '../layout'
import { MascotNote, MascotTransition } from '../mascot/Mascot'
import { Notice, PhoneButton, ReadOnlyNotice } from '../parts'
import { chatTitle, projectTitle } from '../rows'
import { metrics } from '../theme'
import { chatHref } from './HomeScreen'

export function ChatScreen({ projectId, threadId }: { projectId: string; threadId: string }) {
  const key = chatKey(projectId, threadId)
  const state = useComputer()
  const session = useComputerLink()
  const router = useRouter()
  const { t } = useI18n()
  const [dismissedPlan, setDismissedPlan] = useState<string | null>(null)
  const [menuOpen, setMenuOpen] = useState(false)
  const [statusOpen, setStatusOpen] = useState(false)
  const [changesOpen, setChangesOpen] = useState(false)
  const [changesTurn, setChangesTurn] = useState<string | null>(null)
  const [openPath, setOpenPath] = useState<string | null>(null)
  const scroller = useRef<ScrollView>(null)
  const pinned = useRef(true)
  const jumping = useRef(false)
  const [away, setAway] = useState(false)
  const [dockHeight, setDockHeight] = useState(0)

  useEffect(() => {
    session.openChat(key)
    return () => session.closeChat(key)
  }, [key, session])

  const chat = state.chats[key]
  const detail = state.details[key]
  const project = projectById(state, projectId)
  const projectName = project ? projectTitle(project, t) : ''
  const computer = state.computer
  const status = computerStatus(state)
  const online = isReachable(status)
  const running = project?.running ?? false
  const live = online && running
  const ready = state.phases[projectId] === 'ready'
  const chatState: ChatState = chat ? stateOf(chat) : 'done'
  const pending: PendingRequest[] = state.pending[key] ?? []
  const transcript = useMemo(() => (detail ? buildTranscript(detail.history, detail.workspacePath) : []), [detail])
  const catchingUp = live && (detail ? detail.loading && detail.history.items.length === 0 : true)
  const profile = chat?.profileId ? (detail?.profileName ?? chat.profileId) : null
  const configured = controlsOf(detail?.config)
  const models = useModels(projectId, ready, configured.providerId)
  const controls: ChatControls = { ...configured, providerId: configured.providerId ?? models?.defaultProviderId ?? null }
  const references = useReferences(projectId, ready)
  const planMode = detail?.config?.mode === 'plan'
  const planTurn = awaitsPlanConfirmation(chat?.runtime ?? null, detail?.config?.mode) ? (detail?.history.turns.at(-1)?.id ?? key) : null
  const decisions = waitingDecisions(pending, planTurn !== dismissedPlan ? planTurn : null)
  const decision = live ? (decisions[0] ?? null) : null
  const insets = useSafeAreaInsets()
  const canPlan = offersPlanMode(detail?.config, chat?.profileId)
  const runningChanges = useMemo(() => (detail && isLive(chatState) ? runningTurnChanges(detail.history, detail.workspacePath) : null), [detail, chatState])
  const changes = useMemo(() => (detail && changesTurn ? turnChanges(detail.history, changesTurn, detail.workspacePath) : null), [detail, changesTurn])
  const openChanges = useCallback((turnId: string) => {
    setChangesTurn(turnId)
    setChangesOpen(true)
  }, [])
  const readFile = useCallback((path: string) => session.readFile(projectId, path), [projectId, session])
  const computerId = state.computer.id
  const reading = ready && models?.fileSystem === true
  const imageReader = useMemo(
    () => ({ scope: imageScope(computerId, projectId), read: reading ? readFile : null }),
    [computerId, projectId, readFile, reading],
  )
  const openFile = models?.fileSystem === true ? setOpenPath : null
  const workspacePath = detail?.workspacePath ?? null
  const docked = !catchingUp && live && !decision
  const inset = docked ? dockHeight : insets.bottom
  const signsIn = models?.providers.find((provider) => provider.id === controls.providerId)?.signsIn === true

  const title = chat ? chatTitle(chat, t('chat.untitled')) : t('chat.untitled')

  const track = ({ nativeEvent }: NativeSyntheticEvent<NativeScrollEvent>) => {
    const { contentSize, contentOffset, layoutMeasurement } = nativeEvent
    const atEnd = contentSize.height - contentOffset.y - layoutMeasurement.height < 48
    if (jumping.current && !atEnd) return
    jumping.current = false
    pinned.current = atEnd
    setAway(!atEnd)
  }

  const jumpToEnd = () => {
    jumping.current = true
    pinned.current = true
    setAway(false)
    scroller.current?.scrollToEnd({ animated: true })
  }

  const openStatus = () => {
    setStatusOpen(true)
    void session.loadUsage(projectId).catch(() => undefined)
  }

  const actions: DecisionActions = {
    decide: (requestId, choice) => session.decide(key, requestId, choice),
    answer: (requestId, answers) => session.answer(key, requestId, answers),
    dismissQuestion: (request) => session.dismissQuestion(key, request),
    implement: () => session.implementPlan(key),
    feedback: (text) => session.send(key, plainMessage(text)),
    dismissPlan: () => setDismissedPlan(planTurn),
  }

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
            <ScrollView
              ref={scroller}
              style={styles.fill}
              contentContainerStyle={[styles.transcript, { paddingBottom: inset + (docked ? 32 : 16) }]}
              keyboardShouldPersistTaps="handled"
              keyboardDismissMode="on-drag"
              showsVerticalScrollIndicator={false}
              scrollEventThrottle={64}
              onScroll={track}
              onScrollBeginDrag={() => {
                jumping.current = false
              }}
              onScrollEndDrag={track}
              onMomentumScrollEnd={track}
              onContentSizeChange={() => {
                if (pinned.current) scroller.current?.scrollToEnd({ animated: false })
              }}
              onLayout={() => {
                if (pinned.current) scroller.current?.scrollToEnd({ animated: false })
              }}
            >
              {!online ? <ReadOnlyNotice status={status} computer={computer.name} /> : null}
              {online && project && !running ? (
                <Notice
                  icon="info"
                  action={
                    <PhoneButton
                      variant="outline"
                      compact
                      loading={state.phases[projectId] === 'starting'}
                      disabled={status !== 'online'}
                      onPress={() => void session.startProject(projectId)}
                    >
                      {t('notice.start')}
                    </PhoneButton>
                  }
                >
                  {state.phases[projectId] === 'cantStart'
                    ? t('project.cantStart', { computer: computer.name, project: projectName })
                    : t('notice.projectStopped', { project: projectName, computer: computer.name })}
                </Notice>
              ) : null}
              {transcript.length === 0 && detail && !detail.loading ? (
                <MascotNote moment="greeting" profile={profile}>
                  {t('newChat.runsOn', { computer: computer.name, project: projectName })}
                </MascotNote>
              ) : null}
              <FileViewerContext.Provider value={openFile}>
                <ImageReaderContext.Provider value={imageReader}>
                  <ChangesOpenerContext.Provider value={openChanges}>
                    {transcript.map((entry, index) => (
                      <TranscriptLine key={entry.id} entry={entry} previous={transcript[index - 1]} workspacePath={workspacePath} />
                    ))}
                  </ChangesOpenerContext.Provider>
                </ImageReaderContext.Provider>
              </FileViewerContext.Provider>
              {decision ? (
                <View style={styles.decision}>
                  <DecisionCard key={decision.requestId} decision={decision} count={decisions.length} disabled={!ready} actions={actions} />
                </View>
              ) : null}
            </ScrollView>
          )}
          <ChatBar
            title={title}
            project={projectName}
            computer={computer.name}
            status={status}
            onBack={() => router.back()}
            trailing={
              <>
                <ContextRing used={detail?.context ? usedShare(detail.context) : null} onPress={openStatus} />
                <BarButton icon="ellipsisVertical" label={t('chat.menu')} onPress={() => setMenuOpen(true)} />
              </>
            }
          />
          {docked ? (
            <Dock
              floating
              above={runningChanges ? <ChangesPill changes={runningChanges} onPress={() => openChanges(runningChanges.turnId)} /> : null}
              onLayout={({ nativeEvent }) => setDockHeight(nativeEvent.layout.height)}
            >
              <Composer
                key={key}
                running={isLive(chatState)}
                canSend={ready}
                canAttachFiles={models?.fileSystem === true}
                canPlan={canPlan}
                references={references}
                planMode={planMode}
                onPlanMode={(on) => session.setMode(key, on ? 'plan' : 'agent')}
                controls={<ComposerControls projectId={projectId} controls={controls} models={models} onChange={change} />}
                onSend={(draft) => session.send(key, draft)}
                onStop={() => void session.stop(key).catch(() => undefined)}
              />
            </Dock>
          ) : null}
          {catchingUp ? null : (
            <ScrollToBottom
              visible={away}
              working={isLive(chatState)}
              bottom={inset + 16}
              onPress={jumpToEnd}
            />
          )}
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
        onFork={() => void session.fork(key).then((forked) => router.replace(chatHref(computer.id, forked)), () => undefined)}
        onArchive={() => {
          router.back()
          void session.archive(key).catch(() => undefined)
        }}
      />
      <StatusPopover
        visible={statusOpen}
        chatId={threadId}
        folder={workspacePath}
        context={detail?.context ?? null}
        windows={signsIn ? (models?.usage ?? []) : []}
        onClose={() => setStatusOpen(false)}
      />
      {changes ? (
        <ChangesSheet
          changes={changes}
          visible={changesOpen}
          onClose={() => setChangesOpen(false)}
          onOpenFile={openFile ? (path) => openFile(workspaceFile(path, workspacePath)) : null}
        />
      ) : null}
      <FileSheet path={openPath} read={readFile} onClose={() => setOpenPath(null)} />
    </Screen>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  transcript: { flexGrow: 1, gap: 14, paddingTop: BAR_HEIGHT + 20, paddingHorizontal: metrics.gutter, paddingBottom: 16 },
  decision: { marginTop: 'auto' },
})

import { useRouter } from 'expo-router'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Keyboard, KeyboardAvoidingView, Pressable, ScrollView, StyleSheet, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useMobileState, useSession } from '../../app-state/SessionContext'
import { awaitsPlanConfirmation, isLive, type ChatState } from '../../core/chatState'
import { usedShare } from '../../core/contextUsage'
import { waitingDecisions } from '../../core/decisions'
import { draftTitle, plainMessage, type MessageDraft, type ReferenceEntry } from '../../core/draft'
import { CantStartProjectError } from '../../core/session'
import { chatKey, computerStatus, isReachable, projectById, stateOf, type PendingRequest, type ProjectModels } from '../../core/state'
import { workspaceFile } from '../../core/links'
import { controlsOf, offersPlanMode, startConfig, type ChatControls, type NewChatChoices } from '../../core/threadConfig'
import { buildTranscript } from '../../core/transcript'
import { latestChanges } from '../../core/turnChanges'
import { useI18n } from '../../i18n'
import { ChangesPill, ChangesSheet } from '../chat/Changes'
import { BAR_HEIGHT, BarButton, ChatBar } from '../chat/ChatBar'
import { ChatMenu } from '../chat/ChatMenu'
import { FileViewerContext } from '../chat/Chips'
import { Composer } from '../chat/Composer'
import { catalogItem, ComposerControls, defaultModel, type ControlChange } from '../chat/ComposerControls'
import { ContextRing } from '../chat/ContextRing'
import type { DecisionActions } from '../chat/DecisionBodies'
import { DecisionDrawer } from '../chat/DecisionDrawer'
import { FileSheet } from '../chat/FileSheet'
import { StatusPopover } from '../chat/StatusPopover'
import { TranscriptLine } from '../chat/Transcript'
import { Screen } from '../layout'
import { MascotNote, MascotTransition } from '../mascot/Mascot'
import { Notice, PhoneButton, ReadOnlyNotice } from '../parts'
import { chatTitle } from '../rows'
import { metrics, useTheme } from '../theme'
import { chatHref } from './HomeScreen'

function Dock({ children }: { children: ReactNode }) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  return <View style={[styles.dock, { paddingBottom: insets.bottom + 6, backgroundColor: colors.bgPrimary }]}>{children}</View>
}

function useModels(projectId: string, ready: boolean, providerId: string | null): ProjectModels | undefined {
  const session = useSession()
  const models = useMobileState().models[projectId]
  const listable = ready && models?.canListModels === true
  const loaded = providerId ? Boolean(models?.catalogs[providerId]) : false
  useEffect(() => {
    if (!listable) return
    void session.loadModels(projectId).catch(() => undefined)
    if (providerId && !loaded) void session.loadModels(projectId, providerId).catch(() => undefined)
  }, [listable, loaded, projectId, providerId, session])
  return models
}

const NO_REFERENCES: ReferenceEntry[] = []

function useReferences(projectId: string, ready: boolean): ReferenceEntry[] {
  const session = useSession()
  const state = useMobileState()
  const models = state.models[projectId]
  const listable = ready && (models?.canListCommands === true || models?.canListSkills === true)
  useEffect(() => {
    if (listable) void session.loadReferences(projectId).catch(() => undefined)
  }, [listable, projectId, session])
  return state.references[projectId] ?? NO_REFERENCES
}

export function ChatScreen({ projectId, threadId }: { projectId: string; threadId: string }) {
  const key = chatKey(projectId, threadId)
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  const [dismissedPlan, setDismissedPlan] = useState<string | null>(null)
  const [menuOpen, setMenuOpen] = useState(false)
  const [statusOpen, setStatusOpen] = useState(false)
  const [changesOpen, setChangesOpen] = useState(false)
  const [openPath, setOpenPath] = useState<string | null>(null)
  const scroller = useRef<ScrollView>(null)
  const pinned = useRef(true)

  useEffect(() => {
    session.openChat(key)
    return () => session.closeChat(key)
  }, [key, session])

  const chat = state.chats[key]
  const detail = state.details[key]
  const project = projectById(state, projectId)
  const computer = state.computer
  const status = computerStatus(state)
  const online = isReachable(status)
  const running = project?.running ?? false
  const live = online && running
  const ready = state.phases[projectId] === 'ready'
  const chatState: ChatState = chat ? stateOf(chat) : 'done'
  const pending: PendingRequest[] = state.pending[key] ?? []
  const transcript = useMemo(() => (detail ? buildTranscript(detail.history) : []), [detail])
  const catchingUp = live && (detail ? detail.loading && detail.history.items.length === 0 : true)
  const profile = chat?.profileId ? (detail?.profileName ?? chat.profileId) : null
  const configured = controlsOf(detail?.config)
  const models = useModels(projectId, ready, configured.providerId)
  const controls: ChatControls = { ...configured, providerId: configured.providerId ?? models?.defaultProviderId ?? null }
  const references = useReferences(projectId, ready)
  const planMode = detail?.config?.mode === 'plan'
  const planTurn = awaitsPlanConfirmation(chat?.runtime ?? null, detail?.config?.mode) ? (detail?.history.turns.at(-1)?.id ?? key) : null
  const decisions = waitingDecisions(pending, planTurn !== dismissedPlan ? planTurn : null)
  const decision = decisions[0] ?? null
  const planTitle = useMemo(() => [...transcript].reverse().find((entry) => entry.kind === 'plan')?.title ?? null, [transcript])
  const canPlan = offersPlanMode(detail?.config, chat?.profileId)
  const changes = useMemo(() => (detail ? latestChanges(detail.history, detail.workspacePath) : null), [detail])
  const readFile = useCallback((path: string) => session.readFile(projectId, path), [projectId, session])
  const openFile = models?.fileSystem === true ? setOpenPath : null
  const workspacePath = detail?.workspacePath ?? null
  const signsIn = models?.providers.find((provider) => provider.id === controls.providerId)?.signsIn === true

  if (!computer) return <Screen>{null}</Screen>
  const title = chat ? chatTitle(chat, t('chat.untitled')) : t('chat.untitled')
  const stoppable = live && isLive(chatState)

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
              contentContainerStyle={[styles.transcript, !live && { paddingBottom: 16 + 34 }]}
              keyboardShouldPersistTaps="handled"
              keyboardDismissMode="on-drag"
              showsVerticalScrollIndicator={false}
              scrollEventThrottle={64}
              onScroll={({ nativeEvent }) => {
                const { contentSize, contentOffset, layoutMeasurement } = nativeEvent
                pinned.current = contentSize.height - contentOffset.y - layoutMeasurement.height < 48
              }}
              onContentSizeChange={() => {
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
                    ? t('project.cantStart', { computer: computer.name, project: project.name })
                    : t('notice.projectStopped', { project: project.name, computer: computer.name })}
                </Notice>
              ) : null}
              {transcript.length === 0 && detail && !detail.loading ? (
                <MascotNote moment="greeting" profile={profile}>
                  {t('newChat.runsOn', { computer: computer.name, project: project?.name ?? '' })}
                </MascotNote>
              ) : null}
              <FileViewerContext.Provider value={openFile}>
                {transcript.map((entry, index) => (
                  <TranscriptLine key={entry.id} entry={entry} previous={transcript[index - 1]} workspacePath={workspacePath} />
                ))}
              </FileViewerContext.Provider>
            </ScrollView>
          )}
          <ChatBar
            title={title}
            project={project?.name ?? ''}
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
        </View>

        {!catchingUp && live ? (
          <Dock>
            {decision ? (
              <DecisionDrawer
                key={decision.requestId}
                decision={decision}
                count={decisions.length}
                disabled={!ready}
                planTitle={planTitle}
                actions={actions}
              />
            ) : (
              <>
                {changes ? <ChangesPill changes={changes} onPress={() => setChangesOpen(true)} /> : null}
                <Composer
                  key={key}
                  computer={computer.name}
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
              </>
            )}
          </Dock>
        ) : null}
      </KeyboardAvoidingView>
      <ChatMenu
        visible={menuOpen}
        title={title}
        ready={live && ready}
        canFork={models?.canFork === true}
        stoppable={stoppable}
        onClose={() => setMenuOpen(false)}
        onRename={(name) => void session.rename(key, name).catch(() => undefined)}
        onFork={() => void session.fork(key).then((forked) => router.replace(chatHref(forked)), () => undefined)}
        onArchive={() => {
          router.back()
          void session.archive(key).catch(() => undefined)
        }}
        onOpenProject={() => router.push({ pathname: '/project/[projectId]', params: { projectId } })}
        onStop={() => void session.stop(key).catch(() => undefined)}
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

const UNTOUCHED: NewChatChoices = {
  touched: {},
  controls: { providerId: null, model: null, reasoning: 'default', speed: 'standard', approvalPolicy: 'prompt' },
}

function chosen(previous: NewChatChoices, change: ControlChange): NewChatChoices {
  const { touched, controls } = previous
  switch (change.kind) {
    case 'provider':
      return {
        touched: { approval: touched.approval },
        controls: { ...controls, providerId: change.providerId, model: null, reasoning: 'default', speed: 'standard' },
      }
    case 'model':
      return {
        touched: { approval: touched.approval },
        controls: { ...controls, providerId: change.providerId, model: change.model, reasoning: 'default', speed: 'standard' },
      }
    case 'reasoning':
      return { touched: { ...touched, reasoning: true }, controls: { ...controls, reasoning: change.value } }
    case 'speed':
      return { touched: { ...touched, speed: true }, controls: { ...controls, speed: change.speed } }
    case 'approval':
      return { touched: { ...touched, approval: true }, controls: { ...controls, approvalPolicy: change.policy } }
  }
}

export function NewChatScreen({ projectId }: { projectId: string }) {
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  const [phase, setPhase] = useState<'idle' | 'starting' | 'cantStart'>('idle')
  const [choices, setChoices] = useState(UNTOUCHED)
  const [planMode, setPlanMode] = useState(false)
  const created = useRef<string | null>(null)
  const project = projectById(state, projectId)
  const ready = state.phases[projectId] === 'ready'
  const models = useModels(projectId, ready, choices.controls.providerId)
  const references = useReferences(projectId, ready)
  const computer = state.computer
  if (!computer || !project) return <Screen>{null}</Screen>
  const status = computerStatus(state)
  const providerId = choices.controls.providerId ?? models?.defaultProviderId ?? null
  const controls: ChatControls = { ...choices.controls, providerId, model: choices.controls.model ?? defaultModel(models, providerId) }

  async function send(draft: MessageDraft) {
    if (!project?.running) setPhase('starting')
    try {
      const config = startConfig({ touched: choices.touched, controls })
      const key = created.current ?? (await session.newChat(projectId, draftTitle(draft), planMode ? { ...config, mode: 'plan' } : config))
      created.current = key
      await session.send(key, draft)
      router.replace(chatHref(key))
    } catch (error) {
      setPhase(error instanceof CantStartProjectError ? 'cantStart' : 'idle')
      throw error
    }
  }

  return (
    <Screen>
      <KeyboardAvoidingView style={styles.fill} behavior="padding">
        <Pressable accessible={false} onPress={Keyboard.dismiss} style={[styles.fill, styles.emptyBody]}>
          {phase === 'starting' ? (
            <MascotTransition line={t('project.starting', { project: project.name })} />
          ) : phase === 'cantStart' ? (
            <MascotNote moment="asleep">{t('project.cantStart', { computer: computer.name, project: project.name })}</MascotNote>
          ) : (
            <MascotNote moment="greeting">
              {project.running
                ? t('newChat.runsOn', { computer: computer.name, project: project.name })
                : t('newChat.startsOn', { computer: computer.name, project: project.name })}
            </MascotNote>
          )}
          <ChatBar title={t('newChat.title')} project={project.name} computer={computer.name} status={status} onBack={() => router.back()} />
        </Pressable>
        <Dock>
          <Composer
            computer={computer.name}
            running={false}
            autoFocus
            canSend={status === 'online' && phase !== 'starting'}
            canAttachFiles={models?.fileSystem === true}
            canPlan
            references={references}
            planMode={planMode}
            onPlanMode={async (on) => setPlanMode(on)}
            controls={
              <ComposerControls
                projectId={projectId}
                controls={controls}
                models={models}
                onChange={async (change) => setChoices((previous) => chosen(previous, change))}
              />
            }
            onSend={send}
            onStop={() => undefined}
          />
        </Dock>
      </KeyboardAvoidingView>
    </Screen>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  transcript: { flexGrow: 1, gap: 14, paddingTop: BAR_HEIGHT + 20, paddingHorizontal: metrics.gutter, paddingBottom: 16 },
  dock: { gap: 10, paddingTop: 8, paddingHorizontal: metrics.gutter },
  emptyBody: { paddingHorizontal: metrics.gutter },
})

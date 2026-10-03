import { useRouter } from 'expo-router'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { KeyboardAvoidingView, ScrollView, StyleSheet, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useMobileState, useSession } from '../../app-state/SessionContext'
import { isLive, type ChatState } from '../../core/chatState'
import { CantStartProjectError } from '../../core/session'
import { chatKey, computerStatus, isReachable, projectById, stateOf, type PendingRequest, type ProjectModels } from '../../core/state'
import { controlsOf, startConfig, type ChatControls, type NewChatChoices } from '../../core/threadConfig'
import { buildTranscript } from '../../core/transcript'
import { useI18n } from '../../i18n'
import { BAR_HEIGHT, BarButton, ChatBar } from '../chat/ChatBar'
import { ChatMenu } from '../chat/ChatMenu'
import { Composer } from '../chat/Composer'
import { catalogItem, ComposerControls, type ControlChange } from '../chat/ComposerControls'
import { ApprovalCard, ApprovalSheet, QuestionCard } from '../chat/RequestCards'
import { TranscriptLine } from '../chat/Transcript'
import { Screen } from '../layout'
import { Mascot, MascotNote, MascotTransition } from '../mascot/Mascot'
import { Notice, PhoneButton, ReadOnlyNotice, StateMark, Txt } from '../parts'
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

export function ChatScreen({ projectId, threadId }: { projectId: string; threadId: string }) {
  const key = chatKey(projectId, threadId)
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  const [sheetOpen, setSheetOpen] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
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
  const approval = pending.find((request): request is Extract<PendingRequest, { kind: 'approval' }> => request.kind === 'approval') ?? null
  const question = pending.find((request): request is Extract<PendingRequest, { kind: 'question' }> => request.kind === 'question') ?? null
  const transcript = useMemo(() => (detail ? buildTranscript(detail.history) : []), [detail])
  const catchingUp = live && (detail ? detail.loading && detail.history.items.length === 0 : true)
  const profile = chat?.profileId ? (detail?.profileName ?? chat.profileId) : null
  const configured = controlsOf(detail?.config)
  const models = useModels(projectId, ready, configured.providerId)
  const controls: ChatControls = { ...configured, providerId: configured.providerId ?? models?.defaultProviderId ?? null }

  if (!computer) return <Screen>{null}</Screen>
  const title = chat ? chatTitle(chat, t('chat.untitled')) : t('chat.untitled')
  const stoppable = live && isLive(chatState)

  const change = (next: ControlChange) => {
    if (next.kind !== 'model') return session.updateConfig(key, next)
    if (!next.model) return Promise.resolve()
    return session.updateConfig(key, { ...next, model: next.model, catalog: catalogItem(models, next.providerId, next.model) })
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
              {transcript.map((entry, index) => (
                <TranscriptLine
                  key={entry.id}
                  entry={entry}
                  previous={transcript[index - 1]}
                  caret={live && chatState === 'running' && index === transcript.length - 1 && entry.kind === 'assistant'}
                  workspacePath={detail?.workspacePath ?? null}
                />
              ))}
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
                {chatState === 'running' ? <StateMark state={chatState} live={live} /> : null}
                <BarButton icon="ellipsisVertical" label={t('chat.menu')} onPress={() => setMenuOpen(true)} />
              </>
            }
          />
        </View>

        {!catchingUp && live ? (
          <Dock>
            {approval ? (
              <ApprovalCard
                request={approval}
                disabled={!ready}
                onDecide={(decision) => session.decide(key, approval.requestId, decision)}
                onDetails={() => setSheetOpen(true)}
              />
            ) : null}
            {question && !approval ? (
              <QuestionCard
                key={question.requestId}
                request={question}
                disabled={!ready}
                onAnswer={(answers) => session.answer(key, question.requestId, answers)}
              />
            ) : null}
            <Composer
              key={key}
              computer={computer.name}
              running={isLive(chatState)}
              canSend={ready}
              controls={<ComposerControls projectId={projectId} controls={controls} models={models} allowDefault={false} onChange={change} />}
              onSend={(text) => session.send(key, text)}
              onStop={() => void session.stop(key).catch(() => undefined)}
            />
          </Dock>
        ) : null}
      </KeyboardAvoidingView>
      {approval ? (
        <ApprovalSheet
          request={approval}
          visible={sheetOpen}
          disabled={!ready}
          onClose={() => setSheetOpen(false)}
          onDecide={(decision) => {
            setSheetOpen(false)
            session.decide(key, approval.requestId, decision)
          }}
        />
      ) : null}
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
    case 'model':
      return {
        touched: { approval: touched.approval, ...(change.model ? { model: true } : {}) },
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
  const project = projectById(state, projectId)
  const ready = state.phases[projectId] === 'ready'
  const models = useModels(projectId, ready, choices.controls.providerId)
  const computer = state.computer
  if (!computer || !project) return <Screen>{null}</Screen>
  const status = computerStatus(state)
  const controls: ChatControls = { ...choices.controls, providerId: choices.controls.providerId ?? models?.defaultProviderId ?? null }

  async function send(text: string) {
    if (!project?.running) setPhase('starting')
    try {
      const key = await session.newChat(projectId, text, startConfig({ touched: choices.touched, controls }))
      router.replace(chatHref(key))
    } catch (error) {
      setPhase(error instanceof CantStartProjectError ? 'cantStart' : 'idle')
      throw error
    }
  }

  return (
    <Screen>
      <KeyboardAvoidingView style={styles.fill} behavior="padding">
        <View style={[styles.fill, styles.emptyBody]}>
          {phase === 'starting' ? (
            <MascotTransition line={t('project.starting', { project: project.name })} />
          ) : phase === 'cantStart' ? (
            <MascotNote moment="asleep">{t('project.cantStart', { computer: computer.name, project: project.name })}</MascotNote>
          ) : (
            <View style={styles.greeting}>
              <Mascot moment="greeting" size={72} />
              <Txt tone="secondary" style={styles.centered}>
                {project.running
                  ? t('newChat.runsOn', { computer: computer.name, project: project.name })
                  : t('newChat.startsOn', { computer: computer.name, project: project.name })}
              </Txt>
            </View>
          )}
          <ChatBar title={t('newChat.title')} project={project.name} computer={computer.name} status={status} onBack={() => router.back()} />
        </View>
        <Dock>
          <Composer
            computer={computer.name}
            running={false}
            autoFocus
            canSend={status === 'online' && phase !== 'starting'}
            controls={
              <ComposerControls
                projectId={projectId}
                controls={controls}
                models={models}
                allowDefault
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
  transcript: { gap: 14, paddingTop: BAR_HEIGHT + 20, paddingHorizontal: metrics.gutter, paddingBottom: 16 },
  dock: { gap: 10, paddingTop: 8, paddingHorizontal: metrics.gutter },
  emptyBody: { justifyContent: 'center', alignItems: 'center', paddingHorizontal: metrics.gutter },
  greeting: { alignItems: 'center', gap: 14 },
  centered: { textAlign: 'center', maxWidth: 300 },
})

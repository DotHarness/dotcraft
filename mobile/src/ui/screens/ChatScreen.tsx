import { useRouter } from 'expo-router'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { KeyboardAvoidingView, ScrollView, StyleSheet, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useMobileState, useSession } from '../../app-state/SessionContext'
import { isLive, type ChatState } from '../../core/chatState'
import { CantStartProjectError } from '../../core/session'
import { chatKey, computerStatus, isReachable, projectById, stateOf, type PendingRequest } from '../../core/state'
import { buildTranscript } from '../../core/transcript'
import { useI18n } from '../../i18n'
import { Composer } from '../chat/Composer'
import { ApprovalCard, ApprovalSheet, QuestionCard } from '../chat/RequestCards'
import { TranscriptLine } from '../chat/Transcript'
import { Screen } from '../layout'
import { Mascot, MascotNote, MascotTransition, type MascotMoment } from '../mascot/Mascot'
import { BackButton, ChatStateLine, Notice, PhoneButton, ReadOnlyNotice, Txt } from '../parts'
import { chatTitle } from '../rows'
import { metrics, useTheme } from '../theme'
import { chatHref } from './HomeScreen'

function composerMoment(state: ChatState): MascotMoment {
  if (state === 'running') return 'working'
  if (state === 'needs-approval' || state === 'needs-answer') return 'question'
  return state === 'failed' ? 'sad' : 'idle'
}

function ChatBar({ title, meta, profile, onBack }: { title: string; meta: ReactNode; profile?: string | null; onBack: () => void }) {
  const { colors } = useTheme()
  return (
    <View style={[styles.chatBar, { borderBottomColor: colors.borderSubtle }]}>
      <BackButton onPress={onBack} />
      {profile ? <Mascot moment="idle" profile={profile} size={28} /> : null}
      <View style={styles.chatBarText}>
        <Txt accessibilityRole="header" numberOfLines={1} style={styles.chatTitle}>
          {title}
        </Txt>
        <View style={styles.chatMeta}>{meta}</View>
      </View>
    </View>
  )
}

function Dot() {
  return (
    <Txt variant="meta" tone="secondary" accessibilityElementsHidden importantForAccessibility="no">
      ·
    </Txt>
  )
}

function Dock({ children }: { children: ReactNode }) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  return <View style={[styles.dock, { paddingBottom: insets.bottom + 4, backgroundColor: colors.bgPrimary }]}>{children}</View>
}

export function ChatScreen({ projectId, threadId }: { projectId: string; threadId: string }) {
  const key = chatKey(projectId, threadId)
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  const [sheetOpen, setSheetOpen] = useState(false)
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

  if (!computer) return <Screen>{null}</Screen>
  const title = chat ? chatTitle(chat, t('chat.untitled')) : t('chat.untitled')

  return (
    <Screen>
      <KeyboardAvoidingView style={styles.fill} behavior="padding">
        <ChatBar
          title={title}
          profile={profile}
          onBack={() => router.back()}
          meta={
            <>
              {profile ? (
                <>
                  <Txt variant="meta" tone="secondary" numberOfLines={1}>
                    {profile}
                  </Txt>
                  <Dot />
                </>
              ) : null}
              <Txt variant="meta" tone="secondary" numberOfLines={1}>
                {project?.name ?? ''}
              </Txt>
              <Dot />
              <ChatStateLine state={chatState} live={live} />
            </>
          }
        />

        {!online ? <ReadOnlyNotice status={status} computer={computer.name} style={styles.screenNotice} /> : null}
        {online && project && !running ? (
          <Notice
            icon="info"
            style={styles.screenNotice}
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
            {transcript.map((entry, index) => (
              <TranscriptLine
                key={entry.id}
                entry={entry}
                previous={transcript[index - 1]}
                caret={live && chatState === 'running' && index === transcript.length - 1 && entry.kind === 'assistant'}
              />
            ))}
          </ScrollView>
        )}

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
              running={isLive(chatState)}
              canSend={ready}
              mascot={<Mascot moment={composerMoment(chatState)} profile={profile} size={36} />}
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
    </Screen>
  )
}

export function NewChatScreen({ projectId }: { projectId: string }) {
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  const [phase, setPhase] = useState<'idle' | 'starting' | 'cantStart'>('idle')
  const project = projectById(state, projectId)
  const computer = state.computer
  if (!computer || !project) return <Screen>{null}</Screen>
  const status = computerStatus(state)

  async function send(text: string) {
    if (!project?.running) setPhase('starting')
    try {
      const key = await session.newChat(projectId, text)
      router.replace(chatHref(key))
    } catch (error) {
      setPhase(error instanceof CantStartProjectError ? 'cantStart' : 'idle')
      throw error
    }
  }

  return (
    <Screen>
      <KeyboardAvoidingView style={styles.fill} behavior="padding">
        <ChatBar
          title={t('newChat.title')}
          onBack={() => router.back()}
          meta={
            <Txt variant="meta" tone="secondary" numberOfLines={1}>
              {project.name}
            </Txt>
          }
        />
        <View style={[styles.fill, styles.emptyBody]}>
          {phase === 'starting' ? (
            <MascotTransition line={t('project.starting', { project: project.name })} />
          ) : phase === 'cantStart' ? (
            <MascotNote moment="asleep">{t('project.cantStart', { computer: computer.name, project: project.name })}</MascotNote>
          ) : (
            <Txt tone="secondary" style={styles.centered}>
              {project.running
                ? t('newChat.runsOn', { computer: computer.name, project: project.name })
                : t('newChat.startsOn', { computer: computer.name, project: project.name })}
            </Txt>
          )}
        </View>
        <Dock>
          <Composer
            running={false}
            autoFocus
            canSend={status === 'online' && phase !== 'starting'}
            mascot={<Mascot moment="idle" size={36} />}
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
  chatBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingTop: 4,
    paddingHorizontal: 12,
    paddingBottom: 8,
    borderBottomWidth: 1,
  },
  chatBarText: { flex: 1, minWidth: 0 },
  chatTitle: { fontWeight: '600' },
  chatMeta: { flexDirection: 'row', alignItems: 'center', gap: 4, minWidth: 0, overflow: 'hidden' },
  screenNotice: { marginTop: 10, marginHorizontal: metrics.gutter },
  transcript: { gap: 14, paddingTop: 16, paddingHorizontal: metrics.gutter, paddingBottom: 16 },
  dock: { gap: 10, paddingTop: 8, paddingHorizontal: metrics.gutter },
  emptyBody: { justifyContent: 'center', alignItems: 'center', paddingHorizontal: metrics.gutter },
  centered: { textAlign: 'center' },
})

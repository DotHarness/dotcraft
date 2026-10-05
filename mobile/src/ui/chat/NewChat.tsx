import { useEffect, useState, type ReactNode } from 'react'
import { BackHandler, Keyboard, KeyboardAvoidingView, LayoutAnimation, Pressable, StyleSheet, Text, View, type LayoutChangeEvent } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import Svg, { Defs, LinearGradient, Rect, Stop } from 'react-native-svg'
import { useComputer, useComputerLink } from '../../app-state/SessionContext'
import { draftTitle, EMPTY_DRAFT, isEmptyDraft, type MessageDraft, type ReferenceEntry } from '../../core/draft'
import { CantStartProjectError } from '../../core/session'
import { computerStatus, projectById, type ComputerState, type ProjectModels } from '../../core/state'
import { startConfig, type ChatControls, type NewChatChoices } from '../../core/threadConfig'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { MascotNote, MascotTransition } from '../mascot/Mascot'
import { Txt } from '../parts'
import { projectIcon, projectTitle } from '../rows'
import { metrics, type, useTheme } from '../theme'
import { Composer, type PendingSend } from './Composer'
import { ComposerControls, defaultModel, type ControlChange } from './ComposerControls'
import { ProjectPicker } from './ProjectPicker'

export const pendingSends = new Map<string, PendingSend>()

const FADE = 96
const SEAM = 24

export function Dock({
  floating = false,
  above,
  onLayout,
  children,
}: {
  floating?: boolean
  above?: ReactNode
  onLayout?: (event: LayoutChangeEvent) => void
  children: ReactNode
}) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  const bottom = insets.bottom + 6
  if (!floating) {
    return (
      <View style={[styles.dock, { paddingBottom: bottom, backgroundColor: colors.bgPrimary }]}>
        {above}
        {children}
      </View>
    )
  }
  return (
    <View onLayout={onLayout} pointerEvents="box-none" style={[styles.dock, styles.floating, { paddingBottom: bottom }]}>
      {above ? <View style={styles.above}>{above}</View> : null}
      <View>
        <View pointerEvents="none" style={styles.fade}>
          <Svg width="100%" height="100%" preserveAspectRatio="none" viewBox="0 0 1 1">
            <Defs>
              <LinearGradient id="dockFade" x1="0" y1="0" x2="0" y2="1">
                <Stop offset="0" stopColor={colors.bgPrimary} stopOpacity={0} />
                <Stop offset={FADE / 2 / (FADE + SEAM)} stopColor={colors.bgPrimary} stopOpacity={0.15} />
                <Stop offset={FADE / (FADE + SEAM)} stopColor={colors.bgPrimary} stopOpacity={0.75} />
                <Stop offset="1" stopColor={colors.bgPrimary} stopOpacity={0.75} />
              </LinearGradient>
            </Defs>
            <Rect x="0" y="0" width="1" height="1" fill="url(#dockFade)" />
          </Svg>
        </View>
        <View pointerEvents="none" style={[styles.solid, { bottom: -bottom, backgroundColor: colors.bgPrimary }]} />
        {children}
      </View>
    </View>
  )
}

export function useModels(projectId: string, ready: boolean, providerId: string | null): ProjectModels | undefined {
  const session = useComputerLink()
  const models = useComputer().models[projectId]
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

export function useReferences(projectId: string, ready: boolean): ReferenceEntry[] {
  const session = useComputerLink()
  const state = useComputer()
  const models = state.models[projectId]
  const listable = ready && (models?.canListCommands === true || models?.canListSkills === true)
  useEffect(() => {
    if (listable) void session.loadReferences(projectId).catch(() => undefined)
  }, [listable, projectId, session])
  return state.references[projectId] ?? NO_REFERENCES
}

function needsStartOf(state: ComputerState, projectId: string): boolean {
  const project = projectById(state, projectId)
  return Boolean(project && !project.running && computerStatus(state) === 'online' && state.phases[projectId] !== 'cantStart')
}

export function useProjectStart(projectId: string | null) {
  const state = useComputer()
  const session = useComputerLink()
  const phase = projectId ? state.phases[projectId] : undefined
  const needsStart = projectId ? needsStartOf(state, projectId) : false
  useEffect(() => {
    if (projectId && needsStart && phase !== 'starting') void session.startProject(projectId)
  }, [needsStart, phase, projectId, session])
  return needsStart
}

export function animateLayout() {
  LayoutAnimation.configureNext(LayoutAnimation.create(220, LayoutAnimation.Types.easeInEaseOut, LayoutAnimation.Properties.opacity))
}

export function CompactComposer({ draft, disabled, onPress }: { draft: MessageDraft; disabled: boolean; onPress: () => void }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  const text = isEmptyDraft(draft) ? null : draftTitle(draft)
  return (
    <View style={[styles.compactBar, { paddingBottom: insets.bottom + 6, backgroundColor: colors.bgPrimary }]}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('home.newChat')}
        accessibilityState={{ disabled }}
        disabled={disabled}
        onPress={onPress}
        style={({ pressed }) => [
          styles.compact,
          { backgroundColor: colors.composerInputBackground, borderColor: pressed ? colors.borderActive : colors.composerInputBorder },
          disabled && styles.disabled,
        ]}
      >
        <View style={styles.plus}>
          <Icon name="plus" size={22} color={colors.textPrimary} />
        </View>
        <Text numberOfLines={1} style={[type.text, styles.compactText, { color: text ? colors.textPrimary : colors.composerPlaceholder }]}>
          {text ?? t('composer.placeholder')}
        </Text>
      </Pressable>
    </View>
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

export function NewChatPane({
  projectId,
  draft,
  overlay,
  onDraft,
  onPickProject,
  onCollapse,
  onCreated,
}: {
  projectId: string
  draft: MessageDraft
  overlay?: ReactNode
  onDraft: (draft: MessageDraft) => void
  onPickProject: (projectId: string) => void
  onCollapse: () => void
  onCreated: (key: string) => void
}) {
  const state = useComputer()
  const session = useComputerLink()
  const { t } = useI18n()
  const { colors } = useTheme()
  const [failed, setFailed] = useState(false)
  const [sending, setSending] = useState(false)
  const [picking, setPicking] = useState(false)
  const [choices, setChoices] = useState(UNTOUCHED)
  const [planMode, setPlanMode] = useState(false)
  const project = projectById(state, projectId)
  const phase = state.phases[projectId]
  const ready = phase === 'ready'
  const models = useModels(projectId, ready, choices.controls.providerId)
  const references = useReferences(projectId, ready)
  const computer = state.computer
  const status = computerStatus(state)
  const online = status === 'online'
  const needsStart = needsStartOf(state, projectId)

  useEffect(() => {
    const subscription = BackHandler.addEventListener('hardwareBackPress', () => {
      onCollapse()
      return true
    })
    return () => subscription.remove()
  }, [onCollapse])

  if (!project) return null
  const projectName = projectTitle(project, t)
  const providerId = choices.controls.providerId ?? models?.defaultProviderId ?? null
  const controls: ChatControls = { ...choices.controls, providerId, model: choices.controls.model ?? defaultModel(models, providerId) }
  const starting = sending ? !project.running : needsStart || phase === 'starting'
  const cantStart = failed || phase === 'cantStart'

  async function send(message: MessageDraft) {
    setSending(true)
    try {
      const config = startConfig({ touched: choices.touched, controls })
      const key = await session.newChat(projectId, draftTitle(message), planMode ? { ...config, mode: 'plan' } : config)
      pendingSends.set(key, { draft: message, sent: session.send(key, message) })
      onDraft(EMPTY_DRAFT)
      onCreated(key)
    } catch (error) {
      setFailed(error instanceof CantStartProjectError)
      throw error
    } finally {
      setSending(false)
    }
  }

  return (
    <>
      <KeyboardAvoidingView style={styles.fill} behavior="padding">
        <View style={styles.fill}>
          {starting ? (
            <MascotTransition line={t('project.starting', { project: projectName })} />
          ) : cantStart ? (
            <MascotNote moment="asleep">{t('project.cantStart', { computer: computer.name, project: projectName })}</MascotNote>
          ) : (
            <Pressable accessible={false} onPress={Keyboard.dismiss} style={styles.fill} />
          )}
          {overlay}
        </View>
        <Dock>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={`${t('picker.title')}: ${projectName}`}
            onPress={() => setPicking(true)}
            style={({ pressed }) => [styles.projectRow, pressed && { backgroundColor: colors.roundFill }]}
          >
            <Icon name={projectIcon(project)} size={18} color={colors.textSecondary} strokeWidth={1.8} />
            <Txt numberOfLines={1} style={styles.projectName}>
              {projectName}
            </Txt>
            <Icon name="chevronDown" size={16} color={colors.textSecondary} strokeWidth={2} />
          </Pressable>
          <Composer
            running={false}
            autoFocus
            initialDraft={draft}
            clearOnSend={false}
            onDraftChange={onDraft}
            canSend={online && !starting && !cantStart}
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
      <ProjectPicker
        state={state}
        visible={picking}
        onClose={() => setPicking(false)}
        onPick={(id) => {
          setPicking(false)
          if (id !== projectId) onPickProject(id)
        }}
      />
    </>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  dock: { gap: 10, paddingTop: 8, paddingHorizontal: metrics.gutter },
  floating: { position: 'absolute', left: 0, right: 0, bottom: 0 },
  above: { zIndex: 1 },
  fade: { position: 'absolute', left: -metrics.gutter, right: -metrics.gutter, top: -FADE, height: FADE + SEAM },
  solid: { position: 'absolute', left: -metrics.gutter, right: -metrics.gutter, top: SEAM },
  compactBar: { paddingTop: 10, paddingHorizontal: metrics.gutter },
  compact: { flexDirection: 'row', alignItems: 'center', gap: 6, minHeight: 56, paddingHorizontal: 8, borderWidth: 1, borderRadius: 26 },
  plus: { width: 40, height: 40, alignItems: 'center', justifyContent: 'center' },
  compactText: { flex: 1, minWidth: 0 },
  disabled: { opacity: 0.45 },
  projectRow: {
    alignSelf: 'flex-start',
    maxWidth: '100%',
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    paddingVertical: 6,
    paddingHorizontal: 10,
    borderRadius: metrics.rowRadius,
  },
  projectName: { flexShrink: 1, fontWeight: '500' },
})

import { memo, useEffect, useRef, useState } from 'react'
import { Animated, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import { copyableAssistantText, splitAssistantText } from '../../core/assistantContent'
import { workspaceFile } from '../../core/links'
import {
  baseName,
  type ToolEntry,
  type ToolIcon,
  type TaskWake,
  type TaskWakeKind,
  type ToolVerb,
  type TranscriptEntry,
} from '../../core/transcript'
import type { ToolGroupLabel, ToolGroupPart } from '../../core/toolGroups'
import type { UserSegment } from '../../core/userSegments'
import { useI18n, type I18n } from '../../i18n'
import type { MessageId } from '../../i18n/messages/en'
import { Icon, type IconName } from '../icons'
import { nativeDriver, Txt } from '../parts'
import { useReducedMotion } from '../motion'
import { RunningShimmer } from '../RunningShimmer'
import { metrics, type, useTheme } from '../theme'
import { TurnChangesCard } from './Changes'
import { ThinkingGlyph } from './ThinkingGlyph'
import { FileChip, SkillChip } from './Chips'
import { CopyButton } from './CopyButton'
import { ImageThumb, SentImage } from './Images'
import { Markdown } from './Markdown'
import { PlanCard } from './PlanCard'
import { FailureLine, OriginLine, SentAsGoal, SystemDivider, ThreadActionCard } from './TimelineMarkers'

const TOOL_ICON: Record<ToolIcon, IconName> = {
  terminal: 'squareTerminal',
  declined: 'shieldAlert',
  stopped: 'circleStop',
  read: 'bookOpen',
  search: 'search',
  folder: 'folder',
  edit: 'pencil',
  web: 'globe',
  agent: 'bot',
  other: 'wrench',
}

const TOOL_TEXT: Record<ToolVerb, MessageId> = {
  ran: 'tool.ran',
  edited: 'tool.edited',
  read: 'tool.read',
  searched: 'tool.searched',
  used: 'tool.used',
  backgroundStarted: 'tool.backgroundStarted',
  backgroundStopped: 'tool.backgroundStopped',
  spawned: 'tool.spawned',
  messaged: 'tool.messaged',
  listedAgents: 'tool.listedAgents',
  stoppedShell: 'tool.stoppedShell',
  stoppedAgent: 'tool.stoppedAgent',
  stoppedWorkflow: 'tool.stoppedWorkflow',
  stoppingShell: 'tool.stoppingShell',
  stoppingAgent: 'tool.stoppingAgent',
  stoppingWorkflow: 'tool.stoppingWorkflow',
}

const TOOL_LIVE_TEXT: Record<ToolVerb, MessageId> = {
  ...TOOL_TEXT,
  ran: 'tool.live.ran',
  edited: 'tool.live.edited',
  read: 'tool.live.read',
  searched: 'tool.live.searched',
  used: 'tool.live.used',
  spawned: 'tool.live.spawned',
  messaged: 'tool.live.messaged',
  listedAgents: 'tool.live.listedAgents',
}

const TOOL_PENDING_TEXT: Partial<Record<ToolVerb, MessageId>> = {
  ran: 'tool.running',
  edited: 'tool.editing',
  read: 'tool.reading',
  searched: 'tool.searching',
  listedAgents: 'tool.live.listedAgents',
  backgroundStarted: 'tool.backgroundStarted.generic',
  backgroundStopped: 'tool.backgroundStopped.generic',
  stoppedShell: 'tool.stoppedShell.generic',
  stoppedAgent: 'tool.stoppedAgent.generic',
  stoppedWorkflow: 'tool.stoppedWorkflow.generic',
  stoppingShell: 'tool.stoppingShell.generic',
  stoppingAgent: 'tool.stoppingAgent.generic',
  stoppingWorkflow: 'tool.stoppingWorkflow.generic',
}

export function toolText(verb: ToolVerb, subject: string | null, t: I18n['t']): string {
  const pending = TOOL_PENDING_TEXT[verb]
  if (subject === null && pending) return t(pending)
  return t(TOOL_TEXT[verb], { subject: subject ?? '' })
}

const PART_TEXT: Record<ToolGroupPart, readonly [MessageId, MessageId]> = {
  calledTool: ['toolGroup.calledTool.one', 'toolGroup.calledTool.other'],
  created: ['toolGroup.created.one', 'toolGroup.created.other'],
  edited: ['toolGroup.edited.one', 'toolGroup.edited.other'],
  explored: ['toolGroup.explored.one', 'toolGroup.explored.other'],
  ran: ['toolGroup.ran.one', 'toolGroup.ran.other'],
  searchedWeb: ['toolGroup.searchedWeb', 'toolGroup.searchedWeb'],
  fetched: ['toolGroup.fetched.one', 'toolGroup.fetched.other'],
}

function groupText(label: ToolGroupLabel, i18n: I18n): string {
  return i18n.phrases(label.map(({ part, many }) => i18n.t(PART_TEXT[part][many ? 1 : 0])))
}

function liveText(entry: ToolEntry, t: I18n['t']): string {
  const pending = entry.subject === null ? TOOL_PENDING_TEXT[entry.verb] : undefined
  return pending ? t(pending) : t(TOOL_LIVE_TEXT[entry.verb], { subject: entry.subject ?? '' })
}

const NOTICE_TEXT = {
  allowedOnce: 'notice.allowedOnce',
  allowedForSession: 'notice.allowedForSession',
  allowedAlways: 'notice.allowedAlways',
  rejected: 'notice.rejected',
  answered: 'notice.answered',
} as const satisfies Record<string, MessageId>

const WAKE_COPY: Record<TaskWakeKind, { icon: IconName; started: MessageId; generic: MessageId; statuses: Record<string, MessageId> }> = {
  terminal: {
    icon: 'squareTerminal',
    started: 'wake.command',
    generic: 'wake.command.generic',
    statuses: {
      completed: 'wake.command.finished',
      failed: 'wake.command.failed',
      killed: 'wake.command.stopped',
      waitingForInput: 'wake.command.waitingForInput',
    },
  },
  subagent: {
    icon: 'bot',
    started: 'wake.subagent',
    generic: 'wake.subagent.generic',
    statuses: { completed: 'wake.subagent.finished', failed: 'wake.subagent.failed', cancelled: 'wake.subagent.stopped' },
  },
  workflow: {
    icon: 'workflow',
    started: 'wake.workflow',
    generic: 'wake.workflow.generic',
    statuses: {
      succeeded: 'wake.workflow.finished',
      failed: 'wake.workflow.failed',
      cancelled: 'wake.workflow.failed',
      interrupted: 'wake.workflow.failed',
      stopped: 'wake.workflow.stopped',
    },
  },
}

function wakeText(origin: TaskWake, t: I18n['t']): string {
  const copy = WAKE_COPY[origin.task]
  if (!origin.label) return t(copy.generic)
  return t((origin.status && copy.statuses[origin.status]) || copy.started, { label: origin.label })
}

function Disclosure({ open }: { open: boolean }) {
  const { colors } = useTheme()
  return (
    <View style={{ transform: [{ rotate: open ? '90deg' : '0deg' }] }}>
      <Icon name="chevronRight" size={14} color={colors.textSecondary} />
    </View>
  )
}

const HOLD_MS = 2000

type ThinkingEntry = Extract<TranscriptEntry, { kind: 'thinking' }>

interface WorkingLabel {
  text: string
  live: boolean
}

interface ReasoningStage {
  id: string
  stage: 'live' | 'hold' | 'done'
}

function useWorkingLabel(entry: ThinkingEntry): WorkingLabel | null {
  const { t } = useI18n()
  const live = entry.working && entry.reasoning?.live ? entry.reasoning.id : null
  const [span, setSpan] = useState<ReasoningStage | null>(null)
  if (live && span?.id !== live) setSpan({ id: live, stage: 'live' })
  if (!live && span?.stage === 'live') setSpan({ id: span.id, stage: 'hold' })
  const startedAt = useRef(0)

  useEffect(() => {
    if (span?.stage === 'live') startedAt.current = Date.now()
    if (span?.stage === 'hold') {
      const timer = setTimeout(() => setSpan({ id: span.id, stage: 'done' }), Math.max(0, HOLD_MS - (Date.now() - startedAt.current)))
      return () => clearTimeout(timer)
    }
  }, [span])

  if (!entry.working) return null
  const thinking = { text: entry.heading ?? t('chat.thinking'), live: true }
  if (span?.stage === 'live' || span?.stage === 'hold') return thinking
  return entry.quiet ? thinking : null
}

export function useKeptWorkingLine(transcript: TranscriptEntry[], scope: string): TranscriptEntry[] {
  const last = transcript.at(-1)
  const [kept, setKept] = useState<{ scope: string; id: string } | null>(null)
  if (last?.kind === 'thinking' && (kept?.id !== last.id || kept.scope !== scope)) setKept({ scope, id: last.id })
  if (kept && (kept.scope !== scope || last?.kind === 'user')) setKept(null)
  if (!kept || kept.scope !== scope || !last || last.kind === 'thinking' || last.kind === 'user') return transcript
  return [...transcript, { kind: 'thinking', id: kept.id, working: false, heading: null, reasoning: null, quiet: false }]
}

function WorkingLine({ entry, tight }: { entry: ThinkingEntry; tight: boolean }) {
  const { colors } = useTheme()
  const reduced = useReducedMotion()
  const label = useWorkingLabel(entry)
  const [engaged, setEngaged] = useState(entry.working)
  const [faded, setFaded] = useState(!entry.working)
  if (entry.working && !engaged) setEngaged(true)
  if (entry.working && faded) setFaded(false)
  const [shown, setShown] = useState(label)
  if (label && (label.text !== shown?.text || label.live !== shown.live)) setShown(label)
  const labelled = label !== null
  const [labelOpacity] = useState(() => new Animated.Value(labelled ? 1 : 0))
  const [glyphOpacity] = useState(() => new Animated.Value(1))

  useEffect(() => {
    const target = labelled ? 1 : 0
    if (reduced) {
      labelOpacity.setValue(target)
      return
    }
    const animation = Animated.timing(labelOpacity, { toValue: target, duration: 180, useNativeDriver: nativeDriver })
    animation.start()
    return () => animation.stop()
  }, [labelled, labelOpacity, reduced])

  useEffect(() => {
    const working = entry.working
    if (reduced) {
      glyphOpacity.setValue(working ? 1 : 0)
      return
    }
    const animation = Animated.timing(glyphOpacity, { toValue: working ? 1 : 0, duration: 180, useNativeDriver: nativeDriver })
    animation.start(({ finished }) => {
      if (finished && !working) setFaded(true)
    })
    return () => animation.stop()
  }, [entry.working, glyphOpacity, reduced])

  if (!engaged) return null
  return (
    <View style={[styles.working, tight ? styles.tight : undefined]}>
      <Animated.View accessibilityElementsHidden importantForAccessibility="no-hide-descendants" style={{ opacity: glyphOpacity }}>
        <ThinkingGlyph thinking={!faded} color={colors.textSecondary} />
      </Animated.View>
      {shown ? (
        <Animated.View
          accessibilityLiveRegion={label ? 'polite' : 'none'}
          importantForAccessibility={label ? 'auto' : 'no-hide-descendants'}
          accessibilityElementsHidden={!label}
          style={[styles.shrink, { opacity: labelOpacity }]}
        >
          <RunningShimmer active={shown.live} textStyle={[type.text, styles.line, { color: colors.textSecondary }]}>
            {shown.text}
          </RunningShimmer>
        </Animated.View>
      ) : null}
    </View>
  )
}

export function useNow(ticking: boolean): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!ticking) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [ticking])
  return now
}

const ACTIVITY_TEXT = {
  working: ['activity.working', 'activity.workingFor'],
  worked: ['activity.worked', 'activity.workedFor'],
  stopped: ['activity.interrupted', 'activity.interruptedAfter'],
  youStopped: ['activity.youStopped', 'activity.youStoppedAfter'],
} as const satisfies Record<string, readonly [MessageId, MessageId]>

function Activity({ entry, workspacePath }: { entry: Extract<TranscriptEntry, { kind: 'activity' }>; workspacePath: string | null }) {
  const { t, duration } = useI18n()
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const working = entry.status === 'working'
  const now = useNow(working)
  const elapsed = (working ? now : Date.parse(entry.endedAt ?? '')) - Date.parse(entry.startedAt ?? '')
  const timed = Number.isFinite(elapsed) && elapsed >= (working ? 1000 : 0)
  const [plain, withDuration] = ACTIVITY_TEXT[entry.status]
  const label = timed ? t(withDuration, { duration: duration(elapsed) }) : t(plain)
  const expandable = entry.children.length > 0
  const origin = entry.origin ? wakeText(entry.origin, t) : null
  return (
    <View style={styles.activity}>
      <Pressable
        accessibilityLabel={origin ? `${label} · ${origin}` : undefined}
        accessibilityRole={expandable ? 'button' : undefined}
        accessibilityState={expandable ? { expanded: open } : undefined}
        disabled={!expandable}
        onPress={() => setOpen((value) => !value)}
        style={styles.activityRow}
      >
        {entry.origin ? <Icon name={WAKE_COPY[entry.origin.task].icon} size={14} color={colors.textSecondary} strokeWidth={1.8} /> : null}
        <Text numberOfLines={1} style={[type.text, styles.fixed, styles.tabular, { color: colors.textSecondary }]}>
          {label}
        </Text>
        {expandable ? <Disclosure open={open} /> : null}
        <View style={[styles.rule, { backgroundColor: colors.borderDefault }]} />
      </Pressable>
      {open
        ? entry.children.map((child, index) => (
            <TranscriptLine key={child.id} entry={child} previous={entry.children[index - 1]} workspacePath={workspacePath} />
          ))
        : null}
    </View>
  )
}

function ToolGroup({ entry }: { entry: Extract<TranscriptEntry, { kind: 'toolGroup' }> }) {
  const i18n = useI18n()
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const live = entry.active ? entry.children.findLast((child) => child.live) : undefined
  const label = live ? liveText(live, i18n.t) : groupText(entry.label, i18n)
  const tone = entry.failed && !live ? colors.errorText : colors.textSecondary
  return (
    <View style={styles.toolBlock}>
      <Pressable accessibilityRole="button" accessibilityState={{ expanded: open }} onPress={() => setOpen((value) => !value)} style={styles.tool}>
        <Icon name={TOOL_ICON[live?.icon ?? entry.icon]} size={16} color={tone} strokeWidth={1.6} />
        <RunningShimmer active={live !== undefined} style={styles.shrink} textStyle={[type.text, styles.line, { color: tone }]}>
          {label}
        </RunningShimmer>
        <Disclosure open={open} />
      </Pressable>
      {open ? (
        <View style={styles.groupChildren}>
          {entry.children.map((child) => (
            <ToolLine key={child.id} entry={child} />
          ))}
        </View>
      ) : null}
    </View>
  )
}

function ToolDetails({ entry }: { entry: ToolEntry }) {
  const { colors } = useTheme()
  return (
    <View style={styles.details}>
      {entry.details.map((detail, index) => (
        <View key={index} style={[styles.detail, { backgroundColor: colors.bgTertiary }]}>
          <Text selectable style={[type.code, styles.detailText, styles.detailTarget, { color: colors.textPrimary }]}>
            {detail.target}
          </Text>
          {detail.output ? (
            <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.detailOutput}>
              <Text selectable numberOfLines={24} style={[type.code, styles.detailText, { color: colors.textSecondary }]}>
                {detail.output.trimEnd()}
              </Text>
            </ScrollView>
          ) : null}
        </View>
      ))}
    </View>
  )
}

function Elapsed({ since }: { since: string }) {
  const { colors } = useTheme()
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 100)
    return () => clearInterval(timer)
  }, [])
  const start = Date.parse(since)
  const seconds = Number.isFinite(start) ? Math.max(0, now - start) / 1000 : 0
  return <Text style={[type.text, styles.fixed, styles.tabular, { color: colors.textDimmed }]}>{`${seconds.toFixed(1)}s`}</Text>
}

function ToolLine({ entry }: { entry: ToolEntry }) {
  const { t, around } = useI18n()
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const textStyle = [type.text, styles.line, { color: colors.textSecondary }]
  const pendingText = entry.subject === null ? TOOL_PENDING_TEXT[entry.verb] : undefined
  const subject = entry.subject
  const [before, after] = around((entry.live ? TOOL_LIVE_TEXT : TOOL_TEXT)[entry.verb], 'subject')
  return (
    <View style={styles.toolBlock}>
      <Pressable
        accessibilityRole="button"
        accessibilityState={{ expanded: open }}
        disabled={entry.details.length === 0}
        onPress={() => setOpen((value) => !value)}
        style={styles.tool}
      >
        <Icon
          name={TOOL_ICON[entry.icon]}
          size={16}
          color={entry.icon === 'declined' ? colors.warningText : colors.textSecondary}
          strokeWidth={1.6}
        />
        <RunningShimmer active={entry.live} style={styles.shrink} textStyle={textStyle}>
          {pendingText ? (
            t(pendingText)
          ) : (
            <>
              {before}
              {subject}
              {after}
              {entry.added !== undefined ? <Text style={{ color: colors.successText }}>{` +${entry.added}`}</Text> : null}
              {entry.removed !== undefined ? <Text style={{ color: colors.errorText }}>{` −${entry.removed}`}</Text> : null}
            </>
          )}
        </RunningShimmer>
        {entry.live ? <Elapsed since={entry.startedAt} /> : null}
      </Pressable>
      {open ? <ToolDetails entry={entry} /> : null}
      {entry.images.length > 0 ? (
        <View style={styles.images}>
          {entry.images.map((uri, index) => (
            <ImageThumb key={index} uri={uri} label={t('image.open')} style={styles.toolImage} />
          ))}
        </View>
      ) : null}
    </View>
  )
}

function interactiveEntry(
  message: Extract<TranscriptEntry, { kind: 'assistant' }>,
  source: string,
  index: number,
  t: I18n['t'],
): ToolEntry {
  return {
    kind: 'tool',
    id: `${message.id}:interactive:${index}`,
    verb: 'used',
    icon: 'other',
    subject: t('tool.interactiveContent'),
    details: [{ target: 'dotcraft-ui', output: source }],
    images: [],
    startedAt: message.at,
    category: null,
    web: null,
    path: '',
    created: false,
    live: false,
    failed: false,
  }
}

function GeneratedImage({ entry }: { entry: Extract<TranscriptEntry, { kind: 'image' }> }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  if (entry.status === 'failed' || (entry.status === 'completed' && !entry.uri && !entry.dropped)) {
    return (
      <Txt tone="error" accessibilityRole="alert">
        {entry.status === 'failed' ? (entry.error ?? t('image.failed')) : t('image.noData')}
      </Txt>
    )
  }
  const running = entry.status === 'inProgress'
  return (
    <View style={styles.generated} accessibilityLiveRegion={running ? 'polite' : 'none'}>
      <View style={styles.tool}>
        <Icon name="image" size={16} color={colors.textSecondary} strokeWidth={1.6} />
        <RunningShimmer active={running} style={styles.shrink} textStyle={[type.text, { color: colors.textSecondary }]}>
          {running ? t('image.generating') : t('image.generated')}
        </RunningShimmer>
      </View>
      {entry.uri ? (
        <ImageThumb uri={entry.uri} label={t('image.open')} style={styles.generatedImage} />
      ) : (
        <View style={[styles.generatedImage, styles.placeholder, { backgroundColor: colors.bgTertiary }]} />
      )}
    </View>
  )
}

function UserText({ segments, workspacePath }: { segments: UserSegment[]; workspacePath: string | null }) {
  return (
    <>
      {segments.map((segment, index) =>
        segment.type === 'text' ? (
          segment.value
        ) : (
          segment.type === 'file' ? (
            <FileChip key={index} path={workspaceFile(segment.path, workspacePath)} label={baseName(segment.display)} />
          ) : (
            <SkillChip key={index} name={segment.name} />
          )
        ),
      )}
    </>
  )
}

export const TranscriptLine = memo(function TranscriptLine({
  entry,
  previous,
  workspacePath,
}: {
  entry: TranscriptEntry
  previous?: TranscriptEntry
  workspacePath: string | null
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const toolish = (value: TranscriptEntry | undefined) => value?.kind === 'tool' || value?.kind === 'toolGroup'
  const tight = toolish(entry) && toolish(previous)
  switch (entry.kind) {
    case 'user':
      return (
        <View style={styles.user}>
          {entry.origin ? (
            <OriginLine origin={entry.origin} />
          ) : entry.added ? (
            <View style={styles.origin}>
              <Icon name="cornerDownRight" size={13} color={colors.textDimmed} />
              <Txt variant="caption" tone="dimmed">
                {t('chat.addedToTurn')}
              </Txt>
            </View>
          ) : null}
          {entry.images.length > 0 ? (
            <View style={styles.photos}>
              {entry.images.map((source, index) => (
                <SentImage key={index} source={source} label={t('image.open')} style={styles.photo} />
              ))}
            </View>
          ) : null}
          {entry.segments.length > 0 ? (
            <View style={[styles.bubble, { backgroundColor: colors.userMessageBg }]}>
              <Txt selectable style={styles.line}>
                <UserText segments={entry.segments} workspacePath={workspacePath} />
              </Txt>
            </View>
          ) : null}
          {entry.goal ? <SentAsGoal /> : null}
        </View>
      )
    case 'assistant':
      return (
        <View accessibilityLiveRegion={entry.streaming ? 'polite' : 'none'}>
          <View style={styles.assistant}>
            {splitAssistantText(entry.text, entry.streaming).map((part, index) =>
              part.kind === 'markdown' ? (
                <Markdown key={index} text={part.text} workspacePath={workspacePath} />
              ) : (
                <ToolLine key={index} entry={interactiveEntry(entry, part.source, index, t)} />
              ),
            )}
          </View>
          {entry.changes ? (
            <View style={styles.changes}>
              <TurnChangesCard changes={entry.changes} />
            </View>
          ) : null}
          {entry.copy ? <CopyButton text={copyableAssistantText(entry.text)} label={t('chat.copy')} style={styles.copy} /> : null}
        </View>
      )
    case 'thinking':
      return <WorkingLine entry={entry} tight={toolish(previous)} />
    case 'tool':
      return (
        <View style={tight ? styles.tight : undefined}>
          <ToolLine entry={entry} />
        </View>
      )
    case 'toolGroup':
      return (
        <View style={tight ? styles.tight : undefined}>
          <ToolGroup entry={entry} />
        </View>
      )
    case 'activity':
      return <Activity entry={entry} workspacePath={workspacePath} />
    case 'image':
      return <GeneratedImage entry={entry} />
    case 'plan':
      return <PlanCard entry={entry} workspacePath={workspacePath} />
    case 'changes':
      return <TurnChangesCard changes={entry.changes} />
    case 'divider':
      return <SystemDivider entry={entry} />
    case 'threadAction':
      return <ThreadActionCard action={entry.action} />
    case 'failure':
      return <FailureLine entry={entry} />
    case 'notice':
      return (
        <View style={styles.process}>
          <Txt tone="secondary" style={styles.fixed}>
            {t(NOTICE_TEXT[entry.notice])}
          </Txt>
          {entry.detail ? (
            <Txt tone="dimmed" numberOfLines={1} style={styles.shrink}>
              {`· ${entry.detail}`}
            </Txt>
          ) : null}
        </View>
      )
  }
})

const styles = StyleSheet.create({
  shrink: { flexShrink: 1 },
  fixed: { flexShrink: 0 },
  tabular: { fontVariant: ['tabular-nums'] },
  line: { lineHeight: 22 },
  user: { alignItems: 'flex-end', gap: 4 },
  assistant: { gap: 12 },
  origin: { flexDirection: 'row', alignItems: 'center', gap: 4 },
  photos: { flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 6, maxWidth: '84%' },
  photo: { width: 112, maxHeight: 160 },
  bubble: { maxWidth: '84%', paddingVertical: 9, paddingHorizontal: 14, borderRadius: 20 },
  activity: { gap: 14 },
  activityRow: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  rule: { flex: 1, height: StyleSheet.hairlineWidth, marginLeft: 4 },
  groupChildren: { gap: 8, paddingLeft: 22 },
  toolBlock: { gap: 8 },
  tool: { flexDirection: 'row', alignItems: 'center', gap: 6, minWidth: 0 },
  details: { gap: 6 },
  detail: { borderRadius: 10, overflow: 'hidden' },
  detailTarget: { padding: 10 },
  detailOutput: { paddingHorizontal: 10, paddingBottom: 10 },
  detailText: { lineHeight: 19 },
  images: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  toolImage: { width: 160 },
  generated: { gap: 8 },
  generatedImage: { width: '100%', maxWidth: 320 },
  placeholder: { aspectRatio: 1, borderRadius: 12 },
  changes: { marginTop: 10 },
  copy: { alignSelf: 'flex-start', marginTop: 4, marginLeft: -6 },
  tight: { marginTop: -6 },
  working: { flexDirection: 'row', alignItems: 'center', gap: 6, minHeight: 22 },
  process: { flexDirection: 'row', gap: 6, minWidth: 0 },
  failure: { flexDirection: 'row', gap: 12, padding: 14, borderWidth: 1, borderRadius: metrics.noticeRadius },
  failureText: { flex: 1, minWidth: 0, gap: 2 },
  strong: { fontWeight: '600' },
})

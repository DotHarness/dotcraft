import { useState } from 'react'
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import type { BackgroundTask } from '../../core/backgroundTasks'
import { useI18n, type I18n } from '../../i18n'
import type { MessageId } from '../../i18n/messages/en'
import { CircleStopGlyph, Icon, type IconName } from '../icons'
import { Txt } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { metrics, type, useTheme } from '../theme'
import { useNow } from './Transcript'

const KIND: Record<BackgroundTask['kind'], { icon: IconName; label: MessageId }> = {
  shell: { icon: 'squareTerminal', label: 'tasks.kind.shell' },
  agent: { icon: 'bot', label: 'tasks.kind.agent' },
  workflow: { icon: 'workflow', label: 'tasks.kind.workflow' },
}

export function runningTasksLabel(t: I18n['t'], count: number): string {
  return t(count === 1 ? 'tasks.runningOne' : 'tasks.runningMany', { count })
}

export function TasksRow({ count, onPress }: { count: number; onPress: () => void }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const label = runningTasksLabel(t, count)
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={onPress}
      hitSlop={6}
      style={({ pressed }) => [styles.row, pressed && styles.pressed]}
    >
      <Icon name="activity" size={16} color={colors.accent} />
      <Text style={[type.text, { color: colors.accent }]}>{label}</Text>
    </Pressable>
  )
}

function elapsed(task: BackgroundTask, now: number): number | null {
  const start = Date.parse(task.startedAt ?? '')
  const end = task.status === 'running' ? now : Date.parse(task.endedAt ?? '')
  return Number.isFinite(start) && Number.isFinite(end) ? end - start : null
}

function TaskDetail({ task }: { task: BackgroundTask }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  if (task.kind === 'shell') {
    return task.output ? (
      <ScrollView horizontal showsHorizontalScrollIndicator={false} style={[styles.output, { backgroundColor: colors.bgTertiary }]} contentContainerStyle={styles.outputContent}>
        <Text selectable style={[type.code, styles.outputText, { color: colors.textSecondary }]}>
          {task.output}
        </Text>
      </ScrollView>
    ) : (
      <Txt variant="meta" tone="dimmed">
        {t('tasks.noOutput')}
      </Txt>
    )
  }
  const text = task.kind === 'agent' ? task.role : t(task.agents === 1 ? 'tasks.agentsOne' : 'tasks.agentsMany', { count: task.agents })
  return text ? (
    <Txt variant="meta" tone="secondary">
      {text}
    </Txt>
  ) : null
}

function TaskCard({ task, now, onStop }: { task: BackgroundTask; now: number; onStop: (task: BackgroundTask) => Promise<void> }) {
  const { t, duration } = useI18n()
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const [stopping, setStopping] = useState(false)
  const [failed, setFailed] = useState(false)
  const running = task.status === 'running'
  const span = elapsed(task, now)
  const meta = [
    t(KIND[task.kind].label),
    ...(span !== null ? [duration(span)] : []),
    ...(task.status === 'stopped' ? [t('tasks.stopped')] : task.status === 'failed' ? [t('state.failed')] : []),
  ].join(' · ')
  const stop = () => {
    setStopping(true)
    setFailed(false)
    onStop(task).then(
      () => setStopping(false),
      () => {
        setStopping(false)
        setFailed(true)
      },
    )
  }
  const body = (
    <>
      <View style={styles.kind}>
        <Icon name={KIND[task.kind].icon} size={18} color={colors.textSecondary} strokeWidth={1.6} />
      </View>
      <View style={styles.cardText}>
        <Txt style={styles.title}>{task.title}</Txt>
        <Txt variant="meta" tone={task.status === 'failed' ? 'error' : 'secondary'}>
          {meta}
        </Txt>
      </View>
    </>
  )
  return (
    <View style={[styles.card, { backgroundColor: colors.bgSecondary, borderColor: colors.borderDefault }]}>
      {running ? (
        <View style={styles.cardHead}>
          {body}
          {task.stop ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={t('tasks.stop', { task: task.title })}
              accessibilityState={{ busy: stopping }}
              disabled={stopping}
              hitSlop={6}
              onPress={stop}
              style={({ pressed }) => [styles.stop, pressed && { backgroundColor: colors.roundFillPressed }, stopping && styles.pressed]}
            >
              <CircleStopGlyph size={22} color={colors.textPrimary} />
            </Pressable>
          ) : null}
        </View>
      ) : (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={`${task.title}, ${meta}`}
          accessibilityState={{ expanded: open }}
          onPress={() => setOpen((value) => !value)}
          style={styles.cardHead}
        >
          {body}
          <View style={[styles.chevron, { transform: [{ rotate: open ? '90deg' : '0deg' }] }]}>
            <Icon name="chevronRight" size={16} color={colors.textDimmed} />
          </View>
        </Pressable>
      )}
      {failed ? (
        <Txt variant="meta" tone="error" accessibilityRole="alert" style={styles.inset}>
          {t('tasks.stopFailed')}
        </Txt>
      ) : null}
      {open ? (
        <View style={styles.inset}>
          <TaskDetail task={task} />
        </View>
      ) : null}
    </View>
  )
}

function Section({
  title,
  count,
  initiallyOpen,
  tasks,
  now,
  onStop,
}: {
  title: string
  count?: number
  initiallyOpen: boolean
  tasks: BackgroundTask[]
  now: number
  onStop: (task: BackgroundTask) => Promise<void>
}) {
  const { colors } = useTheme()
  const [open, setOpen] = useState(initiallyOpen)
  if (tasks.length === 0) return null
  return (
    <View style={styles.section}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={count === undefined ? title : `${title} ${count}`}
        accessibilityState={{ expanded: open }}
        onPress={() => setOpen((value) => !value)}
        style={styles.sectionHead}
      >
        <Txt accessibilityRole="header" style={styles.sectionTitle}>
          {title}
        </Txt>
        {count !== undefined ? <Txt tone="dimmed">{count}</Txt> : null}
        <View style={{ transform: [{ rotate: open ? '0deg' : '-90deg' }] }}>
          <Icon name="chevronDown" size={16} color={colors.textSecondary} />
        </View>
      </Pressable>
      {open ? tasks.map((task) => <TaskCard key={`${task.kind}:${task.id}`} task={task} now={now} onStop={onStop} />) : null}
    </View>
  )
}

export function TasksSheet({
  visible,
  running,
  completed,
  onClose,
  onStop,
}: {
  visible: boolean
  running: BackgroundTask[]
  completed: BackgroundTask[]
  onClose: () => void
  onStop: (task: BackgroundTask) => Promise<void>
}) {
  const { t } = useI18n()
  const now = useNow(visible && running.length > 0)
  return (
    <SheetLayer visible={visible} onClose={onClose}>
      <SheetHeader title={t('tasks.title')} />
      <Section title={t('tasks.runningSection')} initiallyOpen tasks={running} now={now} onStop={onStop} />
      <Section title={t('tasks.completedSection')} count={completed.length} initiallyOpen={false} tasks={completed} now={now} onStop={onStop} />
    </SheetLayer>
  )
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 6, alignSelf: 'flex-start', minHeight: 32 },
  pressed: { opacity: 0.6 },
  section: { gap: 8 },
  sectionHead: { flexDirection: 'row', alignItems: 'center', gap: 6, minHeight: metrics.touch },
  sectionTitle: { fontWeight: '600' },
  card: { borderWidth: 1, borderRadius: metrics.noticeRadius, paddingVertical: 12, paddingHorizontal: 12, gap: 10 },
  cardHead: { flexDirection: 'row', alignItems: 'flex-start', gap: 10 },
  kind: { paddingTop: 1 },
  cardText: { flex: 1, minWidth: 0, gap: 2 },
  title: { fontWeight: '600' },
  stop: { width: 36, height: 36, marginTop: -6, marginRight: -6, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
  chevron: { paddingTop: 2 },
  inset: { marginLeft: 28 },
  output: { borderRadius: 8 },
  outputContent: { padding: 10 },
  outputText: { lineHeight: 19 },
})

import { useMemo, useState } from 'react'
import { Pressable, StyleSheet, View } from 'react-native'
import Svg, { Defs, LinearGradient, Rect, Stop } from 'react-native-svg'
import type { PlanTodo, TranscriptEntry } from '../../core/transcript'
import { useI18n } from '../../i18n'
import { Icon, type IconName } from '../icons'
import { PhoneButton, Txt } from '../parts'
import { useTheme } from '../theme'
import { CopyButton } from './CopyButton'
import { Markdown } from './Markdown'

type PlanEntry = Extract<TranscriptEntry, { kind: 'plan' }>

const PREVIEW_HEIGHT = 220

const STATUS_ICONS: Record<PlanTodo['status'], IconName> = {
  pending: 'circle',
  in_progress: 'circleDot',
  completed: 'circleCheck',
  cancelled: 'circleX',
}

function copyText(entry: PlanEntry, title: string): string {
  if (entry.content.trim()) return entry.content
  return [title.trim() && `# ${title.trim()}`, entry.overview.trim(), entry.todos.map((todo) => `- ${todo.content}`).join('\n')]
    .filter(Boolean)
    .join('\n\n')
}

function Fade() {
  const { colors } = useTheme()
  return (
    <Svg pointerEvents="none" style={styles.fade} width="100%" height="100%">
      <Defs>
        <LinearGradient id="plan-fade" x1="0" y1="0" x2="0" y2="1">
          <Stop offset="0.65" stopColor={colors.bgSecondary} stopOpacity={0} />
          <Stop offset="1" stopColor={colors.bgSecondary} stopOpacity={1} />
        </LinearGradient>
      </Defs>
      <Rect width="100%" height="100%" fill="url(#plan-fade)" />
    </Svg>
  )
}

function Todos({ todos }: { todos: PlanTodo[] }) {
  const { colors } = useTheme()
  return (
    <View style={styles.todos}>
      {todos.map((todo, index) => {
        const cancelled = todo.status === 'cancelled'
        return (
          <View key={index} style={styles.todo}>
            <View style={styles.todoIcon}>
              <Icon
                name={STATUS_ICONS[todo.status]}
                size={15}
                color={todo.status === 'pending' || cancelled ? colors.textDimmed : colors.textSecondary}
              />
            </View>
            <Txt tone={cancelled ? 'dimmed' : 'primary'} style={[styles.shrink, cancelled && styles.struck]}>
              {todo.content}
            </Txt>
          </View>
        )
      })}
    </View>
  )
}

export function PlanCard({ entry, workspacePath }: { entry: PlanEntry; workspacePath: string | null }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [expanded, setExpanded] = useState(false)
  const title = entry.title.trim() || t('plan.badge')
  const copy = useMemo(() => copyText(entry, title), [entry, title])
  const hasContent = entry.content.trim().length > 0
  const canExpand = hasContent || entry.todos.length > 0
  const toggleLabel = t(expanded ? 'plan.collapse' : 'plan.expand')

  return (
    <View style={[styles.card, { borderColor: colors.borderDefault, backgroundColor: colors.bgSecondary }]}>
      <View style={styles.head}>
        <View style={styles.badge}>
          <Icon name="lightbulb" size={14} color={colors.textSecondary} />
          <Txt variant="meta" tone="secondary" style={styles.medium}>
            {t('plan.badge')}
          </Txt>
        </View>
        <View style={styles.actions}>
          {copy ? <CopyButton text={copy} label={t('plan.copy')} /> : null}
          {canExpand ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={toggleLabel}
              accessibilityState={{ expanded }}
              hitSlop={6}
              onPress={() => setExpanded((value) => !value)}
              style={({ pressed }) => [styles.toggle, pressed && { backgroundColor: colors.roundFill }]}
            >
              <View style={expanded && styles.flipped}>
                <Icon name="chevronDown" size={16} color={colors.textDimmed} />
              </View>
            </Pressable>
          ) : null}
        </View>
      </View>
      <Txt accessibilityRole="header" style={styles.title}>
        {title}
      </Txt>
      {!hasContent && entry.overview.trim() ? <Txt tone="secondary">{entry.overview}</Txt> : null}
      {hasContent ? (
        <View style={!expanded && styles.collapsed}>
          <View style={!expanded && styles.preview}>
            <Markdown text={entry.content} workspacePath={workspacePath} />
            {expanded ? null : <Fade />}
          </View>
          {expanded ? null : (
            <View style={styles.expand}>
              <PhoneButton compact onPress={() => setExpanded(true)}>
                {t('plan.expand')}
              </PhoneButton>
            </View>
          )}
        </View>
      ) : null}
      {expanded && entry.todos.length > 0 ? <Todos todos={entry.todos} /> : null}
    </View>
  )
}

const styles = StyleSheet.create({
  shrink: { flexShrink: 1 },
  medium: { fontWeight: '500' },
  card: { gap: 8, padding: 12, borderWidth: 1, borderRadius: 8 },
  head: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  badge: { flexDirection: 'row', alignItems: 'center', gap: 5, flexShrink: 1 },
  actions: { flexDirection: 'row', alignItems: 'center', gap: 2 },
  toggle: { padding: 6, borderRadius: 8 },
  flipped: { transform: [{ rotate: '180deg' }] },
  title: { fontWeight: '600', lineHeight: 22 },
  collapsed: { paddingBottom: 38 },
  preview: { maxHeight: PREVIEW_HEIGHT, overflow: 'hidden' },
  fade: { position: 'absolute', top: 0, left: 0, right: 0, bottom: 0 },
  expand: { position: 'absolute', left: 0, right: 0, bottom: 0, alignItems: 'center' },
  todos: { gap: 6, marginTop: 4 },
  todo: { flexDirection: 'row', alignItems: 'flex-start', gap: 8 },
  todoIcon: { height: 20, justifyContent: 'center' },
  struck: { textDecorationLine: 'line-through' },
})

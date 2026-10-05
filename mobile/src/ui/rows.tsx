import type { ReactNode } from 'react'
import { Pressable, StyleSheet, View } from 'react-native'
import { stateOf, type ChatSummary, type ProjectInfo } from '../core/state'
import { useI18n, type I18n } from '../i18n'
import { Icon, type IconName } from './icons'
import { StateMark, STATE_LABEL, Txt } from './parts'
import { metrics, useTheme } from './theme'

export function projectTitle(project: ProjectInfo, t: I18n['t']): string {
  return project.isChats ? t('project.chats') : project.name
}

export function projectIcon(project: ProjectInfo): IconName {
  return project.isChats ? 'messagesSquare' : 'folder'
}

export function Row({
  onPress,
  lead,
  title,
  meta,
  trail,
  single,
}: {
  onPress?: () => void
  lead?: ReactNode
  title: string
  meta?: string
  trail?: ReactNode
  single?: boolean
}) {
  const { colors } = useTheme()
  const content = (
    <>
      {lead ? <View style={styles.lead}>{lead}</View> : null}
      <View style={styles.text}>
        <Txt numberOfLines={1}>{title}</Txt>
        {meta ? (
          <Txt variant="meta" tone="secondary" numberOfLines={1}>
            {meta}
          </Txt>
        ) : null}
      </View>
      {trail ? <View style={styles.trail}>{trail}</View> : null}
    </>
  )
  if (!onPress) return <View style={[styles.row, single && styles.single]}>{content}</View>
  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={({ pressed }) => [styles.row, single && styles.single, pressed && { backgroundColor: colors.bgTertiary }]}
    >
      {content}
    </Pressable>
  )
}

export function chatTitle(chat: ChatSummary, untitled: string): string {
  return chat.title?.trim() || untitled
}

export function ChatRow({
  chat,
  live,
  projectName,
  onPress,
}: {
  chat: ChatSummary
  live: boolean
  projectName: string | null
  onPress: () => void
}) {
  const { t, ago } = useI18n()
  const state = stateOf(chat)
  const status = state === 'done' ? ago(chat.updatedAt) : t(STATE_LABEL[state])
  return (
    <Row
      onPress={onPress}
      title={chatTitle(chat, t('chat.untitled'))}
      meta={projectName ? `${projectName} · ${status}` : status}
      trail={<StateMark state={state} live={live} />}
    />
  )
}

export function ProjectRow({ project, meta, onPress }: { project: ProjectInfo; meta?: string; onPress: () => void }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const note = meta ?? (project.running ? undefined : t('project.notRunning'))
  return (
    <Row
      onPress={onPress}
      single={!note}
      lead={<Icon name={projectIcon(project)} size={20} color={colors.textSecondary} strokeWidth={1.7} />}
      title={projectTitle(project, t)}
      meta={note}
    />
  )
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    minHeight: 58,
    marginHorizontal: -8,
    padding: 8,
    borderRadius: metrics.rowRadius,
  },
  single: { minHeight: 48 },
  lead: { width: 24, alignItems: 'center' },
  text: { flex: 1, minWidth: 0, gap: 1 },
  trail: { flexDirection: 'row', alignItems: 'center', gap: 6 },
})

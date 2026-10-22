import { createContext, useContext, useEffect, useEffectEvent, useRef, useState } from 'react'
import { Pressable, StyleSheet, Text, View } from 'react-native'
import type { DividerKind, MessageOrigin, OriginMark, ThreadAction, TranscriptEntry } from '../../core/transcript'
import type { ChatSignals } from '../../core/state'
import { useI18n } from '../../i18n'
import type { MessageId } from '../../i18n/messages/en'
import { Icon, type IconName } from '../icons'
import { Txt } from '../parts'
import { RunningShimmer } from '../RunningShimmer'
import { type, useTheme } from '../theme'
import { CopyButton } from './CopyButton'

export interface ThreadLinks {
  open: (threadId: string) => void
  title: (threadId: string) => string | null
}

export const ThreadLinksContext = createContext<ThreadLinks | null>(null)

export const RetryContext = createContext<(() => Promise<void>) | null>(null)

const DIVIDERS: Record<DividerKind, { icon: IconName; text: MessageId }> = {
  forked: { icon: 'gitFork', text: 'divider.forked' },
  remoteConnected: { icon: 'plugZap', text: 'divider.remoteConnected' },
  remoteLocal: { icon: 'monitor', text: 'divider.remoteLocal' },
  remoteDisconnected: { icon: 'unplug', text: 'divider.remoteDisconnected' },
  channelBound: { icon: 'messageSquareShare', text: 'divider.channelBound' },
  channelUnbound: { icon: 'messageSquareOff', text: 'divider.channelUnbound' },
  compactedAuto: { icon: 'chevronsDown', text: 'divider.compactedAuto' },
  compactedManual: { icon: 'chevronsDown', text: 'divider.compactedManual' },
  compactedReactive: { icon: 'chevronsDown', text: 'divider.compactedReactive' },
  usageLimit: { icon: 'info', text: 'divider.usageLimit' },
}

const ORIGINS: Record<MessageOrigin, { icon: IconName; text: MessageId }> = {
  workflow: { icon: 'workflow', text: 'chat.sentFromWorkflow' },
  automation: { icon: 'bot', text: 'origin.automation' },
  app: { icon: 'bot', text: 'origin.app' },
  goal: { icon: 'target', text: 'origin.goal' },
  thread: { icon: 'messagesSquare', text: 'origin.thread' },
  subagent: { icon: 'messagesSquare', text: 'origin.subagent' },
}

function NoticeDivider({ icon, title, active = false }: { icon: IconName; title: string; active?: boolean }) {
  const { colors } = useTheme()
  const line = [styles.line, { backgroundColor: colors.borderDefault }]
  return (
    <View accessible accessibilityLabel={title} accessibilityLiveRegion={active ? 'polite' : 'none'} style={styles.divider}>
      <View style={line} />
      <View style={styles.label}>
        <Icon name={icon} size={14} color={colors.textSecondary} />
        <RunningShimmer active={active} style={styles.shrink} textStyle={[type.text, { color: colors.textSecondary }]}>
          {title}
        </RunningShimmer>
      </View>
      <View style={line} />
    </View>
  )
}

export function SystemDivider({ entry }: { entry: Extract<TranscriptEntry, { kind: 'divider' }> }) {
  const { t } = useI18n()
  const copy = DIVIDERS[entry.divider]
  const text = entry.divider === 'channelBound' && entry.target ? 'divider.channelBoundTarget' : copy.text
  return <NoticeDivider icon={copy.icon} title={t(text, { host: entry.host ?? '', channel: entry.channel ?? '', target: entry.target ?? '' })} />
}

export function LiveSignals({ signals }: { signals: ChatSignals }) {
  const { t } = useI18n()
  return (
    <>
      {signals.compacting ? (
        <NoticeDivider icon="chevronsDown" title={t(signals.compacting === 'manual' ? 'status.compactingManual' : 'status.compacting')} active />
      ) : null}
      {signals.retry ? (
        <NoticeDivider
          icon="refreshCw"
          title={t(signals.retry.busy ? 'status.serverBusy' : 'status.reconnecting', { attempt: signals.retry.attempt, max: signals.retry.max })}
          active
        />
      ) : null}
    </>
  )
}

export function OriginLine({ origin }: { origin: OriginMark }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const links = useContext(ThreadLinksContext)
  const copy = ORIGINS[origin.kind]
  const badge = t(copy.text)
  const label = origin.label || (origin.threadId ? (links?.title(origin.threadId) ?? '') : '')
  const threadId = origin.threadId
  const content = (
    <>
      <Icon name={copy.icon} size={13} color={colors.textDimmed} />
      <Txt variant="caption" tone="dimmed">
        {badge}
      </Txt>
    </>
  )
  const accessibilityLabel = label ? `${badge} · ${label}` : badge
  if (!threadId || !links) {
    return (
      <View accessible accessibilityLabel={accessibilityLabel} style={styles.origin}>
        {content}
      </View>
    )
  }
  return (
    <Pressable accessibilityRole="link" accessibilityLabel={accessibilityLabel} hitSlop={8} onPress={() => links.open(threadId)} style={styles.origin}>
      {content}
    </Pressable>
  )
}

export function SentAsGoal() {
  const { t } = useI18n()
  const { colors } = useTheme()
  return (
    <View style={styles.origin}>
      <Icon name="target" size={12} color={colors.textDimmed} />
      <Txt variant="caption" tone="dimmed">
        {t('chat.sentAsGoal')}
      </Txt>
    </View>
  )
}

export function ThreadActionCard({ action }: { action: ThreadAction }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const links = useContext(ThreadLinksContext)
  const title = t(action.kind === 'created' ? 'threadAction.created' : 'threadAction.messaged')
  const name = links?.title(action.threadId) ?? action.name ?? t('threadAction.untitled')
  const subtitle = action.queued ? t('threadAction.queued') : name
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${title} · ${name}`}
      accessibilityHint={t('threadAction.open')}
      disabled={!links}
      onPress={() => links?.open(action.threadId)}
      style={({ pressed }) => [styles.card, { borderColor: colors.borderDefault, backgroundColor: pressed ? colors.bgTertiary : colors.bgSecondary }]}
    >
      <View style={[styles.iconTile, { backgroundColor: colors.bgPrimary }]}>
        <Icon name={action.kind === 'created' ? 'messageSquarePlus' : 'messageSquareText'} size={20} strokeWidth={1.5} color={colors.textSecondary} />
      </View>
      <View style={styles.cardText}>
        <Txt numberOfLines={1} style={styles.name}>
          {title}
        </Txt>
        <Txt variant="meta" tone="secondary" numberOfLines={1}>
          {subtitle}
        </Txt>
      </View>
      <Icon name="chevronRight" size={16} color={colors.textSecondary} />
    </Pressable>
  )
}

function RetryButton({ delay, onRetry }: { delay: number | null; onRetry: () => Promise<void> }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [deadline] = useState(() => (delay === null ? null : Date.now() + delay * 1000))
  const [remaining, setRemaining] = useState(delay ?? 0)
  const [busy, setBusy] = useState(false)
  const fired = useRef(false)
  const run = () => {
    fired.current = true
    setBusy(true)
    void onRetry()
      .catch(() => undefined)
      .finally(() => setBusy(false))
  }
  const expire = useEffectEvent(() => {
    if (!fired.current) run()
  })

  useEffect(() => {
    if (deadline === null || busy) return
    const tick = () => {
      const left = Math.max(0, Math.ceil((deadline - Date.now()) / 1000))
      setRemaining(left)
      if (left === 0) expire()
    }
    tick()
    const timer = setInterval(tick, 1000)
    return () => clearInterval(timer)
  }, [deadline, busy])

  const counting = deadline !== null && remaining > 0
  const label = counting ? t('failure.retryCountdown', { seconds: remaining }) : t('failure.retry')
  const progress = counting && delay ? (delay - remaining) / delay : 0
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: busy, busy }}
      disabled={busy}
      onPress={run}
      style={({ pressed }) => [
        styles.retry,
        { borderColor: colors.borderDefault, backgroundColor: pressed ? colors.bgTertiary : colors.bgPrimary },
        busy && styles.disabled,
      ]}
    >
      {counting ? <View style={[styles.retryFill, { width: `${progress * 100}%`, backgroundColor: colors.roundFill }]} /> : null}
      <Text numberOfLines={1} style={[type.body, styles.tabular, { color: colors.textPrimary }]}>
        {label}
      </Text>
    </Pressable>
  )
}

export function FailureLine({ entry }: { entry: Extract<TranscriptEntry, { kind: 'failure' }> }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const onRetry = useContext(RetryContext)
  const message = entry.overloaded ? t('failure.overloaded') : entry.message
  return (
    <View accessibilityRole="alert" style={[styles.failure, { backgroundColor: colors.bgSecondary }]}>
      <View style={styles.failureBody}>
        <View style={styles.failureIcon}>
          <Icon name="circleAlert" size={14} color={colors.errorText} />
        </View>
        <Txt variant="meta" tone="secondary" selectable style={styles.shrink}>
          {message}
        </Txt>
      </View>
      {entry.retry && onRetry ? (
        <RetryButton delay={entry.retry.delay} onRetry={onRetry} />
      ) : (
        <CopyButton text={entry.message} label={t('failure.copy')} />
      )}
    </View>
  )
}

const styles = StyleSheet.create({
  divider: { flexDirection: 'row', alignItems: 'center', gap: 8, paddingVertical: 6 },
  line: { flex: 1, minWidth: 8, height: StyleSheet.hairlineWidth },
  label: { flexDirection: 'row', alignItems: 'center', gap: 6, flexShrink: 1, minWidth: 0, paddingHorizontal: 4 },
  shrink: { flexShrink: 1 },
  origin: { flexDirection: 'row', alignItems: 'center', gap: 4 },
  card: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 10, paddingHorizontal: 12, borderWidth: 1, borderRadius: 8 },
  iconTile: { width: 40, height: 40, borderRadius: 8, alignItems: 'center', justifyContent: 'center' },
  cardText: { flex: 1, minWidth: 0, gap: 2 },
  name: { fontWeight: '500' },
  failure: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    rowGap: 8,
    columnGap: 12,
    minHeight: 44,
    paddingVertical: 8,
    paddingLeft: 12,
    paddingRight: 8,
    borderRadius: 8,
  },
  failureBody: { flexDirection: 'row', alignItems: 'flex-start', gap: 8, flexGrow: 1, flexShrink: 1, flexBasis: 200, minWidth: 0 },
  failureIcon: { marginTop: 3 },
  retry: { height: 32, paddingHorizontal: 12, borderWidth: 1, borderRadius: 8, justifyContent: 'center', overflow: 'hidden' },
  retryFill: { position: 'absolute', left: 0, top: 0, bottom: 0 },
  disabled: { opacity: 0.5 },
  tabular: { fontVariant: ['tabular-nums'] },
})

import { useState } from 'react'
import { Pressable, StyleSheet, Text, View } from 'react-native'
import type { ToolVerb, TranscriptEntry } from '../../core/transcript'
import { useI18n } from '../../i18n'
import type { MessageId } from '../../i18n/messages/en'
import { Icon, type IconName } from '../icons'
import { Caret, RichText, Txt } from '../parts'
import { metrics, type, useTheme } from '../theme'

const TOOL_ICON: Record<ToolVerb, IconName> = {
  ran: 'terminal',
  edited: 'filePen',
  read: 'fileText',
  searched: 'search',
  used: 'wrench',
}

const TOOL_TEXT: Record<ToolVerb, MessageId> = {
  ran: 'tool.ran',
  edited: 'tool.edited',
  read: 'tool.read',
  searched: 'tool.searched',
  used: 'tool.used',
}

const TOOL_PENDING_TEXT: Record<Exclude<ToolVerb, 'used'>, MessageId> = {
  ran: 'tool.running',
  edited: 'tool.editing',
  read: 'tool.reading',
  searched: 'tool.searching',
}

const NOTICE_TEXT = {
  allowedOnce: 'notice.allowedOnce',
  allowedForSession: 'notice.allowedForSession',
  allowedAlways: 'notice.allowedAlways',
  rejected: 'notice.rejected',
  answered: 'notice.answered',
  stopped: 'notice.stopped',
  turnFailed: 'notice.turnFailed',
} as const satisfies Record<string, MessageId>

function Reasoning({ text, seconds }: { text: string; seconds: number | null }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const label = seconds === null ? t('chat.thinking') : t('chat.thoughtFor', { seconds })
  return (
    <View>
      <Pressable
        accessibilityRole="button"
        accessibilityState={{ expanded: open }}
        disabled={!text}
        onPress={() => setOpen((value) => !value)}
        style={styles.reasoningToggle}
      >
        <Txt tone="secondary">{label}</Txt>
        {text ? (
          <View style={{ transform: [{ rotate: open ? '90deg' : '0deg' }] }}>
            <Icon name="chevronRight" size={14} color={colors.textSecondary} />
          </View>
        ) : null}
      </Pressable>
      {open ? (
        <Text style={[type.body, styles.reasoningText, { color: colors.textDimmed, borderLeftColor: colors.borderDefault }]}>{text}</Text>
      ) : null}
    </View>
  )
}

function ToolLine({ entry }: { entry: Extract<TranscriptEntry, { kind: 'tool' }> }) {
  const { t, around } = useI18n()
  const { colors } = useTheme()
  const icon = (
    <View style={styles.toolIcon}>
      <Icon name={TOOL_ICON[entry.verb]} size={15} color={colors.textDimmed} strokeWidth={1.7} />
    </View>
  )
  if (entry.subjects.length === 0 && entry.verb !== 'used') {
    return (
      <View style={styles.tool}>
        {icon}
        <Text style={[type.text, styles.line, styles.shrink, { color: colors.textSecondary }]}>{t(TOOL_PENDING_TEXT[entry.verb])}</Text>
      </View>
    )
  }
  const subject = entry.subjects.length === 1 ? entry.subjects[0] : t('tool.files', { count: entry.subjects.length })
  const code = entry.code && entry.subjects.length === 1
  const [before, after] = around(TOOL_TEXT[entry.verb], 'subject')
  return (
    <View style={styles.tool}>
      {icon}
      <Text style={[type.text, styles.line, styles.shrink, { color: colors.textSecondary }]}>
        {before}
        <Text style={code ? [type.code, { backgroundColor: colors.bgTertiary }] : undefined}>{code ? ` ${subject} ` : subject}</Text>
        {after}
        {entry.added !== undefined ? <Text style={{ color: colors.successText }}>{` +${entry.added}`}</Text> : null}
        {entry.removed !== undefined ? <Text style={{ color: colors.errorText }}>{` −${entry.removed}`}</Text> : null}
      </Text>
    </View>
  )
}

export function TranscriptLine({ entry, previous, caret }: { entry: TranscriptEntry; previous?: TranscriptEntry; caret: boolean }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const tight = entry.kind === 'tool' && previous?.kind === 'tool'
  switch (entry.kind) {
    case 'user':
      return (
        <View style={styles.user}>
          {entry.added ? (
            <View style={styles.origin}>
              <Icon name="cornerDownRight" size={13} color={colors.textDimmed} />
              <Txt variant="caption" tone="dimmed">
                {t('chat.addedToTurn')}
              </Txt>
            </View>
          ) : null}
          <View style={[styles.bubble, { backgroundColor: colors.userMessageBg }]}>
            <Txt selectable style={styles.line}>
              {entry.text}
            </Txt>
          </View>
        </View>
      )
    case 'assistant':
      return (
        <View accessibilityLiveRegion={entry.streaming ? 'polite' : 'none'}>
          <RichText text={entry.text} trailing={caret ? <Caret /> : null} />
        </View>
      )
    case 'reasoning':
      return <Reasoning text={entry.text} seconds={entry.seconds} />
    case 'tool':
      return (
        <View style={tight ? styles.tight : undefined}>
          <ToolLine entry={entry} />
        </View>
      )
    case 'notice':
      if (entry.tone === 'error') {
        return (
          <View accessibilityRole="alert" style={[styles.failure, { borderColor: colors.failureBorder, backgroundColor: colors.failureFill }]}>
            <Icon name="triangleAlert" size={20} color={colors.error} />
            <View style={styles.failureText}>
              <Txt style={styles.strong}>{t(NOTICE_TEXT[entry.notice])}</Txt>
              {entry.detail ? (
                <Txt variant="meta" tone="secondary">
                  {entry.detail}
                </Txt>
              ) : null}
              <Txt variant="meta" tone="secondary">
                {t('notice.retryHint')}
              </Txt>
            </View>
          </View>
        )
      }
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
}

const styles = StyleSheet.create({
  shrink: { flexShrink: 1 },
  fixed: { flexShrink: 0 },
  line: { lineHeight: 22 },
  user: { alignItems: 'flex-end', gap: 4 },
  origin: { flexDirection: 'row', alignItems: 'center', gap: 4 },
  bubble: { maxWidth: '84%', paddingVertical: 9, paddingHorizontal: 14, borderRadius: 20 },
  reasoningToggle: { flexDirection: 'row', alignItems: 'center', gap: 4, alignSelf: 'flex-start' },
  reasoningText: { marginTop: 6, paddingLeft: 12, borderLeftWidth: 2 },
  tool: { flexDirection: 'row', alignItems: 'flex-start', gap: 8 },
  toolIcon: { marginTop: 4 },
  tight: { marginTop: -10 },
  process: { flexDirection: 'row', gap: 6, minWidth: 0 },
  failure: { flexDirection: 'row', gap: 12, padding: 14, borderWidth: 1, borderRadius: metrics.noticeRadius },
  failureText: { flex: 1, minWidth: 0, gap: 2 },
  strong: { fontWeight: '600' },
})

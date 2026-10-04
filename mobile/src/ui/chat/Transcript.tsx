import * as Clipboard from 'expo-clipboard'
import { useEffect, useState } from 'react'
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import { workspaceFile } from '../../core/links'
import { baseName, type ToolIcon, type ToolVerb, type TranscriptEntry } from '../../core/transcript'
import type { UserSegment } from '../../core/userSegments'
import { useI18n } from '../../i18n'
import type { MessageId } from '../../i18n/messages/en'
import { Icon, type IconName } from '../icons'
import { Spinner, Txt } from '../parts'
import { metrics, type, useTheme } from '../theme'
import { FileChip, InlineChip, SkillChip } from './Chips'
import { ImageThumb } from './Images'
import { Markdown } from './Markdown'
import { PlanCard } from './PlanCard'

type ToolEntry = Extract<TranscriptEntry, { kind: 'tool' }>

const TOOL_ICON: Record<ToolIcon, IconName> = {
  terminal: 'squareTerminal',
  declined: 'shieldAlert',
  stopped: 'circleStop',
  read: 'bookOpen',
  search: 'search',
  folder: 'folder',
  edit: 'pencil',
  web: 'globe',
  other: 'wrench',
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

function ToolLine({ entry }: { entry: ToolEntry }) {
  const { t, around } = useI18n()
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const grouped = entry.subjects.length > 1
  const textStyle = [type.text, styles.line, styles.shrink, { color: colors.textSecondary }]
  const pending = entry.subjects.length === 0 && entry.verb !== 'used'
  const subject = grouped ? t('tool.files', { count: entry.subjects.length }) : entry.subjects[0]
  const code = entry.code && !grouped
  const [before, after] = around(TOOL_TEXT[entry.verb], 'subject')
  return (
    <View style={styles.toolBlock}>
      <Pressable
        accessibilityRole="button"
        accessibilityState={{ expanded: open }}
        disabled={entry.details.length === 0}
        onPress={() => setOpen((value) => !value)}
        style={styles.tool}
      >
        {grouped ? null : (
          <Icon
            name={TOOL_ICON[entry.icon]}
            size={16}
            color={entry.icon === 'declined' ? colors.warningText : colors.textSecondary}
            strokeWidth={1.6}
          />
        )}
        {pending ? (
          <Text numberOfLines={1} style={textStyle}>
            {t(TOOL_PENDING_TEXT[entry.verb as Exclude<ToolVerb, 'used'>])}
          </Text>
        ) : (
          <Text numberOfLines={1} style={textStyle}>
            {before}
            <Text style={code ? [type.code, { backgroundColor: colors.bgTertiary }] : undefined}>{code ? ` ${subject} ` : subject}</Text>
            {after}
            {entry.added !== undefined ? <Text style={{ color: colors.successText }}>{` +${entry.added}`}</Text> : null}
            {entry.removed !== undefined ? <Text style={{ color: colors.errorText }}>{` −${entry.removed}`}</Text> : null}
          </Text>
        )}
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
        {running ? <Spinner size={16} /> : <Icon name="image" size={16} color={colors.textSecondary} strokeWidth={1.6} />}
        <Txt tone="secondary">{running ? t('image.generating') : t('image.generated')}</Txt>
      </View>
      {entry.uri ? (
        <ImageThumb uri={entry.uri} label={t('image.open')} style={styles.generatedImage} />
      ) : (
        <View style={[styles.generatedImage, styles.placeholder, { backgroundColor: colors.bgTertiary }]} />
      )}
    </View>
  )
}

function CopyButton({ text }: { text: string }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [copied, setCopied] = useState(false)
  useEffect(() => {
    if (!copied) return
    const timer = setTimeout(() => setCopied(false), 1500)
    return () => clearTimeout(timer)
  }, [copied])
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={copied ? t('common.copied') : t('chat.copy')}
      hitSlop={6}
      onPress={() => void Clipboard.setStringAsync(text).then(() => setCopied(true))}
      style={({ pressed }) => [styles.copy, pressed && { backgroundColor: colors.roundFill }]}
    >
      <Icon name={copied ? 'check' : 'copy'} size={16} color={colors.textDimmed} />
    </Pressable>
  )
}

function UserText({ segments, workspacePath }: { segments: UserSegment[]; workspacePath: string | null }) {
  return (
    <>
      {segments.map((segment, index) =>
        segment.type === 'text' ? (
          segment.value
        ) : (
          <InlineChip key={index}>
            {segment.type === 'file' ? <FileChip path={workspaceFile(segment.path, workspacePath)} label={baseName(segment.display)} /> : <SkillChip name={segment.name} />}
          </InlineChip>
        ),
      )}
    </>
  )
}

export function TranscriptLine({
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
          {entry.images.length > 0 ? (
            <View style={styles.photos}>
              {entry.images.map((uri, index) => (
                <ImageThumb key={index} uri={uri} label={t('image.open')} style={styles.photo} />
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
        </View>
      )
    case 'assistant':
      return (
        <View accessibilityLiveRegion={entry.streaming ? 'polite' : 'none'}>
          <Markdown text={entry.text} workspacePath={workspacePath} />
          {entry.streaming ? null : <CopyButton text={entry.text} />}
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
    case 'image':
      return <GeneratedImage entry={entry} />
    case 'plan':
      return <PlanCard entry={entry} workspacePath={workspacePath} />
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
  photos: { flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 6, maxWidth: '84%' },
  photo: { width: 112, maxHeight: 160 },
  bubble: { maxWidth: '84%', paddingVertical: 9, paddingHorizontal: 14, borderRadius: 20 },
  reasoningToggle: { flexDirection: 'row', alignItems: 'center', gap: 4, alignSelf: 'flex-start' },
  reasoningText: { marginTop: 6, paddingLeft: 12, borderLeftWidth: 2 },
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
  copy: { alignSelf: 'flex-start', marginTop: 4, marginLeft: -6, padding: 6, borderRadius: 8 },
  tight: { marginTop: -6 },
  process: { flexDirection: 'row', gap: 6, minWidth: 0 },
  failure: { flexDirection: 'row', gap: 12, padding: 14, borderWidth: 1, borderRadius: metrics.noticeRadius },
  failureText: { flex: 1, minWidth: 0, gap: 2 },
  strong: { fontWeight: '600' },
})

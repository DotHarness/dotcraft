import { useEffect, useState, type ReactNode } from 'react'
import {
  Animated,
  Easing,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  View,
  type StyleProp,
  type TextProps,
  type TextStyle,
  type ViewStyle,
} from 'react-native'
import Svg, { Circle, Path } from 'react-native-svg'
import type { ChatState } from '../core/chatState'
import type { ComputerStatus } from '../core/state'
import { useI18n } from '../i18n'
import type { MessageId } from '../i18n/messages/en'
import { Icon, type IconName } from './icons'
import { camera, metrics, type, useTheme } from './theme'

export const nativeDriver = Platform.OS !== 'web'

export const STATE_LABEL: Record<ChatState, MessageId> = {
  running: 'state.running',
  'needs-approval': 'state.needsApproval',
  'needs-answer': 'state.needsAnswer',
  done: 'state.done',
  failed: 'state.failed',
}

export function Txt({
  tone = 'primary',
  variant = 'text',
  style,
  ...props
}: TextProps & {
  tone?: 'primary' | 'secondary' | 'dimmed' | 'error'
  variant?: 'text' | 'meta' | 'caption'
  style?: StyleProp<TextStyle>
}) {
  const { colors } = useTheme()
  const color =
    tone === 'secondary' ? colors.textSecondary : tone === 'dimmed' ? colors.textDimmed : tone === 'error' ? colors.errorText : colors.textPrimary
  return <Text {...props} style={[type[variant], { color }, style]} />
}

function Spinner({ size, color }: { size: number; color?: string }) {
  const { colors } = useTheme()
  const [rotation] = useState(() => new Animated.Value(0))
  useEffect(() => {
    const loop = Animated.loop(
      Animated.timing(rotation, { toValue: 1, duration: 1000, easing: Easing.linear, useNativeDriver: nativeDriver }),
    )
    loop.start()
    return () => loop.stop()
  }, [rotation])
  const stroke = color ?? colors.textSecondary
  const spin = rotation.interpolate({ inputRange: [0, 1], outputRange: ['0deg', '360deg'] })
  return (
    <Animated.View style={{ width: size, height: size, transform: [{ rotate: spin }] }}>
      <Svg width={size} height={size} viewBox="0 0 24 24" fill="none">
        <Circle cx={12} cy={12} r={7} stroke={stroke} strokeWidth={2} opacity={0.3} />
        <Circle cx={12} cy={12} r={7} stroke={stroke} strokeWidth={2} strokeDasharray="33 44" />
      </Svg>
    </Animated.View>
  )
}

function StatusIndicator({ tone }: { tone: 'success' | 'warning' | 'error' | 'neutral' | 'pending' }) {
  const { colors } = useTheme()
  const fill = tone === 'success' ? colors.success : tone === 'warning' ? colors.warning : tone === 'error' ? colors.error : colors.textDimmed
  return (
    <View style={styles.indicator}>
      {tone === 'pending' ? <Spinner size={14} /> : <View style={[styles.dot, { backgroundColor: fill }]} />}
    </View>
  )
}

export function StateMark({ state, live }: { state: ChatState; live: boolean }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  let glyph: ReactNode
  if (state === 'needs-approval' || state === 'needs-answer') {
    glyph = (
      <Svg width={18} height={18} viewBox="0 0 18 18">
        <Circle cx={9} cy={9} r={9} fill={colors.warningBg} />
        <Path d="M9 4.6v5.2" fill="none" stroke={colors.warningText} strokeLinecap="round" strokeWidth={1.9} />
        <Circle cx={9} cy={12.9} r={1.05} fill={colors.warningText} />
      </Svg>
    )
  } else if (state === 'failed') {
    glyph = (
      <Svg width={18} height={18} viewBox="0 0 18 18">
        <Path d="M9 2.2 16.4 15H1.6Z" fill="none" stroke={colors.errorText} strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.6} />
        <Path d="M9 7v3.6" fill="none" stroke={colors.errorText} strokeLinecap="round" strokeWidth={1.6} />
        <Circle cx={9} cy={12.7} r={0.95} fill={colors.errorText} />
      </Svg>
    )
  } else if (state === 'running' && live) {
    glyph = <Spinner size={16} />
  } else {
    return null
  }
  return (
    <View style={styles.mark} accessible accessibilityRole="image" accessibilityLabel={t(STATE_LABEL[state])}>
      {glyph}
    </View>
  )
}

export function ComputerStatusLine({ status, updatedAt }: { status: ComputerStatus; updatedAt: string | null }) {
  const { t, ago } = useI18n()
  const label =
    status === 'online'
      ? t('status.online')
      : status === 'connecting'
        ? t('status.connecting')
        : t(status === 'offline' ? 'status.offline' : 'status.accessOff', { time: ago(updatedAt) })
  return (
    <View style={styles.statusLine}>
      <StatusIndicator tone={status === 'online' ? 'success' : status === 'connecting' ? 'pending' : 'neutral'} />
      <Txt variant="meta" tone="secondary" numberOfLines={1} style={styles.shrink}>
        {label}
      </Txt>
    </View>
  )
}

export function ChatStateLine({ state, live }: { state: ChatState; live: boolean }) {
  const { t } = useI18n()
  const tone = state === 'failed' ? 'error' : state === 'needs-approval' || state === 'needs-answer' ? 'warning' : 'neutral'
  const label = t(STATE_LABEL[state])
  return (
    <View style={styles.statusLine}>
      <StatusIndicator tone={state === 'running' && live ? 'pending' : tone} />
      <Txt variant="meta" tone="secondary" numberOfLines={1} style={styles.shrink}>
        {live || state === 'done' || state === 'failed' ? label : t('state.lastSynced', { state: label })}
      </Txt>
    </View>
  )
}

export function Notice({
  icon,
  children,
  action,
  tone = 'default',
  style,
}: {
  icon: IconName
  children: string
  action?: ReactNode
  tone?: 'default' | 'camera'
  style?: StyleProp<ViewStyle>
}) {
  const { colors } = useTheme()
  const onCamera = tone === 'camera'
  const color = onCamera ? camera.noticeText : colors.textSecondary
  return (
    <View
      accessibilityRole="text"
      accessibilityLiveRegion="polite"
      style={[
        styles.notice,
        onCamera
          ? { borderColor: camera.noticeBorder, backgroundColor: camera.noticeFill }
          : { borderColor: colors.borderDefault, backgroundColor: colors.bgSecondary },
        style,
      ]}
    >
      <View style={styles.noticeIcon}>
        <Icon name={icon} size={16} color={color} />
      </View>
      <Text style={[type.meta, styles.shrink, { color }]}>{children}</Text>
      {action}
    </View>
  )
}

export function ReadOnlyNotice({ status, computer, style }: { status: ComputerStatus; computer: string; style?: StyleProp<ViewStyle> }) {
  const { t } = useI18n()
  const accessOff = status === 'access-off'
  return (
    <Notice icon={accessOff ? 'unplug' : 'cloudOff'} style={style}>
      {t(accessOff ? 'notice.accessOff' : 'notice.offline', { computer })}
    </Notice>
  )
}

export function PhoneButton({
  variant = 'primary',
  icon,
  children,
  onPress,
  disabled = false,
  loading = false,
  compact = false,
  height,
  style,
}: {
  variant?: 'primary' | 'outline' | 'ghost' | 'danger'
  icon?: IconName
  children: string
  onPress: () => void
  disabled?: boolean
  loading?: boolean
  compact?: boolean
  height?: number
  style?: StyleProp<ViewStyle>
}) {
  const { colors } = useTheme()
  const label =
    variant === 'primary' ? colors.bgPrimary : variant === 'danger' ? colors.errorText : colors.textPrimary
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={children}
      accessibilityState={{ disabled: disabled || loading, busy: loading }}
      disabled={disabled || loading}
      onPress={onPress}
      style={({ pressed }) => {
        const surface: ViewStyle =
          variant === 'primary'
            ? { backgroundColor: pressed ? colors.primaryPressed : colors.textPrimary, borderColor: colors.textPrimary }
            : variant === 'outline'
              ? { backgroundColor: pressed ? colors.bgTertiary : colors.bgSecondary, borderColor: pressed ? colors.borderActive : colors.borderDefault }
              : variant === 'danger'
                ? { backgroundColor: pressed ? colors.dangerFillPressed : colors.dangerFill, borderColor: 'transparent' }
                : { backgroundColor: pressed ? colors.bgTertiary : 'transparent', borderColor: 'transparent' }
        return [
          styles.button,
          compact && styles.buttonCompact,
          height !== undefined && { height },
          surface,
          disabled && !loading && styles.disabled,
          style,
        ]
      }}
    >
      {loading ? (
        <View style={styles.buttonSpinner}>
          <Spinner size={18} color={label} />
        </View>
      ) : null}
      <View style={[styles.buttonLabel, loading && styles.hidden]}>
        {icon ? <Icon name={icon} size={18} color={label} /> : null}
        <Text numberOfLines={1} style={[compact ? type.body : type.text, styles.buttonText, { color: label }]}>
          {children}
        </Text>
      </View>
    </Pressable>
  )
}

export function RoundIconButton({
  label,
  icon,
  onPress,
  tone = 'default',
}: {
  label: string
  icon: IconName
  onPress: () => void
  tone?: 'default' | 'camera'
}) {
  const { colors } = useTheme()
  const onCamera = tone === 'camera'
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={onPress}
      hitSlop={4}
      style={({ pressed }) => [
        styles.round,
        { backgroundColor: onCamera ? camera.roundFill : pressed ? colors.roundFillPressed : colors.roundFill },
      ]}
    >
      <Icon
        name={icon}
        size={icon === 'x' ? 18 : icon === 'chevronLeft' ? 22 : 20}
        color={onCamera ? camera.text : colors.textPrimary}
        strokeWidth={icon === 'settings' ? 1.7 : 1.8}
      />
    </Pressable>
  )
}

export function BackButton({ onPress, tone }: { onPress: () => void; tone?: 'default' | 'camera' }) {
  const { t } = useI18n()
  return <RoundIconButton label={t('common.back')} icon="chevronLeft" onPress={onPress} tone={tone} />
}

export function RowChevron() {
  const { colors } = useTheme()
  return <Icon name="chevronRight" size={16} color={colors.textDimmed} />
}

export function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <View style={styles.section}>
      <Txt accessibilityRole="header" style={styles.sectionTitle}>
        {title}
      </Txt>
      {children}
    </View>
  )
}

function InlineText({ text, codeStyle }: { text: string; codeStyle: TextStyle }) {
  return (
    <>
      {text.split(/(`[^`]+`)/g).map((part, index) =>
        part.startsWith('`') && part.endsWith('`') && part.length > 1 ? (
          <Text key={index} style={codeStyle}>
            {part.slice(1, -1)}
          </Text>
        ) : (
          <Text key={index}>{part}</Text>
        ),
      )}
    </>
  )
}

export function Caret() {
  const { colors } = useTheme()
  const [opacity] = useState(() => new Animated.Value(1))
  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, { toValue: 0, duration: 0, delay: 400, useNativeDriver: nativeDriver }),
        Animated.timing(opacity, { toValue: 1, duration: 0, delay: 400, useNativeDriver: nativeDriver }),
      ]),
    )
    loop.start()
    return () => loop.stop()
  }, [opacity])
  return <Animated.Text style={{ opacity, color: colors.textPrimary }}>{'▏'}</Animated.Text>
}

export function RichText({ text, trailing }: { text: string; trailing: ReactNode }) {
  const { colors } = useTheme()
  const codeStyle: TextStyle = { ...type.code, backgroundColor: colors.bgTertiary, borderRadius: 5 }
  const blocks: ({ kind: 'p'; text: string } | { kind: 'ul'; items: string[] })[] = []
  for (const line of text.split('\n')) {
    if (line.startsWith('- ')) {
      const last = blocks[blocks.length - 1]
      if (last?.kind === 'ul') last.items.push(line.slice(2))
      else blocks.push({ kind: 'ul', items: [line.slice(2)] })
    } else if (line.trim().length > 0) {
      blocks.push({ kind: 'p', text: line })
    }
  }
  const base: StyleProp<TextStyle> = [type.text, styles.prose, { color: colors.textPrimary }]
  return (
    <View style={styles.blocks}>
      {blocks.map((block, index) => {
        const last = index === blocks.length - 1
        if (block.kind === 'ul') {
          return (
            <View key={index} style={styles.list}>
              {block.items.map((item, itemIndex) => (
                <View key={itemIndex} style={styles.listItem}>
                  <Text style={base}>{'•'}</Text>
                  <Text style={[base, styles.shrink]}>
                    <InlineText text={item} codeStyle={codeStyle} />
                    {last && itemIndex === block.items.length - 1 ? trailing : null}
                  </Text>
                </View>
              ))}
            </View>
          )
        }
        return (
          <Text key={index} style={base}>
            <InlineText text={block.text} codeStyle={codeStyle} />
            {last ? trailing : null}
          </Text>
        )
      })}
    </View>
  )
}

const styles = StyleSheet.create({
  shrink: { flexShrink: 1 },
  indicator: { width: 14, height: 14, alignItems: 'center', justifyContent: 'center' },
  dot: { width: 8, height: 8, borderRadius: 4 },
  mark: { width: 20, height: 20, alignItems: 'center', justifyContent: 'center' },
  statusLine: { flexDirection: 'row', alignItems: 'center', gap: 4, minWidth: 0, flexShrink: 1 },
  notice: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: 8,
    paddingVertical: 10,
    paddingHorizontal: 12,
    borderWidth: 1,
    borderRadius: metrics.noticeRadius,
  },
  noticeIcon: { marginTop: 1 },
  button: {
    height: metrics.touch,
    paddingHorizontal: 20,
    borderWidth: 1,
    borderRadius: metrics.pill,
    alignItems: 'center',
    justifyContent: 'center',
  },
  buttonCompact: { height: 34, paddingHorizontal: 14 },
  buttonLabel: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  buttonText: { fontWeight: '600' },
  buttonSpinner: { position: 'absolute', top: 0, right: 0, bottom: 0, left: 0, alignItems: 'center', justifyContent: 'center' },
  hidden: { opacity: 0 },
  disabled: { opacity: 0.45 },
  round: { width: 40, height: 40, margin: 2, borderRadius: 20, alignItems: 'center', justifyContent: 'center' },
  section: { marginTop: 26 },
  sectionTitle: { fontWeight: '600', marginBottom: 2 },
  prose: { lineHeight: 22 },
  blocks: { gap: 8 },
  list: { gap: 4 },
  listItem: { flexDirection: 'row', gap: 8, paddingLeft: 4 },
})

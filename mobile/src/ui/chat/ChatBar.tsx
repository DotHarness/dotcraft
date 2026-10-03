import type { ReactNode } from 'react'
import { Pressable, StyleSheet, Text, View } from 'react-native'
import Svg, { Defs, LinearGradient, Rect, Stop } from 'react-native-svg'
import type { ComputerStatus } from '../../core/state'
import { useI18n } from '../../i18n'
import { Icon, type IconName } from '../icons'
import { Spinner, Txt } from '../parts'
import { metrics, type, useTheme } from '../theme'

export const BAR_HEIGHT = 48

function Pill({ children, style }: { children: ReactNode; style?: object }) {
  const { colors } = useTheme()
  return (
    <View style={[styles.pill, { backgroundColor: colors.bgElevated, borderColor: colors.borderDefault, boxShadow: colors.shadow1 }, style]}>
      {children}
    </View>
  )
}

export function BarButton({ icon, label, onPress }: { icon: IconName; label: string; onPress: () => void }) {
  const { colors } = useTheme()
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={label} onPress={onPress} hitSlop={4} style={({ pressed }) => [styles.button, pressed && { backgroundColor: colors.roundFillPressed }]}>
      <Icon name={icon} size={icon === 'chevronLeft' ? 22 : 20} color={colors.textPrimary} />
    </Pressable>
  )
}

function StatusDot({ status }: { status: ComputerStatus }) {
  const { colors } = useTheme()
  if (status === 'connecting') return <Spinner size={10} />
  return <View style={[styles.dot, { backgroundColor: status === 'online' ? colors.success : colors.textDimmed }]} />
}

export function ChatBar({
  title,
  project,
  computer,
  status,
  trailing,
  onBack,
}: {
  title: string
  project: string
  computer: string
  status: ComputerStatus
  trailing?: ReactNode
  onBack: () => void
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const meta = [type.caption, styles.meta, { color: colors.textSecondary }]
  return (
    <View style={styles.bar} pointerEvents="box-none">
      <Svg style={StyleSheet.absoluteFill} width="100%" height="100%" pointerEvents="none" preserveAspectRatio="none" viewBox="0 0 1 1">
        <Defs>
          <LinearGradient id="fade" x1="0" y1="0" x2="0" y2="1">
            <Stop offset="0" stopColor={colors.bgPrimary} stopOpacity={1} />
            <Stop offset="0.6" stopColor={colors.bgPrimary} stopOpacity={0.85} />
            <Stop offset="1" stopColor={colors.bgPrimary} stopOpacity={0} />
          </LinearGradient>
        </Defs>
        <Rect x="0" y="0" width="1" height="1" fill="url(#fade)" />
      </Svg>
      <Pill style={styles.round}>
        <BarButton icon="chevronLeft" label={t('common.back')} onPress={onBack} />
      </Pill>
      <Pill style={styles.titlePill}>
        <Txt accessibilityRole="header" numberOfLines={1} style={styles.title}>
          {title}
        </Txt>
        <View style={styles.metaRow}>
          <Icon name="folder" size={12} color={colors.textSecondary} strokeWidth={2} />
          <Text numberOfLines={1} style={[...meta, styles.project]}>
            {project}
          </Text>
          <Icon name="monitor" size={12} color={colors.textSecondary} strokeWidth={2} />
          <StatusDot status={status} />
          <Text numberOfLines={1} style={meta}>
            {computer}
          </Text>
        </View>
      </Pill>
      {trailing ? <Pill style={styles.trailing}>{trailing}</Pill> : null}
    </View>
  )
}

const styles = StyleSheet.create({
  bar: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    paddingTop: 4,
    paddingHorizontal: 12,
    paddingBottom: 20,
  },
  pill: { height: BAR_HEIGHT, borderWidth: 1, borderRadius: metrics.pill, justifyContent: 'center' },
  round: { width: BAR_HEIGHT, alignItems: 'center' },
  button: { width: 40, height: 40, borderRadius: 20, alignItems: 'center', justifyContent: 'center' },
  titlePill: { flex: 1, minWidth: 0, paddingHorizontal: 16 },
  title: { fontWeight: '600', lineHeight: 19 },
  metaRow: { flexDirection: 'row', alignItems: 'center', gap: 4, minWidth: 0 },
  meta: { flexShrink: 1 },
  project: { marginRight: 4, maxWidth: '50%' },
  dot: { width: 6, height: 6, borderRadius: 3 },
  trailing: { flexDirection: 'row', alignItems: 'center', gap: 2, paddingHorizontal: 4 },
})

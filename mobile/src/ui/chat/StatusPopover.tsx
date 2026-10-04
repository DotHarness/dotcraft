import * as Clipboard from 'expo-clipboard'
import { useEffect, useState, type ReactNode } from 'react'
import { Animated, Easing, Modal, Pressable, StyleSheet, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { windowSpan, type UsageWindow } from '../../core/accountUsage'
import { compactCount, type ContextUsage } from '../../core/contextUsage'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { nativeDriver, Txt } from '../parts'
import { metrics, useTheme } from '../theme'
import { BAR_HEIGHT } from './ChatBar'

const OPEN = Easing.bezier(0.23, 1, 0.32, 1)

function Row({ label, value, action }: { label: string; value: string; action?: ReactNode }) {
  return (
    <View style={styles.row}>
      <View style={styles.rowText}>
        <Txt style={styles.label}>{label}</Txt>
        <Txt variant="meta" tone="secondary" numberOfLines={1} ellipsizeMode="middle" selectable>
          {value}
        </Txt>
      </View>
      {action}
    </View>
  )
}

function CopyId({ id }: { id: string }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [copied, setCopied] = useState(false)
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={copied ? t('common.copied') : t('chatStatus.copyId')}
      hitSlop={6}
      onPress={() => void Clipboard.setStringAsync(id).then(() => setCopied(true))}
      style={({ pressed }) => [styles.copy, pressed && { backgroundColor: colors.roundFill }]}
    >
      <Icon name={copied ? 'check' : 'copy'} size={18} color={colors.textSecondary} />
    </Pressable>
  )
}

export function StatusPopover({
  visible,
  chatId,
  folder,
  context,
  windows,
  onClose,
}: {
  visible: boolean
  chatId: string
  folder: string | null
  context: ContextUsage | null
  windows: UsageWindow[]
  onClose: () => void
}) {
  const { t, dateTime } = useI18n()
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  const [progress] = useState(() => new Animated.Value(0))
  useEffect(() => {
    if (!visible) {
      progress.setValue(0)
      return
    }
    Animated.timing(progress, { toValue: 1, duration: 180, easing: OPEN, useNativeDriver: nativeDriver }).start()
  }, [progress, visible])
  const contextText = context
    ? t('chatStatus.contextLeft', {
        percent: Math.round(context.percentLeft * 100),
        used: compactCount(Math.min(context.tokens, context.contextWindow || context.tokens)),
        window: compactCount(context.contextWindow),
      })
    : t('chatStatus.contextUnknown')
  return (
    <Modal transparent visible={visible} animationType="none" onRequestClose={onClose} statusBarTranslucent navigationBarTranslucent>
      <Pressable style={StyleSheet.absoluteFill} onPress={onClose} accessible={false} />
      <Animated.View
        accessibilityViewIsModal
        style={[
          styles.card,
          {
            top: insets.top + BAR_HEIGHT + 10,
            borderColor: colors.borderSubtle,
            backgroundColor: colors.bgElevated,
            boxShadow: colors.shadow3,
            opacity: progress,
            transform: [{ translateY: progress.interpolate({ inputRange: [0, 1], outputRange: [-8, 0] }) }],
          },
        ]}
      >
        <Txt accessibilityRole="header" tone="secondary" style={styles.title}>
          {t('chatStatus.open')}
        </Txt>
        <View style={[styles.separator, { backgroundColor: colors.borderDefault }]} />
        <Row label={t('chatStatus.chat')} value={chatId} action={<CopyId id={chatId} />} />
        {folder ? <Row label={t('chatStatus.folder')} value={folder} /> : null}
        <Row label={t('chatStatus.context')} value={contextText} />
        {windows.map((window) => {
          const span = windowSpan(window.seconds)
          return (
            <Row
              key={`${window.seconds}-${window.resetAt}`}
              label={t(span.unit === 'days' ? 'chatStatus.daysLimit' : 'chatStatus.hoursLimit', { count: span.count })}
              value={t('chatStatus.usageLeft', { percent: window.percentLeft, time: dateTime(window.resetAt) })}
            />
          )
        })}
      </Animated.View>
    </Modal>
  )
}

const styles = StyleSheet.create({
  card: {
    position: 'absolute',
    left: 12,
    right: 12,
    paddingVertical: 14,
    paddingHorizontal: 18,
    gap: 12,
    borderWidth: 1,
    borderRadius: metrics.heroRadius,
  },
  title: { fontWeight: '500' },
  separator: { height: StyleSheet.hairlineWidth },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  rowText: { flex: 1, minWidth: 0, gap: 2 },
  label: { fontWeight: '600' },
  copy: { width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
})

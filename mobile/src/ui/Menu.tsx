import { useEffect, useState, type ReactNode } from 'react'
import { Animated, Easing, Modal, Pressable, StyleSheet, View } from 'react-native'
import { Icon, type IconName } from './icons'
import { nativeDriver, Txt } from './parts'
import { metrics, useTheme } from './theme'

const OPEN = Easing.bezier(0.23, 1, 0.32, 1)

export function MenuRow({
  icon,
  label,
  checked,
  disabled = false,
  danger = false,
  onPress,
}: {
  icon: IconName
  label: string
  checked?: boolean
  disabled?: boolean
  danger?: boolean
  onPress: () => void
}) {
  const { colors } = useTheme()
  const color = danger ? colors.errorText : colors.textPrimary
  return (
    <Pressable
      accessibilityRole="menuitem"
      accessibilityLabel={label}
      accessibilityState={checked === undefined ? { disabled } : { checked, disabled }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [styles.row, pressed && { backgroundColor: colors.roundFill }, disabled && styles.disabled]}
    >
      <Icon name={icon} size={20} color={color} />
      <Txt numberOfLines={1} style={[styles.label, { color }]}>
        {label}
      </Txt>
      {checked ? <Icon name="check" size={18} color={colors.textPrimary} strokeWidth={2} /> : null}
    </Pressable>
  )
}

export function PopoverMenu({
  visible,
  label,
  title,
  anchor,
  align = 'center',
  onClose,
  children,
}: {
  visible: boolean
  label: string
  title?: string
  anchor: { top: number } | { bottom: number }
  align?: 'center' | 'end'
  onClose: () => void
  children: ReactNode
}) {
  const { colors } = useTheme()
  const [progress] = useState(() => new Animated.Value(0))
  const below = 'top' in anchor
  useEffect(() => {
    if (!visible) {
      progress.setValue(0)
      return
    }
    Animated.timing(progress, { toValue: 1, duration: 180, easing: OPEN, useNativeDriver: nativeDriver }).start()
  }, [progress, visible])
  return (
    <Modal transparent visible={visible} animationType="none" onRequestClose={onClose} statusBarTranslucent navigationBarTranslucent>
      <Pressable style={StyleSheet.absoluteFill} onPress={onClose} accessible={false} />
      <View style={[styles.place, anchor, align === 'end' ? styles.end : below ? styles.centered : null]}>
        <Animated.View
          accessibilityRole="menu"
          accessibilityLabel={label}
          style={[
            styles.card,
            {
              borderColor: colors.borderSubtle,
              backgroundColor: colors.bgElevated,
              boxShadow: colors.shadow3,
              opacity: progress,
              transform: [{ translateY: progress.interpolate({ inputRange: [0, 1], outputRange: [below ? -8 : 8, 0] }) }],
            },
          ]}
        >
          {title ? (
            <Txt numberOfLines={1} style={[styles.title, { color: colors.textSecondary }]}>
              {title}
            </Txt>
          ) : null}
          {children}
        </Animated.View>
      </View>
    </Modal>
  )
}

const styles = StyleSheet.create({
  place: { position: 'absolute', left: metrics.gutter, right: metrics.gutter, pointerEvents: 'box-none' },
  centered: { alignItems: 'center' },
  end: { alignItems: 'flex-end' },
  title: { paddingHorizontal: 12, paddingTop: 10, paddingBottom: 6 },
  disabled: { opacity: 0.4 },
  card: { width: 264, maxWidth: '80%', padding: 6, borderWidth: 1, borderRadius: metrics.heroRadius },
  row: { flexDirection: 'row', alignItems: 'center', gap: 14, minHeight: 50, paddingHorizontal: 12, borderRadius: metrics.rowRadius },
  label: { flex: 1, minWidth: 0, fontWeight: '500' },
})

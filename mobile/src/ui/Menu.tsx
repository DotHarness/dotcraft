import { useEffect, useState, type ReactNode } from 'react'
import { Animated, Easing, Modal, Pressable, StyleSheet, View } from 'react-native'
import { Icon, type IconName } from './icons'
import { nativeDriver, Txt } from './parts'
import { metrics, useTheme } from './theme'

const OPEN = Easing.bezier(0.23, 1, 0.32, 1)

export function MenuRow({ icon, label, checked, onPress }: { icon: IconName; label: string; checked?: boolean; onPress: () => void }) {
  const { colors } = useTheme()
  return (
    <Pressable
      accessibilityRole="menuitem"
      accessibilityLabel={label}
      accessibilityState={checked === undefined ? undefined : { checked }}
      onPress={onPress}
      style={({ pressed }) => [styles.row, pressed && { backgroundColor: colors.roundFill }]}
    >
      <Icon name={icon} size={20} color={colors.textPrimary} />
      <Txt numberOfLines={1} style={styles.label}>
        {label}
      </Txt>
      {checked ? <Icon name="check" size={18} color={colors.textPrimary} strokeWidth={2} /> : null}
    </Pressable>
  )
}

export function PopoverMenu({
  visible,
  label,
  anchor,
  onClose,
  children,
}: {
  visible: boolean
  label: string
  anchor: { top: number } | { bottom: number }
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
      <View style={[styles.place, anchor, below ? styles.centered : null]}>
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
          {children}
        </Animated.View>
      </View>
    </Modal>
  )
}

const styles = StyleSheet.create({
  place: { position: 'absolute', left: metrics.gutter, right: metrics.gutter, pointerEvents: 'box-none' },
  centered: { alignItems: 'center' },
  card: { width: 264, maxWidth: '80%', padding: 6, borderWidth: 1, borderRadius: metrics.heroRadius },
  row: { flexDirection: 'row', alignItems: 'center', gap: 14, minHeight: 50, paddingHorizontal: 12, borderRadius: metrics.rowRadius },
  label: { flex: 1, minWidth: 0, fontWeight: '500' },
})

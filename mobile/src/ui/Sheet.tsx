import { useEffect, useState, type ReactNode } from 'react'
import { Animated, Easing, Modal, Pressable, ScrollView, StyleSheet, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useI18n } from '../i18n'
import { nativeDriver, PhoneButton, RoundIconButton, Txt } from './parts'
import { metrics, type, useTheme } from './theme'

const EXPAND = Easing.bezier(0.23, 1, 0.32, 1)
const EXPAND_MS = 240

export function SheetLayer({
  visible,
  onClose,
  alert = false,
  children,
}: {
  visible: boolean
  onClose: () => void
  alert?: boolean
  children: ReactNode
}) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  const [progress] = useState(() => new Animated.Value(0))
  useEffect(() => {
    if (!visible) {
      progress.setValue(0)
      return
    }
    Animated.timing(progress, { toValue: 1, duration: EXPAND_MS, easing: EXPAND, useNativeDriver: nativeDriver }).start()
  }, [progress, visible])
  return (
    <Modal transparent visible={visible} animationType="none" onRequestClose={onClose} statusBarTranslucent navigationBarTranslucent>
      <View style={styles.layer}>
        <Animated.View style={[StyleSheet.absoluteFill, { backgroundColor: colors.overlayScrim, opacity: progress }]}>
          <Pressable style={StyleSheet.absoluteFill} onPress={onClose} accessible={false} />
        </Animated.View>
        <Animated.View
          accessibilityViewIsModal
          accessibilityRole={alert ? 'alert' : undefined}
          style={[
            styles.sheet,
            {
              paddingBottom: insets.bottom + 12,
              backgroundColor: colors.bgElevated,
              boxShadow: colors.shadow3,
              opacity: progress,
              transform: [{ translateY: progress.interpolate({ inputRange: [0, 1], outputRange: [32, 0] }) }],
            },
          ]}
        >
          <View style={[styles.grabber, { backgroundColor: colors.borderActive }]} />
          <ScrollView bounces={false} showsVerticalScrollIndicator={false} contentContainerStyle={styles.sheetContent}>
            {children}
          </ScrollView>
        </Animated.View>
      </View>
    </Modal>
  )
}

export function SheetHeader({ title, onClose }: { title: string; onClose: () => void }) {
  const { t } = useI18n()
  return (
    <View style={styles.head}>
      <Txt accessibilityRole="header" style={[type.sheetTitle, styles.shrink]}>
        {title}
      </Txt>
      <RoundIconButton label={t('common.close')} icon="x" onPress={onClose} />
    </View>
  )
}

export function ConfirmSheet({
  visible,
  title,
  text,
  confirmLabel,
  danger,
  onCancel,
  onConfirm,
}: {
  visible: boolean
  title: string
  text: string
  confirmLabel: string
  danger: boolean
  onCancel: () => void
  onConfirm: () => void
}) {
  const { t } = useI18n()
  return (
    <SheetLayer visible={visible} onClose={onCancel} alert>
      <View style={styles.centerHead}>
        <Txt accessibilityRole="header" style={[type.sheetTitle, styles.centered]}>
          {title}
        </Txt>
      </View>
      <Txt tone="secondary" style={styles.centered}>
        {text}
      </Txt>
      <View style={styles.decision}>
        <PhoneButton variant={danger ? 'danger' : 'primary'} onPress={onConfirm}>
          {confirmLabel}
        </PhoneButton>
        <PhoneButton variant="ghost" onPress={onCancel}>
          {t('common.cancel')}
        </PhoneButton>
      </View>
    </SheetLayer>
  )
}

const styles = StyleSheet.create({
  layer: { flex: 1, justifyContent: 'flex-end' },
  sheet: {
    maxHeight: '88%',
    paddingTop: 8,
    paddingHorizontal: metrics.gutter,
    borderTopLeftRadius: metrics.heroRadius,
    borderTopRightRadius: metrics.heroRadius,
  },
  sheetContent: { gap: 12 },
  grabber: { alignSelf: 'center', width: 36, height: 5, borderRadius: 3, marginBottom: 16 },
  head: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 12 },
  centerHead: { paddingTop: 8, alignItems: 'center' },
  centered: { textAlign: 'center' },
  shrink: { flexShrink: 1 },
  decision: { gap: 8, marginTop: 4 },
})

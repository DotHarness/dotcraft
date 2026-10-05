import { useEffect, useRef, useState, type ReactNode } from 'react'
import {
  Animated,
  Easing,
  Modal,
  Pressable,
  ScrollView,
  StyleSheet,
  View,
  type GestureResponderEvent,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useI18n } from '../i18n'
import { nativeDriver, PhoneButton, Txt } from './parts'
import { metrics, type, useTheme } from './theme'

const EXPAND = Easing.bezier(0.23, 1, 0.32, 1)
const EXPAND_MS = 240
const SETTLE_MS = 220
const DISMISS_PX = 96
const DISMISS_VELOCITY = 0.8
const FLING_VELOCITY = 0.5
const FIT_SHARE = 0.88

interface Drag {
  from: number
  startY: number
  lastY: number
  lastAt: number
  velocity: number
}

function SheetPanel({
  progress,
  space,
  alert,
  onClose,
  children,
}: {
  progress: Animated.Value
  space: number
  alert: boolean
  onClose: () => void
  children: ReactNode
}) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  const [height] = useState(() => new Animated.Value(0))
  const [drop] = useState(() => new Animated.Value(0))
  const [mode, setMode] = useState<'fit' | 'held' | 'full'>('fit')
  const natural = useRef(0)
  const expanded = useRef(false)
  const drag = useRef<Drag>({ from: 0, startY: 0, lastY: 0, lastAt: 0, velocity: 0 })
  const full = space - insets.top

  const animate = (value: Animated.Value, to: number, done?: () => void) =>
    Animated.timing(value, { toValue: to, duration: SETTLE_MS, easing: EXPAND, useNativeDriver: false }).start(done)

  const settle = (next: 'fit' | 'full') =>
    animate(height, next === 'full' ? full : natural.current, () => {
      expanded.current = next === 'full'
      setMode(next)
    })

  const grant = (event: GestureResponderEvent) => {
    const { pageY } = event.nativeEvent
    const from = expanded.current ? full : natural.current
    drag.current = { from, startY: pageY, lastY: pageY, lastAt: event.nativeEvent.timestamp, velocity: 0 }
    height.setValue(from)
    setMode('held')
  }

  const move = (event: GestureResponderEvent) => {
    const { pageY, timestamp } = event.nativeEvent
    const current = drag.current
    if (timestamp > current.lastAt) current.velocity = (pageY - current.lastY) / (timestamp - current.lastAt)
    current.lastY = pageY
    current.lastAt = timestamp
    const next = current.from - (pageY - current.startY)
    height.setValue(Math.min(Math.max(next, natural.current), full))
    drop.setValue(Math.max(natural.current - next, 0))
  }

  const release = () => {
    const { from, startY, lastY, velocity } = drag.current
    const next = from - (lastY - startY)
    if (next < natural.current) {
      if (natural.current - next > DISMISS_PX || velocity > DISMISS_VELOCITY) {
        animate(drop, natural.current, onClose)
        return
      }
      animate(drop, 0)
      settle('fit')
      return
    }
    const fling = Math.abs(velocity) > FLING_VELOCITY
    settle((fling ? velocity < 0 : next > (natural.current + full) / 2) ? 'full' : 'fit')
  }

  return (
    <Animated.View
      onLayout={({ nativeEvent }) => {
        if (mode === 'fit') natural.current = nativeEvent.layout.height
      }}
      style={[{ transform: [{ translateY: drop }] }, mode === 'fit' ? (space > 0 ? { maxHeight: space * FIT_SHARE } : null) : { height }]}
    >
      <Animated.View
        accessibilityViewIsModal
        accessibilityRole={alert ? 'alert' : undefined}
        style={[
          styles.sheet,
          mode !== 'fit' && styles.fill,
          mode === 'full' && styles.square,
          {
            paddingBottom: insets.bottom + 12,
            backgroundColor: colors.bgElevated,
            boxShadow: colors.shadow3,
            opacity: progress,
            transform: [{ translateY: progress.interpolate({ inputRange: [0, 1], outputRange: [32, 0] }) }],
          },
        ]}
      >
        <View
          onStartShouldSetResponder={() => true}
          onResponderTerminationRequest={() => false}
          onResponderGrant={grant}
          onResponderMove={move}
          onResponderRelease={release}
          onResponderTerminate={release}
          style={styles.handle}
        >
          <View style={[styles.grabber, { backgroundColor: colors.borderActive }]} />
        </View>
        <ScrollView bounces={false} showsVerticalScrollIndicator={false} contentContainerStyle={styles.sheetContent}>
          {children}
        </ScrollView>
      </Animated.View>
    </Animated.View>
  )
}

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
  const { t } = useI18n()
  const { colors } = useTheme()
  const [progress] = useState(() => new Animated.Value(0))
  const [space, setSpace] = useState(0)
  useEffect(() => {
    if (!visible) {
      progress.setValue(0)
      return
    }
    Animated.timing(progress, { toValue: 1, duration: EXPAND_MS, easing: EXPAND, useNativeDriver: nativeDriver }).start()
  }, [progress, visible])
  return (
    <Modal transparent visible={visible} animationType="none" onRequestClose={onClose} statusBarTranslucent navigationBarTranslucent>
      <View style={styles.layer} onLayout={({ nativeEvent }) => setSpace(nativeEvent.layout.height)}>
        <Animated.View style={[StyleSheet.absoluteFill, { backgroundColor: colors.overlayScrim, opacity: progress }]}>
          <Pressable style={StyleSheet.absoluteFill} onPress={onClose} accessibilityRole="button" accessibilityLabel={t('common.close')} />
        </Animated.View>
        {visible ? (
          <SheetPanel progress={progress} space={space} alert={alert} onClose={onClose}>
            {children}
          </SheetPanel>
        ) : null}
      </View>
    </Modal>
  )
}

export function SheetHeader({ title, action }: { title: string; action?: ReactNode }) {
  return (
    <View style={styles.head}>
      <Txt accessibilityRole="header" style={[type.sheetTitle, styles.shrink]}>
        {title}
      </Txt>
      {action}
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
    flexShrink: 1,
    borderTopLeftRadius: metrics.heroRadius,
    borderTopRightRadius: metrics.heroRadius,
  },
  fill: { flex: 1 },
  square: { borderTopLeftRadius: 0, borderTopRightRadius: 0 },
  handle: { alignSelf: 'stretch', alignItems: 'center', paddingTop: 8, paddingBottom: 16 },
  sheetContent: { gap: 12, paddingHorizontal: metrics.gutter },
  grabber: { width: 36, height: 5, borderRadius: 3 },
  head: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 12 },
  centerHead: { paddingTop: 8, alignItems: 'center' },
  centered: { textAlign: 'center' },
  shrink: { flexShrink: 1 },
  decision: { gap: 8, marginTop: 4 },
})

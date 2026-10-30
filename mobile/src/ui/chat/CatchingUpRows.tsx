import { useEffect, useState } from 'react'
import { Animated, Easing, StyleSheet, View } from 'react-native'
import { useI18n } from '../../i18n'
import { useReducedMotion } from '../motion'
import { nativeDriver } from '../parts'
import { useTheme } from '../theme'

const PULSE_MS = 700
const LINES = ['92%', '78%', '54%'] as const

export function CatchingUpRows() {
  const { colors } = useTheme()
  const { t } = useI18n()
  const reduced = useReducedMotion()
  const [opacity] = useState(() => new Animated.Value(0.45))
  useEffect(() => {
    if (reduced) return
    const pulse = (toValue: number) => Animated.timing(opacity, { toValue, duration: PULSE_MS, easing: Easing.inOut(Easing.ease), useNativeDriver: nativeDriver })
    const loop = Animated.loop(Animated.sequence([pulse(0.85), pulse(0.45)]))
    loop.start()
    return () => loop.stop()
  }, [opacity, reduced])
  return (
    <View accessible accessibilityRole="progressbar" accessibilityLabel={t('chat.catchingUp')} aria-busy style={styles.rows}>
      {LINES.map((width) => (
        <Animated.View key={width} style={[styles.line, { width, opacity, backgroundColor: colors.bgTertiary }]} />
      ))}
    </View>
  )
}

const styles = StyleSheet.create({
  rows: { gap: 10, paddingVertical: 5 },
  line: { height: 10, borderRadius: 5 },
})

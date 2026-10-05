import { useEffect, useState } from 'react'
import { Animated, Easing, Pressable, StyleSheet, View } from 'react-native'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { nativeDriver } from '../parts'
import { useTheme } from '../theme'

const WAVE = [0, 0.1, 0.25, 0.55, 0.7]
const LIFT = [0, 0, 1.2, -2, 0]
const DELAYS = [0, 0.1, 0.2]

function WorkingDots() {
  const { colors } = useTheme()
  const [progress] = useState(() => new Animated.Value(0))
  useEffect(() => {
    const loop = Animated.loop(Animated.timing(progress, { toValue: 1, duration: 1000, easing: Easing.linear, useNativeDriver: nativeDriver }))
    loop.start()
    return () => loop.stop()
  }, [progress])
  return (
    <View style={styles.dots}>
      {DELAYS.map((delay) => (
        <Animated.View
          key={delay}
          style={[
            styles.dot,
            {
              backgroundColor: colors.textPrimary,
              transform: [
                {
                  translateY: progress.interpolate({
                    inputRange: [...WAVE.map((at) => at + delay), 1],
                    outputRange: [...LIFT, 0],
                    extrapolate: 'clamp',
                  }),
                },
              ],
            },
          ]}
        />
      ))}
    </View>
  )
}

export function ScrollToBottom({ visible, working, bottom, onPress }: { visible: boolean; working: boolean; bottom: number; onPress: () => void }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [shown] = useState(() => new Animated.Value(0))
  useEffect(() => {
    Animated.timing(shown, { toValue: visible ? 1 : 0, duration: 150, easing: Easing.inOut(Easing.ease), useNativeDriver: nativeDriver }).start()
  }, [shown, visible])
  return (
    <Animated.View pointerEvents={visible ? 'box-none' : 'none'} style={[styles.anchor, { bottom, opacity: shown }]}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('chat.scrollToBottom')}
        accessibilityElementsHidden={!visible}
        importantForAccessibility={visible ? 'auto' : 'no-hide-descendants'}
        hitSlop={8}
        onPress={onPress}
        style={({ pressed }) => [
          styles.button,
          { backgroundColor: colors.bgElevated, borderColor: colors.borderDefault, boxShadow: colors.shadow1 },
          pressed && { backgroundColor: colors.roundFillPressed },
        ]}
      >
        {working ? <WorkingDots /> : <Icon name="arrowDown" size={18} color={colors.textPrimary} />}
      </Pressable>
    </Animated.View>
  )
}

const styles = StyleSheet.create({
  anchor: { position: 'absolute', left: 0, right: 0, alignItems: 'center' },
  button: { width: 36, height: 36, borderRadius: 18, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
  dots: { flexDirection: 'row', alignItems: 'center', gap: 4 },
  dot: { width: 4, height: 4, borderRadius: 2, opacity: 0.7 },
})

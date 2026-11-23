import { useEffect, useState, type ReactNode } from 'react'
import { Animated, StyleSheet, View, type StyleProp, type ViewStyle } from 'react-native'
import { useReducedMotion } from '../motion'
import { nativeDriver } from '../parts'

const SWEEP_MS = 1000
const FADE_MS = 180

type Handoff = 'sweep' | 'fade'

export function ToolLabelHandoff({
  handoffKey,
  active,
  style,
  children,
}: {
  handoffKey: string
  active: boolean
  style?: StyleProp<ViewStyle>
  children: ReactNode
}) {
  const reduced = useReducedMotion()
  const [shown, setShown] = useState<{ key: string; active: boolean; handoff: Handoff | null; seq: number }>({
    key: handoffKey,
    active,
    handoff: null,
    seq: 0,
  })
  if (handoffKey !== shown.key) {
    const handoff: Handoff | null = reduced ? null : active ? 'sweep' : shown.active ? 'fade' : null
    setShown({ key: handoffKey, active, handoff, seq: shown.seq + 1 })
  } else if (active !== shown.active) {
    setShown({ ...shown, active })
  }

  const [width, setWidth] = useState(0)
  const [sweep] = useState(() => new Animated.Value(1))
  const [opacity] = useState(() => new Animated.Value(1))

  useEffect(() => {
    if (!shown.handoff) return
    sweep.setValue(shown.handoff === 'sweep' ? 0 : 1)
    opacity.setValue(0)
    const animation = Animated.parallel([
      Animated.timing(sweep, { toValue: 1, duration: shown.handoff === 'sweep' ? SWEEP_MS : 0, useNativeDriver: nativeDriver }),
      Animated.timing(opacity, { toValue: 1, duration: FADE_MS, useNativeDriver: nativeDriver }),
    ])
    animation.start()
    return () => animation.stop()
  }, [shown.seq, shown.handoff, sweep, opacity])

  const offset = width * 1.08
  const outer = sweep.interpolate({ inputRange: [0, 0.74, 1], outputRange: [-offset, 0, 0] })
  const inner = sweep.interpolate({ inputRange: [0, 0.74, 1], outputRange: [offset, 0, 0] })

  return (
    <Animated.View
      style={[style, styles.clip, { opacity, transform: [{ translateX: outer }] }]}
      onLayout={({ nativeEvent }) => setWidth(nativeEvent.layout.width)}
    >
      <Animated.View style={{ transform: [{ translateX: inner }] }}>
        <View>{children}</View>
      </Animated.View>
    </Animated.View>
  )
}

const styles = StyleSheet.create({
  clip: { overflow: 'hidden' },
})

import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Animated, StyleSheet, Text, View, type StyleProp, type TextStyle, type ViewStyle } from 'react-native'
import { useReducedMotion } from './motion'
import { nativeDriver } from './parts'
import { useTheme } from './theme'

const MAX_BAND = 80
const SPEED = 450
const MIN_SWEEP_MS = 1_000
const FIRST_SWEEP_MS = 600
const SWEEP_EVERY_MS = 4_000

const LAYERS = [
  { share: 1, opacity: 0.35 },
  { share: 0.6, opacity: 0.7 },
  { share: 0.25, opacity: 1 },
]

function bandOf(width: number): number {
  return Math.round(Math.min(MAX_BAND, width / 2))
}

export function RunningShimmer({
  active = true,
  numberOfLines = 1,
  style,
  textStyle,
  children,
}: {
  active?: boolean
  numberOfLines?: number
  style?: StyleProp<ViewStyle>
  textStyle?: StyleProp<TextStyle>
  children: ReactNode
}) {
  const { colors } = useTheme()
  const reduced = useReducedMotion()
  const [x] = useState(() => new Animated.Value(0))
  const [width, setWidth] = useState(0)
  const measured = useRef(0)
  const sweeps = active && !reduced

  useEffect(() => {
    if (!sweeps) return
    let animation: Animated.CompositeAnimation | null = null
    let interval: ReturnType<typeof setInterval> | undefined
    const sweep = () => {
      const span = Math.round(measured.current)
      if (span <= 0) return
      const band = bandOf(span)
      const distance = span + band
      x.setValue(-band)
      animation = Animated.timing(x, {
        toValue: span,
        duration: Math.max(MIN_SWEEP_MS, (distance / SPEED) * 1_000),
        easing: (progress) => Math.round(progress * distance) / distance,
        useNativeDriver: nativeDriver,
      })
      animation.start()
    }
    x.setValue(-MAX_BAND * 2)
    const start = setTimeout(() => {
      sweep()
      interval = setInterval(sweep, SWEEP_EVERY_MS)
    }, FIRST_SWEEP_MS)
    return () => {
      clearTimeout(start)
      clearInterval(interval)
      animation?.stop()
    }
  }, [sweeps, x])

  if (!active) {
    return (
      <View style={style}>
        <Text numberOfLines={numberOfLines} style={textStyle}>
          {children}
        </Text>
      </View>
    )
  }

  const band = bandOf(width)
  return (
    <View
      style={style}
      onLayout={({ nativeEvent }) => {
        measured.current = nativeEvent.layout.width
        setWidth(nativeEvent.layout.width)
      }}
    >
      <Text numberOfLines={numberOfLines} style={[textStyle, { color: colors.shimmerBase }]}>
        {children}
      </Text>
      {sweeps && width > 0
        ? LAYERS.map(({ share, opacity }) => {
            const left = Animated.add(x, Math.round((band * (1 - share)) / 2))
            return (
              <Animated.View
                key={share}
                aria-hidden
                accessibilityElementsHidden
                importantForAccessibility="no-hide-descendants"
                pointerEvents="none"
                style={[styles.window, { width: Math.round(band * share), opacity, transform: [{ translateX: left }] }]}
              >
                <Animated.View style={[styles.copy, { width, transform: [{ translateX: Animated.multiply(left, -1) }] }]}>
                  <Text numberOfLines={numberOfLines} style={[textStyle, { color: colors.shimmerPeak }]}>
                    {children}
                  </Text>
                </Animated.View>
              </Animated.View>
            )
          })
        : null}
    </View>
  )
}

const styles = StyleSheet.create({
  window: { position: 'absolute', top: 0, bottom: 0, left: 0, overflow: 'hidden' },
  copy: { position: 'absolute', top: 0, left: 0 },
})

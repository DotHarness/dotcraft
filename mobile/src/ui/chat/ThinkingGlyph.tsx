import { useIsFocused } from 'expo-router'
import { useEffect, useId, useState } from 'react'
import { AppState, PixelRatio, Platform } from 'react-native'
import Animated, { cancelAnimation, Easing, ReduceMotion, useAnimatedProps, useDerivedValue, useSharedValue, withRepeat, withTiming } from 'react-native-reanimated'
import Svg, { Circle, Defs, FeColorMatrix, FeGaussianBlur, Filter, G, type GProps } from 'react-native-svg'
import { useReducedMotion } from '../motion'
import { THINKING_LOOP_MS, THINKING_REST, thinkingPose, thinkingRotation } from './thinkingMotion'

const SIZE = 16
const WEB = Platform.OS === 'web'
const AnimatedGroup = Animated.createAnimatedComponent(G)
const AnimatedCircle = Animated.createAnimatedComponent(Circle)

export function ThinkingGlyph({ thinking, color }: { thinking: boolean; color: string }) {
  const filter = `thinking-glyph-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
  const edge = Math.min(20, Math.max(4, (SIZE * PixelRatio.get()) / 4))
  const reduced = useReducedMotion()
  const focused = useIsFocused()
  const [foreground, setForeground] = useState(() => AppState.currentState === 'active')
  const progress = useSharedValue(0)
  const pose = useDerivedValue(() => reduced || !thinking ? THINKING_REST : thinkingPose(progress.get()), [reduced, thinking])

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => setForeground(state === 'active'))
    return () => subscription.remove()
  }, [])

  useEffect(() => {
    if (thinking && !reduced && focused && foreground) {
      progress.set(0)
      progress.set(withRepeat(withTiming(1, { duration: THINKING_LOOP_MS, easing: Easing.linear, reduceMotion: ReduceMotion.Never }), -1))
    }
    return () => cancelAnimation(progress)
  }, [thinking, reduced, focused, foreground, progress])

  const rotation = useAnimatedProps<GProps & { matrix?: number[] }>(() => WEB
    ? { transform: `rotate(${pose.get().spin} 12 12)` }
    : { matrix: thinkingRotation(pose.get().spin) })
  const orbit = useAnimatedProps(() => ({ cy: 12 + pose.get().drop }))
  const center = useAnimatedProps(() => ({ r: 2.6 * pose.get().core }))

  return (
    <Svg width={SIZE} height={SIZE} viewBox="0 0 24 24">
      <Defs>
        <Filter id={filter} x="-50%" y="-50%" width="200%" height="200%">
          <FeGaussianBlur in="SourceGraphic" stdDeviation={1.15} result="blur" />
          <FeColorMatrix in="blur" type="matrix" values={`1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 ${edge} ${-0.4 * edge}`} />
        </Filter>
      </Defs>
      <G filter={`url(#${filter})`}>
        <AnimatedGroup animatedProps={rotation}>
          {[0, 120, 240].map((angle) => (
            <G key={angle} transform={`rotate(${angle} 12 12)`}>
              <AnimatedCircle cx={12} r={3.2} fill={color} animatedProps={orbit} />
            </G>
          ))}
        </AnimatedGroup>
        <AnimatedCircle cx={12} cy={12} fill={color} animatedProps={center} />
      </G>
    </Svg>
  )
}

import { useIsFocused } from 'expo-router'
import { useEffect, useId, useState } from 'react'
import { AppState, Platform } from 'react-native'
import Animated, { cancelAnimation, Easing, ReduceMotion, useAnimatedProps, useSharedValue, withRepeat, withTiming } from 'react-native-reanimated'
import Svg, { Circle, ClipPath, Defs, G, Path, Rect, type GProps, type PathProps } from 'react-native-svg'
import { TERMINAL_CURSOR, TERMINAL_PROMPT } from '../icons'
import {
  commandPose,
  glyphMatrix,
  leafPath,
  leafScale,
  meridianPath,
  meridianScale,
  scanPose,
  TOOL_GLYPH_LOOP_MS,
  type ToolGlyphMotion,
} from './toolGlyphMotion'

const SIZE = 16
const WEB = Platform.OS === 'web'
const AnimatedGroup = Animated.createAnimatedComponent(G)
const AnimatedPath = Animated.createAnimatedComponent(Path)
const OUTPUT_LINES = ['M7.5 8.5h9', 'M7.5 12h6', 'M7.5 15.5h7.5']
const BOOK = 'M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z'

function placed(matrix: number[]): GProps & { matrix?: number[] } {
  'worklet'
  return WEB ? { transform: `matrix(${matrix.join(' ')})` } : { matrix }
}

export function ToolActivityGlyph({ motion, color }: { motion: ToolGlyphMotion; color: string }) {
  const clip = `tool-glyph-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
  const focused = useIsFocused()
  const [foreground, setForeground] = useState(() => AppState.currentState === 'active')
  const progress = useSharedValue(0)

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => setForeground(state === 'active'))
    return () => subscription.remove()
  }, [])

  useEffect(() => {
    if (focused && foreground) {
      progress.set(0)
      progress.set(withRepeat(withTiming(1, { duration: TOOL_GLYPH_LOOP_MS[motion], easing: Easing.linear, reduceMotion: ReduceMotion.Never }), -1))
    }
    return () => cancelAnimation(progress)
  }, [motion, focused, foreground, progress])

  const prompt = useAnimatedProps(() => {
    const pose = commandPose(progress.get())
    return { ...placed(glyphMatrix(1, 0, pose.promptY)), opacity: pose.promptOpacity }
  })
  const cursor = useAnimatedProps<PathProps>(() => ({ opacity: commandPose(progress.get()).cursorOpacity }))
  const output = useAnimatedProps(() => {
    const pose = commandPose(progress.get())
    return { ...placed(glyphMatrix(1, 0, pose.outputY)), opacity: pose.outputOpacity }
  })
  const lens = useAnimatedProps(() => {
    const pose = scanPose(progress.get())
    return placed(glyphMatrix(1, pose.x, pose.y))
  })
  const leaf = useAnimatedProps<PathProps>(() => ({ d: leafPath(leafScale(progress.get())) }))
  const leading = useAnimatedProps<PathProps>(() => ({ d: meridianPath(meridianScale(progress.get(), false)) }))
  const trailing = useAnimatedProps<PathProps>(() => ({ d: meridianPath(meridianScale(progress.get(), true)) }))

  return (
    <Svg
      width={SIZE}
      height={SIZE}
      viewBox="0 0 24 24"
      style={{ flexShrink: 0 }}
      fill="none"
      stroke={color}
      strokeWidth={1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {motion === 'command' ? (
        <>
          <Defs>
            <ClipPath id={clip}>
              <Rect x={4} y={4} width={16} height={16} rx={3.5} />
            </ClipPath>
          </Defs>
          <Rect x={3} y={3} width={18} height={18} rx={4.5} />
          <G clipPath={`url(#${clip})`}>
            <AnimatedGroup animatedProps={prompt}>
              <Path d={TERMINAL_PROMPT} />
              <AnimatedPath d={TERMINAL_CURSOR} animatedProps={cursor} />
            </AnimatedGroup>
            <AnimatedGroup animatedProps={output}>
              {OUTPUT_LINES.map((d) => <Path key={d} d={d} />)}
            </AnimatedGroup>
          </G>
        </>
      ) : motion === 'search' ? (
        <AnimatedGroup animatedProps={lens}>
          <Path d="m21 21-4.34-4.34" />
          <Circle cx={11} cy={11} r={8} />
        </AnimatedGroup>
      ) : motion === 'read' ? (
        <>
          <Path d="M12 7v14" />
          <Path d={BOOK} />
          <AnimatedPath d={leafPath(1)} animatedProps={leaf} />
        </>
      ) : (
        <>
          <Circle cx={12} cy={12} r={10} />
          <Path d="M2 12h20" />
          <AnimatedPath d={meridianPath(0.4)} animatedProps={leading} />
          <AnimatedPath d={meridianPath(-0.4)} animatedProps={trailing} />
        </>
      )}
    </Svg>
  )
}

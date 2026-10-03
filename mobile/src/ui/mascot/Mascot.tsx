import { memo, useEffect, useMemo, useState } from 'react'
import { AccessibilityInfo, LogBox, StyleSheet, type StyleProp, type ViewStyle, View } from 'react-native'
import { Txt } from '../parts'
import MascotView, { type MascotPose } from './MascotView'

// Expo DOM components send props on mount without handling a native view that is already gone.
LogBox.ignoreLogs(["Call to function 'DomWebView.injectJavaScript' has been rejected"])

const MascotSurface = memo(MascotView)

const SETTLE_MS = 150

export type MascotMoment =
  | 'idle'
  | 'content'
  | 'look-around'
  | 'asleep'
  | 'question'
  | 'greeting'
  | 'celebrate'
  | 'working'
  | 'sad'
  | 'wary'
  | 'curious'

const POSE: Record<MascotMoment, MascotPose> = {
  idle: 'idle',
  content: 'idle',
  'look-around': 'idle',
  asleep: 'sleep',
  question: 'waiting',
  greeting: 'greeting',
  celebrate: 'done',
  working: 'working',
  sad: 'blocked',
  wary: 'blocked',
  curious: 'thinking',
}

function useReducedMotion(): boolean {
  const [reduced, setReduced] = useState(false)
  useEffect(() => {
    let mounted = true
    void AccessibilityInfo.isReduceMotionEnabled().then((value) => {
      if (mounted) setReduced(value)
    })
    const subscription = AccessibilityInfo.addEventListener('reduceMotionChanged', setReduced)
    return () => {
      mounted = false
      subscription.remove()
    }
  }, [])
  return reduced
}

function useSettled<T>(value: T): T {
  const [settled, setSettled] = useState(value)
  useEffect(() => {
    if (Object.is(settled, value)) return
    const timer = setTimeout(() => setSettled(value), SETTLE_MS)
    return () => clearTimeout(timer)
  }, [value, settled])
  return settled
}

export function Mascot({
  moment,
  size,
  profile,
  style,
}: {
  moment: MascotMoment
  size: number
  profile?: string | null
  style?: StyleProp<ViewStyle>
}) {
  const shown = useSettled(moment)
  const shownProfile = useSettled(profile ?? null)
  const reduced = useReducedMotion()
  const motion = useSettled(!reduced)
  const canvas = Math.round(size * 1.5)
  const inset = (canvas - size) / 2
  const dom = useMemo(
    () => ({
      style: { width: canvas, height: canvas, backgroundColor: 'transparent' },
      scrollEnabled: false,
      matchContents: false,
      importantForAccessibility: 'no-hide-descendants' as const,
    }),
    [canvas],
  )
  return (
    <View
      style={[{ width: size, height: size }, style]}
      pointerEvents="none"
      importantForAccessibility="no-hide-descendants"
      aria-hidden
    >
      <View style={[styles.canvas, { top: -inset, left: -inset, width: canvas, height: canvas }]}>
        <MascotSurface
          pose={POSE[shown]}
          happy={shown === 'content'}
          lookAround={shown === 'look-around' || shown === 'wary'}
          profile={shownProfile}
          size={size}
          canvas={canvas}
          motion={motion}
          dom={dom}
        />
      </View>
    </View>
  )
}

export function MascotTransition({ line, inList = false }: { line: string; inList?: boolean }) {
  return (
    <View
      style={[styles.transition, inList && styles.transitionInList]}
      accessibilityRole="progressbar"
      accessibilityLabel={line}
      accessibilityLiveRegion="polite"
    >
      <Mascot moment="working" size={72} />
      <Txt tone="secondary" style={styles.centered}>
        {line}
      </Txt>
    </View>
  )
}

export function MascotNote({ moment, profile, children }: { moment: MascotMoment; profile?: string | null; children: string }) {
  return (
    <View style={styles.note}>
      <Mascot moment={moment} profile={profile} size={56} />
      <Txt tone="secondary" style={styles.centered}>
        {children}
      </Txt>
    </View>
  )
}

const styles = StyleSheet.create({
  canvas: { position: 'absolute' },
  transition: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: 14,
    paddingTop: 24,
    paddingHorizontal: 16,
    paddingBottom: 48,
  },
  transitionInList: { flex: 0, paddingTop: 56 },
  note: { alignItems: 'center', gap: 10, paddingTop: 28, paddingHorizontal: 16, paddingBottom: 8 },
  centered: { textAlign: 'center', maxWidth: 300 },
})

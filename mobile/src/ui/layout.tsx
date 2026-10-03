import type { ReactNode } from 'react'
import { ScrollView, StyleSheet, View, type StyleProp, type ViewStyle } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { camera, metrics, useTheme } from './theme'

export function Screen({ children, tone = 'default', style }: { children: ReactNode; tone?: 'default' | 'camera'; style?: StyleProp<ViewStyle> }) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  return (
    <View
      style={[
        styles.screen,
        { paddingTop: insets.top, backgroundColor: tone === 'camera' ? camera.background : colors.bgPrimary },
        style,
      ]}
    >
      {children}
    </View>
  )
}

export function TopBar({ children, end = false }: { children?: ReactNode; end?: boolean }) {
  return <View style={[styles.topBar, end && styles.topBarEnd]}>{children}</View>
}

export function ScrollArea({ children, last = false }: { children: ReactNode; last?: boolean }) {
  const insets = useSafeAreaInsets()
  return (
    <ScrollView
      style={styles.scroll}
      contentContainerStyle={[styles.scrollContent, { paddingBottom: last ? insets.bottom + 24 : 24 }]}
      keyboardShouldPersistTaps="handled"
      showsVerticalScrollIndicator={false}
    >
      {children}
    </ScrollView>
  )
}

export function BottomBar({ children }: { children: ReactNode }) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  return <View style={[styles.bottomBar, { paddingBottom: insets.bottom + 6, backgroundColor: colors.bgPrimary }]}>{children}</View>
}

export function Hero({ children }: { children: ReactNode }) {
  return <View style={styles.hero}>{children}</View>
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
  topBar: { flexDirection: 'row', alignItems: 'center', minHeight: 52, paddingVertical: 4, paddingHorizontal: 12 },
  topBarEnd: { justifyContent: 'flex-end' },
  scroll: { flex: 1 },
  scrollContent: { paddingHorizontal: metrics.gutter },
  bottomBar: { flexDirection: 'row', gap: 10, paddingTop: 10, paddingHorizontal: metrics.gutter },
  hero: { paddingTop: 2, paddingBottom: 4 },
})

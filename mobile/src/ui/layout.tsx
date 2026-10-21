import { useCallback, useState, type ReactElement, type ReactNode } from 'react'
import { RefreshControl, ScrollView, StyleSheet, View, type RefreshControlProps, type ScrollViewProps, type StyleProp, type ViewStyle } from 'react-native'
import Svg, { Defs, LinearGradient, Rect, Stop } from 'react-native-svg'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useComputerLink } from '../app-state/SessionContext'
import { camera, metrics, useTheme } from './theme'

export function Screen({ children, tone = 'default', style }: { children: ReactNode; tone?: 'default' | 'camera'; style?: StyleProp<ViewStyle> }) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  return (
    <View
      style={[
        styles.screen,
        { paddingTop: insets.top, backgroundColor: tone === 'camera' ? camera.background : colors.surfaceBottom },
        style,
      ]}
    >
      {tone === 'camera' ? null : (
        <Svg style={StyleSheet.absoluteFill} width="100%" height="100%" pointerEvents="none" preserveAspectRatio="none" viewBox="0 0 1 1">
          <Defs>
            <LinearGradient id="screenSurface" x1="0" y1="0" x2="0" y2="1">
              <Stop offset="0" stopColor={colors.surfaceTop} />
              <Stop offset="1" stopColor={colors.surfaceBottom} />
            </LinearGradient>
          </Defs>
          <Rect x="0" y="0" width="1" height="1" fill="url(#screenSurface)" />
        </Svg>
      )}
      {children}
    </View>
  )
}

export function TopBar({ children }: { children?: ReactNode }) {
  return <View style={styles.topBar}>{children}</View>
}

export function useRefreshControl(chatKey?: string, offset?: number): ReactElement<RefreshControlProps> {
  const link = useComputerLink()
  const { colors } = useTheme()
  const [refreshing, setRefreshing] = useState(false)
  const onRefresh = useCallback(() => {
    setRefreshing(true)
    void link.refresh(chatKey).finally(() => setRefreshing(false))
  }, [chatKey, link])
  return (
    <RefreshControl
      refreshing={refreshing}
      onRefresh={onRefresh}
      colors={[colors.textSecondary]}
      tintColor={colors.textSecondary}
      progressBackgroundColor={colors.bgElevated}
      progressViewOffset={offset}
    />
  )
}

export function ScrollArea({
  children,
  last = false,
  onScroll,
  refreshControl,
}: {
  children: ReactNode
  last?: boolean
  onScroll?: ScrollViewProps['onScroll']
  refreshControl?: ScrollViewProps['refreshControl']
}) {
  const insets = useSafeAreaInsets()
  return (
    <ScrollView
      style={styles.scroll}
      refreshControl={refreshControl}
      contentContainerStyle={[styles.scrollContent, { paddingBottom: last ? insets.bottom + 24 : 24 }]}
      keyboardShouldPersistTaps="handled"
      showsVerticalScrollIndicator={false}
      onScroll={onScroll}
      scrollEventThrottle={onScroll ? 16 : undefined}
    >
      {children}
    </ScrollView>
  )
}

export function BottomBar({ children }: { children: ReactNode }) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  return <View style={[styles.bottomBar, { paddingBottom: insets.bottom + 6, backgroundColor: colors.surfaceBottom }]}>{children}</View>
}

export function Hero({ children }: { children: ReactNode }) {
  return <View style={styles.hero}>{children}</View>
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
  topBar: { alignSelf: 'stretch', flexDirection: 'row', alignItems: 'center', minHeight: 52, paddingVertical: 4, paddingHorizontal: 12 },
  scroll: { flex: 1 },
  scrollContent: { flexGrow: 1, paddingHorizontal: metrics.gutter },
  bottomBar: { flexDirection: 'row', gap: 10, paddingTop: 10, paddingHorizontal: metrics.gutter },
  hero: { paddingTop: 2, paddingBottom: 4 },
})

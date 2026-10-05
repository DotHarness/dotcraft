import { Stack, useRouter, useSegments } from 'expo-router'
import * as SplashScreen from 'expo-splash-screen'
import { StatusBar } from 'expo-status-bar'
import * as SystemUI from 'expo-system-ui'
import { useEffect } from 'react'
import { StyleSheet, useColorScheme, View } from 'react-native'
import { SafeAreaProvider } from 'react-native-safe-area-context'
import { createRuntime } from '../app-state/createRuntime'
import { RuntimeProvider, useMobileState } from '../app-state/SessionContext'
import { I18nContext, useDeviceI18n } from '../i18n'
import { ThemeContext, themes, useTheme } from '../ui/theme'

void SplashScreen.preventAutoHideAsync()

const LAUNCHED_AT = Date.now()
const SPLASH_MIN_MS = 500

const runtime = createRuntime()

function Gate() {
  const state = useMobileState()
  const router = useRouter()
  const segments = useSegments()
  const { colors } = useTheme()
  const top = segments[0] as string | undefined

  useEffect(() => {
    if (!state.hydrated) return
    const timer = setTimeout(() => void SplashScreen.hideAsync(), Math.max(0, SPLASH_MIN_MS - (Date.now() - LAUNCHED_AT)))
    return () => clearTimeout(timer)
  }, [state.hydrated])

  useEffect(() => {
    if (!state.hydrated || state.order.length > 0 || top === 'pair') return
    if (router.canDismiss()) router.dismissAll()
    router.replace('/pair')
  }, [router, state.hydrated, state.order.length, top])

  return (
    <View style={[styles.fill, { backgroundColor: colors.bgPrimary }]}>
      <Stack screenOptions={{ headerShown: false, animation: 'slide_from_right', contentStyle: { backgroundColor: colors.bgPrimary } }} />
      {state.hydrated ? null : <View style={[StyleSheet.absoluteFill, { backgroundColor: colors.bgPrimary }]} />}
    </View>
  )
}

export default function RootLayout() {
  const scheme = useColorScheme() === 'light' ? 'light' : 'dark'
  const theme = themes[scheme]
  const i18n = useDeviceI18n(runtime.locale)

  useEffect(() => {
    void SystemUI.setBackgroundColorAsync(theme.colors.bgPrimary)
  }, [theme])

  return (
    <SafeAreaProvider>
      <RuntimeProvider runtime={runtime}>
        <ThemeContext.Provider value={theme}>
          <I18nContext.Provider value={i18n}>
            <Gate />
            <StatusBar style={scheme === 'dark' ? 'light' : 'dark'} />
          </I18nContext.Provider>
        </ThemeContext.Provider>
      </RuntimeProvider>
    </SafeAreaProvider>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
})

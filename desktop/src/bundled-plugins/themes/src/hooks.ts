import type { DesktopPluginHost } from '@dotcraft/plugin'
import { useEffect, useState, useSyncExternalStore } from 'react'
import type { ThemeAppearance } from '../../../shared/themes'
import { getSettings, subscribeSettings, type ThemeSettings } from './settings'

export function useSettings(): ThemeSettings | null {
  return useSyncExternalStore(subscribeSettings, getSettings, getSettings)
}

export function useAppearance(host: DesktopPluginHost): ThemeAppearance {
  const [appearance, setAppearance] = useState<ThemeAppearance>(host.environment.theme)
  useEffect(() => {
    setAppearance(host.environment.theme)
    return host.environment.onChange((environment) => setAppearance(environment.theme))
  }, [host])
  return appearance
}

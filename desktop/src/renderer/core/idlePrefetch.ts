import { prefetchSettingsPanel } from '../components/settings/coreSettingsPanels'
import { prefetchCoreMainView } from './coreMainViewRoutes'

const BACKGROUND_READY_DELAY_MS = 5_000
const IDLE_TIMEOUT_MS = 2_000

export function scheduleIdlePrefetch(): () => void {
  let idleHandle: number | null = null
  const timer = window.setTimeout(() => {
    idleHandle = window.requestIdleCallback(() => {
      idleHandle = null
      prefetchCoreMainView('settings')
      prefetchSettingsPanel('appearance')
    }, { timeout: IDLE_TIMEOUT_MS })
  }, BACKGROUND_READY_DELAY_MS)
  return () => {
    window.clearTimeout(timer)
    if (idleHandle !== null) window.cancelIdleCallback(idleHandle)
  }
}

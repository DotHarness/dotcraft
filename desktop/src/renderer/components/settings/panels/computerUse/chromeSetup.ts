import type { ChromeSetupStatus } from '../../../../../preload/api'
import type { useT } from '../../../../contexts/LocaleContext'
import type { StatusTone } from '../../../ui/StatusIndicator'

type Translate = ReturnType<typeof useT>

export type ChromeSetupTone = 'ok' | 'warning' | 'error' | 'muted'

export const CHROME_EXTENSIONS_URL = 'chrome://extensions'

function asRecord(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

export function setupResultOk(value: unknown): boolean {
  return asRecord(value)?.ok === true
}

export function setupResultText(value: unknown, key: string): string {
  const candidate = asRecord(value)?.[key]
  return typeof candidate === 'string' ? candidate : ''
}

export function chromeSetupSummary(
  status: ChromeSetupStatus | null,
  t: Translate
): { label: string; tone: ChromeSetupTone } {
  if (!status) return { label: t('settings.chrome.status.notChecked'), tone: 'muted' }
  if (!setupResultOk(status.installedBrowsers)) return { label: t('settings.chrome.status.chromeMissing'), tone: 'error' }
  if (!setupResultOk(status.extension)) return { label: t('settings.chrome.status.extensionMissing'), tone: 'error' }
  if (!setupResultOk(status.nativeHost)) return { label: t('settings.chrome.status.nativeHostMissing'), tone: 'warning' }
  if (!setupResultOk(status.chromeRunning)) return { label: t('settings.chrome.status.notRunning'), tone: 'warning' }
  if (!setupResultOk(status.backend)) return { label: t('settings.chrome.status.backendDisconnected'), tone: 'warning' }
  return { label: t('settings.chrome.status.connected'), tone: 'ok' }
}

export function chromeStatusTone(tone: ChromeSetupTone): StatusTone {
  if (tone === 'ok') return 'success'
  if (tone === 'muted') return 'neutral'
  return tone
}

export function chromeNativeHostActionLabel(status: ChromeSetupStatus | null, t: Translate): string {
  const nativeHost = asRecord(status?.nativeHost)
  const safeDetails = asRecord(nativeHost?.safeDetails)
  if (
    status &&
    !setupResultOk(nativeHost) &&
    safeDetails?.exists === false &&
    safeDetails?.hostExists === false
  ) {
    return t('settings.chrome.installHost')
  }
  return t('settings.chrome.repairHost')
}

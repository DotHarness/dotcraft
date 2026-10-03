import type { AppLocale } from '../../../../../shared/locales'
import type { MobileDevice, MobileFailure } from '../../../../../shared/mobile'

type Translate = (key: string, vars?: Record<string, string | number>) => string

const PLATFORM_NAMES: Record<MobileDevice['platform'], string> = {
  ios: 'iOS',
  android: 'Android'
}

export function platformLabel(device: MobileDevice): string {
  const name = PLATFORM_NAMES[device.platform]
  return device.osVersion ? `${name} ${device.osVersion}` : name
}

export function addedDay(value: string, locale: AppLocale): string {
  const date = new Date(value)
  return date.toLocaleDateString(
    locale,
    date.getFullYear() === new Date().getFullYear()
      ? { month: 'short', day: 'numeric' }
      : { year: 'numeric', month: 'short', day: 'numeric' }
  )
}

export function failureReason(
  t: Translate,
  failure: Partial<MobileFailure>,
  fallbackKey: string,
  port?: number
): string {
  if (failure.code === 'portUnavailable' && port) return t('settings.phones.failed.portUnavailable', { port })
  if (failure.code === 'gatewayOff') return t('settings.phones.pair.gatewayOff')
  if (failure.code === 'hubUnavailable') return t('settings.phones.hubNoAnswer')
  return failure.message || t(fallbackKey)
}

export function formatCountdown(seconds: number): string {
  const minutes = Math.floor(seconds / 60)
  return `${minutes}:${String(seconds % 60).padStart(2, '0')}`
}

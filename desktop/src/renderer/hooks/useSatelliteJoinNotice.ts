import { useEffect, useRef } from 'react'
import { useLocale } from '../contexts/LocaleContext'
import { translate } from '../../shared/locales'
import { showToast } from '../stores/toastStore'

export function useSatelliteJoinNotice(): void {
  const locale = useLocale()
  const localeRef = useRef(locale)
  localeRef.current = locale
  useEffect(() => window.api.satellites.onJoinLink(link => {
    if (link.forwarded) return
    showToast({
      type: 'info',
      key: 'satellite-join-link',
      message: translate(localeRef.current, 'satellite.toast.joinLink.message'),
      description: translate(localeRef.current, 'satellite.toast.joinLink.description')
    })
  }), [])
}

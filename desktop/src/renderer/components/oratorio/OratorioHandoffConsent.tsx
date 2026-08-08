import { useEffect, useRef } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { OratorioHandoffRequest } from '../../../shared/oratorio'
import { useLocale } from '../../contexts/LocaleContext'

export function OratorioHandoffConsent(): null {
  const locale = useLocale()
  const activeRequest = useRef<string | null>(null)

  useEffect(() => {
    async function present(handoff: OratorioHandoffRequest): Promise<void> {
      if (activeRequest.current === handoff.requestId) return
      activeRequest.current = handoff.requestId
      const approved = await requestConfirmation({
        title: translate(locale as AppLocale, 'appBinding.title'),
        message: `${handoff.summary}\n\n${handoff.workspacePath}`,
        confirmLabel: translate(locale as AppLocale, handoff.operation === 'connect' ? 'appBinding.connect' : 'appBinding.bindThread'),
        cancelLabel: translate(locale as AppLocale, 'settings.browserUse.cancel')
      })
      try { await window.api.oratorio.resolveHandoff(handoff.requestId, approved) } finally { activeRequest.current = null }
    }
    const api = window.api.oratorio
    if (!api) return
    void api.getPendingHandoff().then((handoff) => { if (handoff) void present(handoff) })
    return api.onEvent((event) => { if (event.type === 'handoff-requested' && event.handoff) void present(event.handoff) })
  }, [locale])
  return null
}

function requestConfirmation(options: { title: string; message: string; confirmLabel: string; cancelLabel: string }): Promise<boolean> {
  const trigger = (window as Window & { __confirmDialog?: (value: typeof options) => Promise<boolean> }).__confirmDialog
  return trigger ? trigger(options) : Promise.resolve(false)
}

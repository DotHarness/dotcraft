import { useEffect, useState } from 'react'
import {
  OratorioSettingsPanel,
  type OratorioSettingsView
} from './settings/oratorio-settings'
import './oratorio.css'

export function OratorioSettingsSurface(_props: { host?: unknown; viewId?: string }): JSX.Element {
  const [view, setView] = useState<OratorioSettingsView>('root')
  const [serviceError, setServiceError] = useState(false)
  const [remote, setRemote] = useState(false)

  useEffect(() => {
    let active = true
    void window.api.oratorio.getContext()
      .then((context) => { if (active) { setServiceError(false); setRemote(context.provider === 'remote') } })
      .catch(() => { if (active) setServiceError(true) })
    const unsubscribe = window.api.oratorio.onEvent(() => {
      void window.api.oratorio.getContext()
        .then(() => { if (active) setServiceError(false) })
        .catch(() => { if (active) setServiceError(true) })
    })
    return () => {
      active = false
      unsubscribe()
    }
  }, [])

  return <OratorioSettingsPanel view={view} serviceError={serviceError} readOnly={remote} onViewChange={setView} />
}

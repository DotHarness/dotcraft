import { useEffect, useState } from 'react'
import type { DesktopPluginViewProps } from '@dotcraft/plugin'
import {
  OratorioSettingsPanel,
  type OratorioSettingsView
} from './settings/oratorio-settings'
import {
  consumeOratorioNavigation,
  onOratorioNavigation,
  type OratorioNavigationTarget
} from './oratorio-navigation'
import './oratorio.css'

export function OratorioSettingsSurface({ host }: DesktopPluginViewProps): JSX.Element {
  const [view, setView] = useState<OratorioSettingsView>('root')
  const [serviceError, setServiceError] = useState(false)
  const [remote, setRemote] = useState(false)

  useEffect(() => {
    let active = true
    void host.oratorio.getContext()
      .then((context) => { if (active) { setServiceError(false); setRemote(context.provider === 'remote') } })
      .catch(() => { if (active) setServiceError(true) })
    const unsubscribe = host.oratorio.onEvent(() => {
      void host.oratorio.getContext()
        .then(() => { if (active) setServiceError(false) })
        .catch(() => { if (active) setServiceError(true) })
    })
    return () => {
      active = false
      unsubscribe()
    }
  }, [host])

  useEffect(() => {
    const navigate = (target: OratorioNavigationTarget): void => {
      if (target.kind !== 'settings') return
      setView(target.section === 'github' || target.section === 'gitlab' ? target.section : 'root')
    }
    const pending = consumeOratorioNavigation()
    if (pending) navigate(pending)
    return onOratorioNavigation(navigate)
  }, [])

  return <OratorioSettingsPanel view={view} serviceError={serviceError} readOnly={remote} onViewChange={setView} />
}

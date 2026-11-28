import type { DesktopPluginActivate } from '@dotcraft/plugin'

import { OratorioSettingsSurface } from './OratorioSettingsSurface'
import { OratorioView } from './OratorioView'
import { OratorioBatonIcon } from './OratorioBatonIcon'
import { ConductorMascot, ConductorPermissions, conductorSpace, findConductor, requestConductorGuide } from './conductor'
import { buildConductorMenuEntries, createConductorComposerGate } from './conductor-lifecycle'
import { createConductorProactiveController } from './conductor-proactive'
import { ConductorIntro } from './ConductorIntro'
import { parseOratorioNavigationUrl, requestOratorioNavigation } from './oratorio-navigation'
import { installOratorioHost } from './runtime'

const labels = {
  default: 'Oratorio',
  translations: {
    'zh-Hans': 'Oratorio',
    ja: 'Oratorio',
    ko: 'Oratorio',
    es: 'Oratorio',
    fr: 'Oratorio',
    de: 'Oratorio'
  }
}

export const activate: DesktopPluginActivate = (host) => {
  const uninstallHost = installOratorioHost(host)
  const proactive = createConductorProactiveController(host)
  const lifecycleHooks = { proactive }

  host.effect(() => proactive.start())
  host.navigation.onOpenUrl((url) => {
    const target = parseOratorioNavigationUrl(url)
    if (!target) return false
    requestOratorioNavigation(target)
    if (target.kind === 'settings') host.navigation.openSettingsPage('oratorio')
    else host.navigation.openMainView('board')
    return true
  })
  host.ui.wrap('composer', createConductorComposerGate(lifecycleHooks))
  host.ui.wrap('composer.mascot', ConductorMascot)
  host.ui.wrap('composer.toolbar.permissions', ConductorPermissions)
  host.ui.add('conversation.empty', ConductorIntro)
  host.ui.addThreadMenuItems((context) =>
    context.threadId === findConductor(host)?.threadId ? buildConductorMenuEntries(host, lifecycleHooks) : []
  )
  const onSessionChange = (): void => {
    void requestConductorGuide(host).catch(() => {})
  }
  host.session.onChange(onSessionChange)
  onSessionChange()
  void host.oratorio.getContext().catch(() => {})

  return {
    mainViews: [{ id: 'board', label: labels, icon: OratorioBatonIcon, order: 55, component: OratorioView }],
    spaces: [conductorSpace(labels, lifecycleHooks)],
    settingsPages: [{ id: 'oratorio', label: labels, icon: OratorioBatonIcon, order: 45, component: OratorioSettingsSurface }],
    dispose() {
      uninstallHost()
    }
  }
}

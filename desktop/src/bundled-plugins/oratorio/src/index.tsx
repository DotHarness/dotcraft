import type {
  DesktopPluginActivate,
  DesktopPluginOratorioHandoffRequest
} from '@dotcraft/plugin'

import { OratorioSettingsSurface } from './OratorioSettingsSurface'
import { OratorioView } from './OratorioView'
import { OratorioBatonIcon } from './OratorioBatonIcon'
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

const handoffCopy = {
  title: ['Connect Oratorio', '连接 Oratorio', 'Oratorio に接続', 'Oratorio 연결', 'Conectar Oratorio', 'Connecter Oratorio', 'Oratorio verbinden'],
  connect: ['Connect', '连接', '接続', '연결', 'Conectar', 'Connecter', 'Verbinden'],
  bind: ['Bind thread', '绑定线程', 'スレッドを紐付ける', '스레드 연결', 'Vincular hilo', 'Lier le fil', 'Thread verknüpfen'],
  cancel: ['Cancel', '取消', 'キャンセル', '취소', 'Cancelar', 'Annuler', 'Abbrechen']
} as const
const locales = ['en', 'zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const

export const activate: DesktopPluginActivate = (host) => {
  const uninstallHost = installOratorioHost(host)
  let disposed = false
  let activeRequestId: string | null = null

  const presentHandoff = async (handoff: DesktopPluginOratorioHandoffRequest): Promise<void> => {
    if (activeRequestId === handoff.requestId) return
    activeRequestId = handoff.requestId
    const localeIndex = Math.max(0, locales.indexOf(host.environment.locale as typeof locales[number]))
    const approved = await host.ui.confirm({
      title: handoffCopy.title[localeIndex],
      message: `${handoff.summary}\n\n${handoff.workspacePath}`,
      confirmLabel: handoffCopy[handoff.operation][localeIndex],
      cancelLabel: handoffCopy.cancel[localeIndex]
    })
    if (!disposed) await host.oratorio.resolveHandoff(handoff.requestId, approved)
    activeRequestId = null
  }

  host.navigation.onOpenUrl((url) => {
    const target = parseOratorioNavigationUrl(url)
    if (!target) return false
    requestOratorioNavigation(target)
    if (target.kind === 'settings') host.navigation.openSettingsPage('oratorio')
    else host.navigation.openMainView('board')
    return true
  })
  host.oratorio.onEvent((event) => {
    if (event.type === 'handoff-requested' && event.handoff) void presentHandoff(event.handoff)
  })
  void host.oratorio.getPendingHandoff().then((handoff) => {
    if (handoff) void presentHandoff(handoff)
  })

  return {
    mainViews: [{ id: 'board', label: labels, icon: OratorioBatonIcon, order: 55, component: OratorioView }],
    settingsPages: [{ id: 'oratorio', label: labels, icon: OratorioBatonIcon, order: 45, component: OratorioSettingsSurface }],
    dispose() {
      disposed = true
      uninstallHost()
    }
  }
}

import type { DesktopPluginActivate } from '@dotcraft/plugin'
import { localizedLabel } from './i18n'
import { MemoryIcon } from './MemoryIcon'
import { MemoryView } from './MemoryView'
import { MemoryPolicySettings } from './policy/MemoryPolicySettings'
import { installMemoryStudioHost } from './runtime'
import { requestOpen } from './state/openRequests'
import { MEMORY_VIEW_ID, MemorySources } from './summary/MemorySources'

export const activate: DesktopPluginActivate = (host) => {
  host.effect(() => installMemoryStudioHost(host))
  host.ui.add('summary.sources', MemorySources)
  host.ui.wrap('settings.personalization', MemoryPolicySettings)
  return {
    mainViews: [{ id: MEMORY_VIEW_ID, label: localizedLabel('view.title'), icon: MemoryIcon, order: 60, component: MemoryView }],
    messageActions: [{
      id: 'remember',
      label: localizedLabel('action.remember'),
      icon: MemoryIcon,
      order: 60,
      isAvailable: (message) => message.text.trim().length > 0,
      execute: (message, actionHost) => {
        requestOpen({ kind: 'new', body: message.text })
        actionHost.navigation.openMainView(MEMORY_VIEW_ID)
      }
    }]
  }
}

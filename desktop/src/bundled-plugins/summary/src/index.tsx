import type { DesktopPluginActivate } from '@dotcraft/plugin'
import { translationsOf } from './i18n'
import { initializeSettings } from './settings'
import { SummaryAside } from './SummaryAside'
import { SummaryHeaderAction } from './SummaryHeaderAction'
import { SummaryIcon } from './SummaryIcon'
import { resetView, toggleSummary } from './view'
import './styles/index.css'

export const activate: DesktopPluginActivate = async (host) => {
  await initializeSettings(host.settings)
  host.effect(() => resetView)

  host.ui.add('thread.header.actions', SummaryHeaderAction)
  host.ui.add('conversation.aside.trailing', SummaryAside)

  return {
    commands: [
      {
        id: 'toggle',
        label: { default: 'Toggle summary', translations: translationsOf('toggleCommand') },
        description: {
          default: 'Shows or hides the summary beside the conversation.',
          translations: translationsOf('toggleDescription')
        },
        icon: SummaryIcon,
        isAvailable: (context) => context.threadId !== null,
        execute: toggleSummary
      }
    ]
  }
}

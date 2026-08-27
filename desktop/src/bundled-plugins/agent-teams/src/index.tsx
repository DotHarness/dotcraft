import type { DesktopPluginActivate } from '@dotcraft/plugin'

import { TeamsView } from './TeamsView'

export const activate: DesktopPluginActivate = () => ({
  mainViews: [
    {
      id: 'teams',
      label: {
        default: 'Team',
        translations: {
          'zh-Hans': '团队',
          ja: 'チーム',
          ko: '팀',
          es: 'Equipo',
          fr: 'Équipe',
          de: 'Team'
        }
      },
      order: 40,
      component: TeamsView
    }
  ]
})

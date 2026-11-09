import { useMemo } from 'react'
import { usePluginStore } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { pluginIconUrl } from '../plugins/PluginCatalogItem'
import type { SlashSkillInfo } from './CommandSearchPopover'

export function useSlashSkills(): SlashSkillInfo[] {
  const skills = useSkillsStore((s) => s.skills)
  const plugins = usePluginStore((s) => s.plugins)
  return useMemo(
    () =>
      skills
        .map((skill) => {
          const plugin = skill.pluginId ? plugins.find((entry) => entry.id === skill.pluginId) : undefined
          return {
            name: skill.name.replace(/^\/+/, ''),
            description: skill.description,
            iconUrl: skill.iconSmallDataUrl || skill.iconLargeDataUrl || (plugin && pluginIconUrl(plugin)) || null
          }
        })
        .sort((a, b) => a.name.localeCompare(b.name)),
    [plugins, skills]
  )
}

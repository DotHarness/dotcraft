import { useMemo } from 'react'
import { usePluginStore } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { normalizeSkillName, skillIconUrl } from '../../utils/skillIcon'
import type { SlashSkillInfo } from './CommandSearchPopover'

export function useSlashSkills(): SlashSkillInfo[] {
  const skills = useSkillsStore((s) => s.skills)
  const plugins = usePluginStore((s) => s.plugins)
  return useMemo(
    () =>
      skills
        .map((skill) => ({
          name: normalizeSkillName(skill.name),
          description: skill.description,
          iconUrl: skillIconUrl(skill, plugins)
        }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    [plugins, skills]
  )
}

import { pluginIconUrl } from '../components/plugins/PluginCatalogItem'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useSkillsStore, type SkillEntry } from '../stores/skillsStore'

export function normalizeSkillName(name: string): string {
  return name.replace(/^\/+/, '')
}

export function skillIconUrl(skill: SkillEntry, plugins: PluginEntry[]): string | null {
  const plugin = skill.pluginId ? plugins.find((entry) => entry.id === skill.pluginId) : undefined
  return skill.iconSmallDataUrl || skill.iconLargeDataUrl || (plugin && pluginIconUrl(plugin)) || null
}

function findSkillIconUrl(name: string, skills: SkillEntry[], plugins: PluginEntry[]): string | null {
  const target = normalizeSkillName(name)
  if (!target) return null
  const skill = skills.find((entry) => normalizeSkillName(entry.name) === target)
  return skill ? skillIconUrl(skill, plugins) : null
}

export function getSkillIconUrl(name: string): string | null {
  return findSkillIconUrl(name, useSkillsStore.getState().skills, usePluginStore.getState().plugins)
}

export function useSkillIconUrl(name: string | null | undefined): string | null {
  const skills = useSkillsStore((s) => s.skills)
  const plugins = usePluginStore((s) => s.plugins)
  return name ? findSkillIconUrl(name, skills, plugins) : null
}

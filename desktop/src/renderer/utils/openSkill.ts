import { usePluginStore } from '../stores/pluginStore'
import { useSkillsStore } from '../stores/skillsStore'
import { useUIStore } from '../stores/uiStore'

export async function openSkill(name: string): Promise<void> {
  usePluginStore.getState().clearSelection()
  const ui = useUIStore.getState()
  ui.setPluginCatalogSurface('skills')
  ui.setActiveMainView('skills')
  const skills = useSkillsStore.getState()
  await skills.fetchSkills()
  await skills.selectSkill(name)
}

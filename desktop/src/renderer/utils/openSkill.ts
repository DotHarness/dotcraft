import { useSkillDetailStore } from '../stores/skillDetailStore'
import { useSkillsStore } from '../stores/skillsStore'
import { openActiveThreadFile } from './desktopNavigation'

export function openSkill(name: string): void {
  useSkillDetailStore.getState().open(name)
}

export async function openSkillFile(name: string, file: string): Promise<void> {
  const listedPath = () => useSkillsStore.getState().skills.find((skill) => skill.name === name)?.path
  if (!listedPath()) await useSkillsStore.getState().fetchSkills()
  const path = listedPath()
  if (path && isSamePath(path, file)) {
    openSkill(name)
    return
  }
  try {
    await openActiveThreadFile(file)
  } catch {
    openSkill(name)
  }
}

function isSamePath(left: string, right: string): boolean {
  const normalize = (value: string) => value.replace(/\\/g, '/')
  const a = normalize(left)
  const b = normalize(right)
  return /^[A-Za-z]:\//.test(a) ? a.toLowerCase() === b.toLowerCase() : a === b
}

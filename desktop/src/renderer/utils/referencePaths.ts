import { resolveConversationLink } from '../../shared/viewer/linkResolver'
import { useSkillsStore, type SkillEntry } from '../stores/skillsStore'

export function resolveLocalReferencePath(target: string, workspacePath: string): string | null {
  const resolution = resolveConversationLink({
    target,
    workspacePath
  })
  if (resolution.kind !== 'file') return null
  return isAbsoluteLocalPath(resolution.absolutePath) ? resolution.absolutePath : null
}

export async function resolveSkillReferencePath(skillName: string): Promise<string | null> {
  const normalized = normalizeSkillName(skillName)
  if (!normalized) return null

  const existing = findSkillPath(useSkillsStore.getState().skills, normalized)
  if (existing) return existing

  try {
    await useSkillsStore.getState().fetchSkills()
  } catch {
    return null
  }

  return findSkillPath(useSkillsStore.getState().skills, normalized)
}

function findSkillPath(skills: SkillEntry[], normalizedName: string): string | null {
  const skill = skills.find((entry) => normalizeSkillName(entry.name) === normalizedName)
  const skillPath = skill?.path?.trim()
  return skillPath ? skillPath : null
}

function normalizeSkillName(value: string): string {
  return value.trim().replace(/^\$+/, '').toLowerCase()
}

function isAbsoluteLocalPath(value: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(value) || value.startsWith('/') || value.startsWith('\\\\')
}

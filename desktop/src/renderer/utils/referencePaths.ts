import type { OpenTargetLocation } from '../../shared/openTargets'
import { resolveConversationLink, type LinkNavigationHint } from '../../shared/viewer/linkResolver'
import { useSkillsStore, type SkillEntry } from '../stores/skillsStore'

export interface LocalReference {
  path: string
  location: OpenTargetLocation | null
}

export function referenceLocation(hint: LinkNavigationHint | undefined): OpenTargetLocation | null {
  return hint?.line ? { line: hint.line, column: hint.column ?? 1 } : null
}

export function resolveLocalReference(target: string, workspacePath: string): LocalReference | null {
  const resolution = resolveConversationLink({
    target,
    workspacePath
  })
  if (resolution.kind !== 'file' || !isAbsoluteLocalPath(resolution.absolutePath)) return null
  return { path: resolution.absolutePath, location: referenceLocation(resolution.hint) }
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

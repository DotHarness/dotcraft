import { translate, type AppLocale, type MessageKey } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { resolveSkillOperation, type SkillOperation, type SkillOperationMark } from './toolKind'

const COMPLETED_LABEL_KEYS: Readonly<Record<SkillOperation, MessageKey>> = {
  read: 'toolCall.skill.read',
  create: 'toolCall.skill.created',
  update: 'toolCall.skill.updated'
}

const RUNNING_LABEL_KEYS: Readonly<Record<SkillOperation, MessageKey>> = {
  read: 'toolCall.skill.reading',
  create: 'toolCall.skill.creating',
  update: 'toolCall.skill.updating'
}

export function skillOperationLabelKey(operation: SkillOperation, running: boolean): MessageKey {
  return (running ? RUNNING_LABEL_KEYS : COMPLETED_LABEL_KEYS)[operation]
}

export function formatSkillOperationLabel(mark: SkillOperationMark, locale: AppLocale, running = false): string {
  return translate(locale, skillOperationLabelKey(mark.operation, running), { name: mark.skillName })
}

export type SkillActivity = 'loaded' | 'created' | 'updated'

/**
 * What a group of Skill calls did, counting each Skill once. A Skill created in the group counts as
 * created even when later calls update it, mirroring how file writes are counted.
 */
export function summarizeSkillActivity(
  items: readonly Pick<ConversationItem, 'id' | 'toolName' | 'presentation' | 'source' | 'arguments'>[]
): { activity: SkillActivity; count: number } {
  const loaded = new Set<string>()
  const written = new Map<string, boolean>()
  for (const item of items) {
    const mark = resolveSkillOperation(item)
    if (!mark) {
      const name = typeof item.arguments?.name === 'string' ? item.arguments.name.trim() : ''
      loaded.add(name || item.id)
      continue
    }
    const name = mark.skillName || item.id
    if (mark.operation === 'read') loaded.add(name)
    else written.set(name, written.get(name) === true || mark.operation === 'create')
  }
  if (written.size === 0) return { activity: 'loaded', count: loaded.size }
  const allCreated = [...written.values()].every(Boolean)
  return { activity: allCreated ? 'created' : 'updated', count: written.size }
}

import type { ConversationItem } from '../../types/conversation'
import { useSkillIconUrl } from '../../utils/skillIcon'
import { getToolRowIcon, type ToolRowIcon } from '../../utils/toolCallDisplay'
import { resolveToolKind } from '../../utils/toolKind'

type ToolRowIconItem = Parameters<typeof getToolRowIcon>[0]

function loadedSkillName(item: ToolRowIconItem | undefined): string | null {
  if (!item || resolveToolKind(item) !== 'skillView') return null
  const name = item.arguments?.name
  return typeof name === 'string' ? name.trim() || null : null
}

export function useToolRowIcon(item: ConversationItem | undefined): ToolRowIcon | undefined {
  const imageUrl = useSkillIconUrl(loadedSkillName(item))
  if (!item) return undefined
  const icon = getToolRowIcon(item)
  return imageUrl ? { ...icon, imageUrl } : icon
}

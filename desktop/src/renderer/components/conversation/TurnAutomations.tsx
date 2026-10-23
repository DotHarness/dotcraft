import { memo } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import type { AutomationDefinition } from '../../types/automation'
import type { ConversationTurn } from '../../types/conversation'
import { resolveCoreToolRenderPlan } from '../../utils/toolRendererRegistry'
import { AutomationCard } from './AutomationCard'
import { parseAutomationResult } from './AutomationToolCard'

function turnAutomations(turn: ConversationTurn): AutomationDefinition[] {
  const latest = new Map<string, AutomationDefinition>()
  for (const item of turn.items) {
    if (item.type !== 'toolCall' || item.status !== 'completed' || item.success === false) continue
    if (resolveCoreToolRenderPlan(item)?.family !== 'automation') continue
    const parsed = parseAutomationResult(item.result)
    if (parsed?.operation === 'delete' && parsed.automation) latest.delete(parsed.automation.id)
    if ((parsed?.operation !== 'create' && parsed?.operation !== 'update') || !parsed.automation) continue
    latest.delete(parsed.automation.id)
    latest.set(parsed.automation.id, parsed.automation)
  }
  return [...latest.values()]
}

export const TurnAutomations = memo(function TurnAutomations({ turnId }: { turnId: string }): JSX.Element | null {
  const turn = useConversationStore((s) => s.turns.find((t) => t.id === turnId))
  const automations = turn ? turnAutomations(turn) : []
  if (automations.length === 0) return null
  return (
    <div className="dc-turn-automations">
      {automations.map((automation) => <AutomationCard key={automation.id} automation={automation} />)}
    </div>
  )
})

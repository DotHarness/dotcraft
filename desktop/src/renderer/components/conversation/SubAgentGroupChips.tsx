import type { ConversationItem } from '../../types/conversation'
import { ToolCallCard, type ShellRuntimeScope } from './ToolCallCard'
import { SubAgentChips, getSubAgentChipDisplay } from './SubAgentChips'

/** Items that are not a recognisable spawn fall back to ordinary tool rows. */
export function SubAgentGroupChips({
  items,
  threadId,
  turnId,
  turnRunning,
  shellRuntimeScope
}: {
  items: ConversationItem[]
  threadId: string
  turnId: string
  turnRunning: boolean
  shellRuntimeScope: ShellRuntimeScope
}): JSX.Element {
  const chipItems = items.filter((item) => getSubAgentChipDisplay(item) != null)
  const rest = items.filter((item) => getSubAgentChipDisplay(item) == null)

  return (
    <>
      {chipItems.length > 0 && (
        <SubAgentChips items={chipItems} parentThreadId={threadId} turnRunning={turnRunning} />
      )}
      {rest.map((item) => (
        <ToolCallCard
          key={item.id}
          item={item}
          threadId={threadId}
          turnId={turnId}
          turnRunning={turnRunning}
          shellRuntimeScope={shellRuntimeScope}
        />
      ))}
    </>
  )
}

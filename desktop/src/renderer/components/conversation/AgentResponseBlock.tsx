import { memo, useEffect, useState, type CSSProperties, type ReactNode } from 'react'
import { Info } from 'lucide-react'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import { isToolLikeItemType } from '../../types/conversation'
import { ThinkingIndicator } from './ThinkingIndicator'
import { renderSubAgentTitle, ToolCallCard } from './ToolCallCard'
import { AgentMessage } from './AgentMessage'
import { ErrorBlock } from './ErrorBlock'
import { CancelledNotice } from './CancelledNotice'
import { TurnCompletionSummary } from './TurnCompletionSummary'
import { TurnArtifacts } from './TurnArtifacts'
import { ApprovalCard } from './ApprovalCard'
import { SystemNoticeBlock } from './SystemNoticeBlock'
import { UserMessageBlock } from './UserMessageBlock'
import { planToolRunRender } from '../../utils/toolCallAggregation'
import type { AggregatedToolCall } from '../../utils/toolCallAggregation'
import type { ToolGroupCategory } from '../../utils/toolCallAggregation'
import { isToolItemLive } from '../../utils/toolCallAggregation'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import { ToolCollapseChevron } from './ToolCollapseChevron'
import { useLocale } from '../../contexts/LocaleContext'
import { formatToolGroupLabel } from '../../utils/toolGroupLabel'
import { TurnCollapsedSummary } from './TurnCollapsedSummary'
import { translate, type AppLocale } from '../../../shared/locales'
import { formatSubAgentMeta, getSubAgentAccent } from '../../utils/subAgentPresentation'
import type { StreamRetrySignal } from '../../stores/conversationStore'
import type { SubAgentEntry } from '../../types/toolCall'

interface AgentResponseBlockProps {
  turn: ConversationTurn
  /** Live streaming text (only set for the active turn while running) */
  streamingMessage?: string
  /** Wall-clock ms when the latest live assistant text delta arrived. */
  streamingMessageLastDeltaAt?: number | null
  /** Live reasoning text (only set for the active turn while reasoning) */
  streamingReasoning?: string
  /** Whether this is the currently running turn */
  isRunning?: boolean
  /** Transient provider stream retry rows for this running turn. */
  streamRetrySignals?: StreamRetrySignal[]
  /** Whether this is the active turn that may be in waitingApproval */
  isActiveTurn?: boolean
  /** Whether this turn is the latest turn in the rendered thread. */
  isLastTurn?: boolean
  /** Show a UI-only Thinking row when an active running turn has no live visible work. */
  showIdleThinkingFallback?: boolean
  /**
   * When set, used for streaming item highlight instead of the main conversation store
   * (e.g. automation task review panel).
   */
  activeItemIdOverride?: string | null
  /** Scoped to automation review surfaces that do not use the global conversation store. */
  subAgentEntriesOverride?: SubAgentEntry[]
  /**
   * Main conversation optimization for older history: keep assistant/user text
   * and plans visible while avoiding historical tool-detail component mounts.
   */
  historicalToolContentMode?: HistoricalToolContentMode
}

export type HistoricalToolContentMode = 'full' | 'trimmed'

type ConversationNodeKind = 'assistant' | 'tool' | 'user' | 'other'

const STREAMING_MESSAGE_STALL_MS = 2000

interface ConversationRenderNode {
  kind: ConversationNodeKind
  node: ReactNode
}

/**
 * Renders agent-side content for a single turn in **chronological item order**.
 *
 * Each item type is rendered inline as it appears in `turn.items`:
 *   reasoningContent → ThinkingIndicator
 *   toolCall (consecutive runs aggregated) → ToolCallCard / GroupedToolCallRow
 *   agentMessage → AgentMessage
 *   error → ErrorBlock
 *
 * Streaming agentMessage / reasoningContent items are represented as placeholder
 * rows in `turn.items` (status `streaming`) and rendered inline using the live
 * buffers so order matches committed items (e.g. tool calls after streaming text).
 *
 * Spec §10.3.3
 */
export const AgentResponseBlock = memo(function AgentResponseBlock({
  turn,
  streamingMessage = '',
  streamingMessageLastDeltaAt = null,
  streamingReasoning = '',
  isRunning = false,
  streamRetrySignals = [],
  isActiveTurn = false,
  showIdleThinkingFallback = false,
  activeItemIdOverride,
  historicalToolContentMode = 'full'
}: AgentResponseBlockProps): JSX.Element {
  const pendingApproval = useConversationStore((s) => s.pendingApproval)
  const activeItemIdFromStore = useConversationStore((s) => s.activeItemId)
  const showThinkingContent = useUIStore((s) => s.showThinkingContent)
  const activeItemId =
    activeItemIdOverride !== undefined ? activeItemIdOverride : activeItemIdFromStore

  const trimHistoricalToolContent = historicalToolContentMode === 'trimmed'
  const hydratedItems = hydrateToolCallItems(turn.items)
  const fullRenderableItems = hydratedItems.filter(isDefaultRenderableItem)

  // Exclude user messages and toolResult items (toolResults are merged into their
  // parent toolCall items before rendering, not rendered independently)
  const renderableItems = trimHistoricalToolContent
    ? fullRenderableItems.filter(isTrimmedHistoryRenderableItem)
    : fullRenderableItems

  const renderItemSequence = (
    itemsToRender: ConversationItem[],
    keyPrefix = ''
  ): ConversationRenderNode[] => {
    const nodes: ConversationRenderNode[] = []
    let i = 0

    while (i < itemsToRender.length) {
      const item = itemsToRender[i]

      if (isToolLikeItemType(item.type)) {
        const toolRun: ConversationItem[] = [item]
        while (
          i + 1 < itemsToRender.length
          && isToolLikeItemType(itemsToRender[i + 1].type)
        ) {
          i++
          toolRun.push(itemsToRender[i])
        }
        const isTrailingRun = i + 1 >= itemsToRender.length
        const { entries } = planToolRunRender(toolRun, { isRunning, isTrailingRun })

        const toolRunNodes = entries.map((entry, offset) =>
          renderAggregatedEntry(
            entry,
            turn.id,
            offset,
            isRunning,
            `${keyPrefix}-tool-run-${item.id}`
          )
        )

        if (toolRunNodes.length > 0) {
          nodes.push({
            kind: 'tool',
            node: (
              <ToolRunStack key={`tool-run-${keyPrefix}-${item.id}`}>
                {toolRunNodes}
              </ToolRunStack>
            )
          })
        }
      } else if (item.type === 'userMessage' && item.deliveryMode === 'guidance') {
        nodes.push({
          kind: 'user',
          node: (
            <UserMessageBlock
              key={item.id}
              text={item.text ?? ''}
              nativeInputParts={item.nativeInputParts}
              imageDataUrls={item.imageDataUrls}
              images={item.images}
              createdAt={item.createdAt}
              deliveryMode={item.deliveryMode}
              triggerKind={item.triggerKind}
              triggerLabel={item.triggerLabel}
              triggerRefId={item.triggerRefId}
            />
          )
        })
      } else if (item.type === 'reasoningContent') {
        const isLiveStreaming =
          isRunning && item.status === 'streaming' && item.id === activeItemId
        if (!showThinkingContent && !isLiveStreaming) {
          i++
          continue
        }
        const displayReasoning = isLiveStreaming ? streamingReasoning : (item.reasoning ?? '')
        nodes.push({
          kind: 'assistant',
          node: (
            <ThinkingIndicator
              key={item.id}
              elapsedSeconds={item.elapsedSeconds}
              reasoning={showThinkingContent ? displayReasoning : undefined}
              streaming={isLiveStreaming}
            />
          )
        })
      } else if (item.type === 'agentMessage') {
        const isLiveStreaming =
          isRunning && item.status === 'streaming' && item.id === activeItemId
        const displayText = isLiveStreaming ? streamingMessage : (item.text ?? '')
        if (displayText.trim().length === 0) {
          i++
          continue
        }
        nodes.push({
          kind: 'assistant',
          node: (
            <AgentMessage
              key={item.id}
              text={displayText}
              streaming={isLiveStreaming}
              createdAt={item.createdAt}
              showFooter={item.id === footerAgentMessageId}
            />
          )
        })
      } else if (item.type === 'error') {
        nodes.push({
          kind: 'other',
          node: <ErrorBlock key={item.id} message={item.text ?? 'Unknown error'} />
        })
      } else if (item.type === 'approvalCard') {
        const isActiveApproval = isActiveTurn && pendingApproval?.itemId === item.id
        nodes.push({
          kind: 'other',
          node: (
            <ApprovalCard
              key={item.id}
              item={item}
              isActive={isActiveApproval}
            />
          )
        })
      } else if (item.type === 'systemNotice') {
        nodes.push({
          kind: 'other',
          node: <SystemNoticeBlock key={item.id} item={item} />
        })
      }

      i++
    }

    return nodes
  }

  const renderItemAndRetrySequence = (
    itemsToRender: ConversationItem[],
    retrySignals: StreamRetrySignal[],
    keyPrefix = ''
  ): ConversationRenderNode[] => {
    if (retrySignals.length === 0) {
      return renderItemSequence(itemsToRender, keyPrefix)
    }

    const nodes: ConversationRenderNode[] = []
    const sortedSignals = [...retrySignals].sort(
      (a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt)
    )
    let itemStart = 0

    sortedSignals.forEach((signal, signalIndex) => {
      const signalMs = Date.parse(signal.createdAt)
      let insertIndex = itemStart
      while (insertIndex < itemsToRender.length) {
        const itemMs = Date.parse(itemsToRender[insertIndex].createdAt)
        if (Number.isFinite(signalMs) && Number.isFinite(itemMs) && itemMs > signalMs) break
        insertIndex++
      }

      nodes.push(...renderItemSequence(
        itemsToRender.slice(itemStart, insertIndex),
        `${keyPrefix}-before-retry-${signalIndex}`
      ))
      nodes.push({
        kind: 'tool',
        node: <StreamRetryRow key={signal.id} signal={signal} />
      })
      itemStart = insertIndex
    })

    nodes.push(...renderItemSequence(
      itemsToRender.slice(itemStart),
      `${keyPrefix}-after-retry`
    ))
    return nodes
  }

  const collapseSourceItems = trimHistoricalToolContent ? fullRenderableItems : renderableItems
  const lastFinalAgentMessageIndex =
    !isRunning &&
    turn.status === 'completed' &&
    !collapseSourceItems.some(isGuidanceUserMessage)
      ? findLastAgentMessageIndex(collapseSourceItems)
      : -1
  const lastAgentMessageIndex =
    !isRunning && turn.status === 'completed'
      ? findLastAgentMessageIndex(renderableItems)
      : -1
  const footerAgentMessageId =
    lastAgentMessageIndex >= 0 ? renderableItems[lastAgentMessageIndex]?.id : null
  const hiddenHistoricalItems = trimHistoricalToolContent && !collapseSourceItems.some(isGuidanceUserMessage)
    ? collapseSourceItems.filter((item) => !isTrimmedHistoryRenderableItem(item))
    : []
  const shouldCollapseIntermediate = trimHistoricalToolContent
    ? hiddenHistoricalItems.length > 0
    : lastFinalAgentMessageIndex > 0
  const renderNodes: ConversationRenderNode[] = []
  const streamingMessageStalled = useStreamingMessageStall({
    enabled:
      showIdleThinkingFallback &&
      isRunning &&
      streamingMessage.trim().length > 0 &&
      hasActiveStreamingAgentMessage(renderableItems, activeItemId),
    lastDeltaAt: streamingMessageLastDeltaAt
  })
  const shouldShowIdleThinkingFallback = showIdleThinkingFallback && shouldRenderIdleThinkingFallback({
    items: renderableItems,
    isRunning,
    activeItemId,
    streamingMessage,
    streamingMessageStalled
  })

  if (shouldCollapseIntermediate) {
    let intermediateNodes: ConversationRenderNode[]
    let pinnedPlanNodes: ConversationRenderNode[] = []
    let trailingNodes: ConversationRenderNode[]

    if (trimHistoricalToolContent) {
      intermediateNodes = renderItemSequence(hiddenHistoricalItems, 'trimmed-history-details')
      trailingNodes = renderItemSequence(renderableItems)
    } else {
      const pinnedPlanIndex = findLastCreatePlanIndexBefore(renderableItems, lastFinalAgentMessageIndex)
      const pinnedPlanItem = pinnedPlanIndex >= 0 ? renderableItems[pinnedPlanIndex] : null
      const intermediateItems = pinnedPlanItem
        ? [
            ...renderableItems.slice(0, pinnedPlanIndex),
            ...renderableItems.slice(pinnedPlanIndex + 1, lastFinalAgentMessageIndex)
          ]
        : renderableItems.slice(0, lastFinalAgentMessageIndex)
      const trailingItems = renderableItems.slice(lastFinalAgentMessageIndex)

      intermediateNodes = pinnedPlanItem
        ? [
            ...renderItemSequence(
              renderableItems.slice(0, pinnedPlanIndex),
              'before-pinned-plan'
            ),
            ...renderItemSequence(
              renderableItems.slice(pinnedPlanIndex + 1, lastFinalAgentMessageIndex),
              'after-pinned-plan'
            )
          ]
        : renderItemSequence(intermediateItems)
      pinnedPlanNodes = pinnedPlanItem
        ? renderItemSequence([pinnedPlanItem], 'pinned-plan')
        : []
      trailingNodes = renderItemSequence(trailingItems)
    }

    if (intermediateNodes.length > 0) {
      const elapsedMs = getIntermediateElapsedMs(turn, collapseSourceItems[lastFinalAgentMessageIndex])
      renderNodes.push({
        kind: 'other',
        node: (
          <TurnCollapsedSummary
            key={`turn-collapsed-${turn.id}`}
            elapsedMs={elapsedMs}
          >
            <ConversationNodeFlow nodes={intermediateNodes} defaultGap="var(--conversation-block-gap)" />
          </TurnCollapsedSummary>
        )
      })
    }

    renderNodes.push(...pinnedPlanNodes)
    renderNodes.push(...trailingNodes)
  } else {
    renderNodes.push(...renderItemAndRetrySequence(
      renderableItems,
      isRunning ? streamRetrySignals : []
    ))
  }

  if (shouldShowIdleThinkingFallback) {
    renderNodes.push({
      kind: 'assistant',
      node: <ThinkingIndicator key={`idle-thinking-${turn.id}`} streaming />
    })
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--conversation-tool-assistant-gap)' }}>
      <ConversationNodeFlow nodes={renderNodes} defaultGap="var(--conversation-tool-assistant-gap)" />

      {/* Turn-level failure */}
      {turn.status === 'failed' && turn.error && (
        <ErrorBlock message={turn.error} />
      )}

      {/* Cancellation notice */}
      {turn.status === 'cancelled' && (
        <CancelledNotice reason={turn.cancelReason} />
      )}

      {/* Turn completion artifacts and file changes */}
      {!trimHistoricalToolContent && turn.status === 'completed' && (
        <>
          <TurnArtifacts turnId={turn.id} />
          <TurnCompletionSummary turnId={turn.id} />
        </>
      )}
    </div>
  )
})

// ── Render helpers ────────────────────────────────────────────────────────────

function ConversationNodeFlow({
  nodes,
  defaultGap
}: {
  nodes: ConversationRenderNode[]
  defaultGap: string
}): JSX.Element | null {
  if (nodes.length === 0) return null

  return (
    <div style={conversationFlowStyle}>
      {nodes.map((entry, index) => {
        const previous = nodes[index - 1]
        const marginTop =
          index > 0
            ? previous?.kind === 'tool' || entry.kind === 'tool'
              ? 'var(--conversation-tool-run-gap)'
              : defaultGap
            : undefined

        return (
          <div
            key={index}
            data-testid="conversation-flow-item"
            data-kind={entry.kind}
            style={{
              ...(entry.kind === 'user' ? userFlowItemStyle : {}),
              ...(marginTop ? { marginTop } : {})
            }}
          >
            {entry.node}
          </div>
        )
      })}
    </div>
  )
}

function ToolRunStack({ children }: { children: ReactNode }): JSX.Element {
  return (
    <div
      data-testid="tool-run-stack"
      style={toolRunStackStyle}
    >
      {children}
    </div>
  )
}

function StreamRetryRow({ signal }: { signal: StreamRetrySignal }): JSX.Element {
  const locale = useLocale()
  const label = formatStreamRetryLabel(signal, locale)

  return (
    <div
      data-testid="stream-retry-row"
      role="status"
      aria-live="polite"
      aria-label={label}
      style={streamRetryRowStyle}
    >
      <Info size={15} strokeWidth={1.8} aria-hidden="true" style={streamRetryIconStyle} />
      <span style={streamRetryLabelStyle}>{label}</span>
    </div>
  )
}

function formatStreamRetryLabel(signal: StreamRetrySignal, locale: AppLocale): string {
  if (signal.attempt != null && signal.max != null) {
    return translate(locale, 'conversation.streamRetry.reconnecting', {
      attempt: signal.attempt,
      max: signal.max
    })
  }

  return signal.rawMessage
}

const conversationFlowStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 0
}

const userFlowItemStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column'
}

function renderAggregatedEntry(
  entry: AggregatedToolCall,
  turnId: string,
  offset: number,
  turnRunning: boolean,
  keyPrefix = ''
): React.ReactNode {
  if (entry.kind === 'single') {
    return (
      <ToolCallCard
        key={entry.item.id}
        item={entry.item}
        turnId={turnId}
        turnRunning={turnRunning}
      />
    )
  }
  return (
    <GroupedToolCallRow
      key={`group-${keyPrefix}-${turnId}-${offset}`}
      category={entry.category}
      items={entry.items}
      turnId={turnId}
      turnRunning={turnRunning}
    />
  )
}

const toolRunStackStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 'var(--conversation-tool-run-gap)'
}

const streamRetryRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  minHeight: '28px',
  padding: '3px 6px',
  color: 'var(--text-secondary)',
  fontSize: '12px',
  lineHeight: 1.35,
  userSelect: 'none'
}

const streamRetryIconStyle: CSSProperties = {
  flex: '0 0 auto',
  color: 'var(--text-dimmed)'
}

const streamRetryLabelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontWeight: 600
}

// ── Grouped tool call row ─────────────────────────────────────────────────────

interface GroupedToolCallRowProps {
  category: ToolGroupCategory
  items: ConversationItem[]
  turnId: string
  turnRunning: boolean
}

/**
 * Collapsed summary row for a group of consecutive aggregated tool calls.
 * Expandable to show each individual child tool card.
 */
function GroupedToolCallRow({ category, items, turnId, turnRunning }: GroupedToolCallRowProps): JSX.Element {
  const locale = useLocale()
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const label = formatToolGroupLabel(category, items, locale, changedFiles)
  const hasFailedItems = items.some(isGroupedItemFailed)
  const [expanded, setExpanded] = useState(category === 'subagent')
  const [hovered, setHovered] = useState(false)
  const rowColor = hovered || expanded ? 'var(--text-secondary)' : 'var(--text-dimmed)'

  return (
    <div>
      <button
        onClick={() => setExpanded((v) => !v)}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setHovered(true)}
        onBlur={() => setHovered(false)}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          width: '100%',
          padding: '3px 6px',
          background: 'transparent',
          border: 'none',
          cursor: 'pointer',
          color: hasFailedItems ? 'var(--error)' : rowColor,
          fontSize: '12px',
          textAlign: 'left',
          borderRadius: '4px'
        }}
      >
        <span
          data-testid="tool-row-title-group"
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: '3px',
            flex: '0 1 auto',
            minWidth: 0,
            maxWidth: '100%',
            color: hasFailedItems ? 'var(--error)' : rowColor
          }}
        >
          <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {label}
          </span>
          <ToolCollapseChevron expanded={expanded} visible={hovered || expanded} />
        </span>
      </button>
      {expanded && (
        category === 'subagent'
          ? <SpawnAgentGroupItems items={items} locale={locale} turnId={turnId} turnRunning={turnRunning} />
          : (
            <div style={{ paddingLeft: '16px' }}>
              {items.map((item) => (
                <ToolCallCard key={item.id} item={item} turnId={turnId} turnRunning={turnRunning} />
              ))}
            </div>
          )
      )}
    </div>
  )
}

interface SpawnAgentGroupDisplay {
  id: string
  name: string
  meta: string
  prompt: string
  accentColor: string
}

function SpawnAgentGroupItems({
  items,
  locale,
  turnId,
  turnRunning
}: {
  items: ConversationItem[]
  locale: AppLocale
  turnId: string
  turnRunning: boolean
}): JSX.Element {
  const displays = items
    .map((item) => getSpawnAgentGroupDisplay(item, locale))
    .filter((display): display is SpawnAgentGroupDisplay => display != null)

  if (displays.length === 0) {
    return (
      <div style={{ paddingLeft: '16px' }}>
        {items.map((item) => (
          <ToolCallCard key={item.id} item={item} turnId={turnId} turnRunning={turnRunning} />
        ))}
      </div>
    )
  }

  return (
    <div style={spawnAgentGroupStyle}>
      {displays.map((display) => (
        <div key={display.id} style={spawnAgentGroupItemStyle}>
          <div style={spawnAgentGroupTitleStyle}>
            {renderGroupedSubAgentTitle(locale, display)}
          </div>
          {display.prompt && (
            <div style={spawnAgentPromptPreviewStyle} title={display.prompt}>
              {display.prompt}
            </div>
          )}
        </div>
      ))}
    </div>
  )
}

function renderGroupedSubAgentTitle(
  locale: AppLocale,
  display: SpawnAgentGroupDisplay
): JSX.Element {
  const template = translate(locale, 'toolCall.subAgent.spawnedFromPrompt', {
    name: '__DOTCRAFT_SUB_AGENT_NAME__'
  })
  const parts = template.split('__DOTCRAFT_SUB_AGENT_NAME__')
  if (parts.length === 1) {
    return (
      <span>
        {renderSubAgentTitle(locale, 'toolCall.subAgent.spawned', display.name, display.accentColor)}
        {display.meta && <span style={spawnAgentMetaStyle}>({display.meta})</span>}
      </span>
    )
  }

  return (
    <span>
      {parts.map((part, index) => (
        <span key={`${part}-${index}`}>
          {part}
          {index < parts.length - 1 && (
            <>
              <span style={{ color: display.accentColor, fontWeight: 600 }}>{display.name}</span>
              {display.meta && <span style={spawnAgentMetaStyle}>({display.meta})</span>}
            </>
          )}
        </span>
      ))}
    </span>
  )
}

function getSpawnAgentGroupDisplay(
  item: ConversationItem,
  locale: AppLocale
): SpawnAgentGroupDisplay | null {
  if (item.toolName !== 'SpawnAgent') return null
  const parsed = parseJsonObject(item.result)
  const args = item.arguments
  const childThreadId = getString(parsed, 'childThreadId')
    ?? getString(parsed, 'agentId')
    ?? getString(args, 'childThreadId')
    ?? getString(args, 'agentId')
  const name = getString(parsed, 'agentNickname')
    ?? getString(parsed, 'nickname')
    ?? getString(args, 'agentNickname')
    ?? getString(args, 'nickname')
    ?? translate(locale, 'toolCall.subAgent.agent')
  const prompt = getString(args, 'agentPrompt')
    ?? getString(args, 'message')
    ?? getString(args, 'prompt')
    ?? ''
  const meta = formatSubAgentMeta({
    agentRole: getString(parsed, 'agentRole') ?? getString(args, 'agentRole'),
    profileName: getString(parsed, 'profileName') ?? getString(args, 'profile'),
    runtimeType: getString(parsed, 'runtimeType')
  })

  return {
    id: item.id,
    name,
    meta,
    prompt: truncateGroupedPrompt(prompt, 180),
    accentColor: getSubAgentAccent(childThreadId ?? name)
  }
}

function truncateGroupedPrompt(value: string, maxChars: number): string {
  const trimmed = value.trim().replace(/\s+/g, ' ')
  const chars = Array.from(trimmed)
  if (chars.length <= maxChars) return trimmed
  return `${chars.slice(0, maxChars - 1).join('')}...`
}

function parseJsonObject(value: string | undefined): Record<string, unknown> | undefined {
  if (!value) return undefined
  try {
    const parsed = JSON.parse(value) as unknown
    if (typeof parsed === 'string') {
      const nested = JSON.parse(parsed) as unknown
      return typeof nested === 'object' && nested != null ? nested as Record<string, unknown> : undefined
    }
    return typeof parsed === 'object' && parsed != null ? parsed as Record<string, unknown> : undefined
  } catch {
    return undefined
  }
}

function getString(source: Record<string, unknown> | undefined, key: string): string | null {
  const value = source?.[key]
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null
}

const spawnAgentGroupStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '3px',
  padding: '1px 6px 2px 18px'
}

const spawnAgentGroupItemStyle: CSSProperties = {
  minWidth: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: '1px',
  fontSize: '12px',
  lineHeight: 1.45
}

const spawnAgentGroupTitleStyle: CSSProperties = {
  color: 'var(--text-secondary)',
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const spawnAgentMetaStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  marginLeft: 4
}

const spawnAgentPromptPreviewStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

function isGroupedItemFailed(item: ConversationItem): boolean {
  if (isToolItemLive(item)) return false
  const executionFailed = item.executionStatus === 'failed'
    || item.executionStatus === 'cancelled'
    || (item.exitCode != null && item.exitCode !== 0)
  return item.success === false || executionFailed
}

function findLastAgentMessageIndex(items: ConversationItem[]): number {
  for (let i = items.length - 1; i >= 0; i--) {
    if (items[i].type === 'agentMessage') {
      return i
    }
  }
  return -1
}

function findLastCreatePlanIndexBefore(items: ConversationItem[], beforeIndex: number): number {
  for (let i = beforeIndex - 1; i >= 0; i--) {
    const item = items[i]
    const isToolCall = isToolLikeItemType(item.type)
    if (
      isToolCall
      && item.toolName === 'CreatePlan'
      && item.status === 'completed'
      && item.success !== false
    ) {
      return i
    }
  }
  return -1
}

function isGuidanceUserMessage(item: ConversationItem): boolean {
  return item.type === 'userMessage' && item.deliveryMode === 'guidance'
}

function isDefaultRenderableItem(item: ConversationItem): boolean {
  return (
    (item.type !== 'userMessage' || item.deliveryMode === 'guidance')
    && item.type !== 'toolResult'
    && item.type !== 'commandExecution'
    && item.type !== 'toolExecution'
  )
}

function isCreatePlanItem(item: ConversationItem): boolean {
  return isToolLikeItemType(item.type) && item.toolName === 'CreatePlan'
}

function isTrimmedHistoryRenderableItem(item: ConversationItem): boolean {
  if (item.type === 'agentMessage') return true
  if (item.type === 'userMessage') return item.deliveryMode === 'guidance'
  if (item.type === 'error') return true
  if (item.type === 'systemNotice') return true
  return isCreatePlanItem(item)
}

function shouldRenderIdleThinkingFallback({
  items,
  isRunning,
  activeItemId,
  streamingMessage,
  streamingMessageStalled
}: {
  items: ConversationItem[]
  isRunning: boolean
  activeItemId: string | null
  streamingMessage: string
  streamingMessageStalled: boolean
}): boolean {
  if (!isRunning) return false

  return !items.some((item) => {
    if (item.type === 'reasoningContent' && item.status === 'streaming' && item.id === activeItemId) {
      return true
    }
    if (item.type === 'agentMessage' && item.status === 'streaming' && item.id === activeItemId) {
      return streamingMessage.trim().length > 0 && !streamingMessageStalled
    }
    if (isToolLikeItemType(item.type) && isToolItemLive(item, { turnRunning: true })) {
      return true
    }
    if (item.type === 'approvalCard' && item.approvalState === 'pending') {
      return true
    }
    return false
  })
}

function hasActiveStreamingAgentMessage(
  items: ConversationItem[],
  activeItemId: string | null
): boolean {
  return items.some(
    (item) =>
      item.type === 'agentMessage' &&
      item.status === 'streaming' &&
      item.id === activeItemId
  )
}

function useStreamingMessageStall({
  enabled,
  lastDeltaAt
}: {
  enabled: boolean
  lastDeltaAt: number | null | undefined
}): boolean {
  const [, forceTick] = useState(0)

  useEffect(() => {
    if (!enabled || lastDeltaAt == null) return undefined

    const elapsedMs = Date.now() - lastDeltaAt
    if (elapsedMs >= STREAMING_MESSAGE_STALL_MS) return undefined

    const timeoutId = setTimeout(() => {
      forceTick((value) => value + 1)
    }, STREAMING_MESSAGE_STALL_MS - elapsedMs)

    return () => clearTimeout(timeoutId)
  }, [enabled, lastDeltaAt])

  if (!enabled) return false
  if (lastDeltaAt == null) return true
  return Date.now() - lastDeltaAt >= STREAMING_MESSAGE_STALL_MS
}

function hydrateToolCallItems(items: ConversationItem[]): ConversationItem[] {
  const resultByCallId = new Map<string, ConversationItem>()
  const commandExecutionByCallId = new Map<string, ConversationItem>()
  const toolExecutionByCallId = new Map<string, ConversationItem>()

  for (const item of items) {
    if (item.type === 'toolResult' && item.toolCallId) {
      resultByCallId.set(item.toolCallId, item)
    } else if (item.type === 'commandExecution' && item.toolCallId) {
      commandExecutionByCallId.set(item.toolCallId, item)
    } else if (item.type === 'toolExecution' && item.toolCallId) {
      toolExecutionByCallId.set(item.toolCallId, item)
    }
  }

  return items.map((item) => {
    if (item.type !== 'toolCall' || !item.toolCallId) return item

    const resultItem = resultByCallId.get(item.toolCallId)
    const commandExecution = commandExecutionByCallId.get(item.toolCallId)
    const toolExecution = toolExecutionByCallId.get(item.toolCallId)
    let hydrated = item

    if (toolExecution) {
      hydrated = {
        ...hydrated,
        status: 'completed',
        result: hydrated.result ?? toolExecution.resultPreview,
        resultPreview: toolExecution.resultPreview ?? hydrated.resultPreview,
        success: toolExecution.success ?? hydrated.success,
        executionStatus: toolExecution.executionStatus ?? hydrated.executionStatus,
        duration: toolExecution.duration
          ?? hydrated.duration
          ?? computeItemDurationMs(hydrated.createdAt, toolExecution.completedAt),
        completedAt: toolExecution.completedAt ?? hydrated.completedAt
      }
    }

    if (resultItem) {
      hydrated = {
        ...hydrated,
        status: 'completed',
        result: resultItem.result ?? hydrated.result,
        success: resultItem.success ?? hydrated.success ?? true,
        duration: hydrated.duration ?? computeItemDurationMs(hydrated.createdAt, resultItem.completedAt),
        completedAt: resultItem.completedAt ?? hydrated.completedAt
      }
    }

    if (commandExecution) {
      hydrated = {
        ...hydrated,
        status: commandExecution.status === 'completed' ? 'completed' : hydrated.status,
        command: commandExecution.command ?? hydrated.command,
        workingDirectory: commandExecution.workingDirectory ?? hydrated.workingDirectory,
        commandSource: commandExecution.commandSource ?? hydrated.commandSource,
        aggregatedOutput: commandExecution.aggregatedOutput ?? hydrated.aggregatedOutput,
        exitCode: commandExecution.exitCode ?? hydrated.exitCode,
        executionStatus: commandExecution.executionStatus ?? hydrated.executionStatus,
        duration: commandExecution.duration
          ?? hydrated.duration
          ?? computeItemDurationMs(hydrated.createdAt, commandExecution.completedAt),
        completedAt: commandExecution.completedAt ?? hydrated.completedAt
      }
    }

    return hydrated
  })
}

function computeItemDurationMs(
  createdAt: string | undefined,
  completedAt: string | undefined
): number | undefined {
  if (!createdAt || !completedAt) return undefined
  const startMs = Date.parse(createdAt)
  const endMs = Date.parse(completedAt)
  if (!Number.isFinite(startMs) || !Number.isFinite(endMs) || endMs < startMs) return undefined
  return endMs - startMs
}

function getIntermediateElapsedMs(
  turn: ConversationTurn,
  finalAgentMessage: ConversationItem | undefined
): number {
  const turnStartMs = Date.parse(turn.startedAt)
  if (!Number.isFinite(turnStartMs)) return 0

  const finalStartMs = finalAgentMessage?.createdAt ? Date.parse(finalAgentMessage.createdAt) : Number.NaN
  if (Number.isFinite(finalStartMs) && finalStartMs >= turnStartMs) {
    return finalStartMs - turnStartMs
  }

  const turnCompletedMs = turn.completedAt ? Date.parse(turn.completedAt) : Number.NaN
  if (Number.isFinite(turnCompletedMs) && turnCompletedMs >= turnStartMs) {
    return turnCompletedMs - turnStartMs
  }

  return 0
}

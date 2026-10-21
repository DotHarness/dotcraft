import { memo, useEffect, useRef, useState, type ComponentProps, type CSSProperties, type ReactNode } from 'react'
import { Image as ImageIcon } from 'lucide-react'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import { isToolLikeItemType } from '../../types/conversation'
import { ThinkingIndicator } from './ThinkingIndicator'
import { ThinkingStatusSlot } from './ThinkingStatusSlot'
import { useTurnThinkingStatus } from './turnThinkingStatus'
import { ToolCallCard, type ShellRuntimeScope } from './ToolCallCard'
import { hasAvailableMcpApp } from './McpAppView'
import { AgentMessage } from './AgentMessage'
import { TurnActivitySummary } from './TurnActivitySummary'
import { getTaskWake, getTurnActivityState } from './turnActivityModel'
import { TurnCompletionSummary } from './TurnCompletionSummary'
import { TurnArtifacts } from './TurnArtifacts'
import { TurnThreadActions } from './TurnThreadActions'
import { isThreadActionToolItem, parseThreadToolAction } from '../../utils/threadToolDisplay'
import { ApprovalCard } from './ApprovalCard'
import { SystemNoticeBlock } from './SystemNoticeBlock'
import { UserMessageBlock } from './UserMessageBlock'
import { ImageGenerationStatus } from './ImageGenerationStatus'
import { WebSearchItemRow } from './WebSearchItemRow'
import { useWebCitationSources } from './useWebCitationSources'
import { getToolOutputImages, ToolOutputImageGallery, useGeneratedOutputImage, type ToolOutputImageItem } from './ToolOutputImageGallery'
import { planToolRunRender } from '../../utils/toolCallAggregation'
import type { AggregatedToolCall } from '../../utils/toolCallAggregation'
import { isToolItemLive } from '../../utils/toolCallAggregation'
import { getToolRowIcon, isToolExecutionFailure, type ToolRowIcon } from '../../utils/toolCallDisplay'
import { isCodeModeExecItem } from '../../utils/codeModeExec'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import { resolveDesktopPluginToolRenderer, useDesktopPluginRegistry } from '../../plugins/desktopPluginRegistry'
import { ToolDisclosure } from './ToolDisclosure'
import { useToolCallRevealTarget } from '../../stores/toolCallRevealStore'
import { useTranscriptItemDiffs } from './transcriptDiffs'
import { isBackgroundTaskNotice } from '../../utils/visibleUserMessage'
import { getSubAgentChipDisplay } from './SubAgentChips'
import { SubAgentGroupChips } from './SubAgentGroupChips'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { formatActiveToolLabel, formatToolGroupLabel, getToolGroupIconItem } from '../../utils/toolGroupLabel'
import { RunningShimmer } from '../ui/RunningShimmer'
import { CORE_TOOL_PRESENTATION_IDS, resolveCoreToolRenderPlan } from '../../utils/toolRendererRegistry'
import { resolveToolKind } from '../../utils/toolKind'
import { CapacityRetryRow } from './CapacityRetryRow'
import { TurnFailureNotice } from './TurnFailureNotice'
import { parseWorkflowLaunch } from '../workflow/WorkflowToolCard'

interface AgentResponseBlockProps {
  turn: ConversationTurn
  /** Live streaming text (only set for the active turn while running) */
  streamingMessage?: string
  streamingMessageLastDeltaAt?: number | null
  /** Live reasoning text (only set for the active turn while reasoning) */
  streamingReasoning?: string
  isRunning?: boolean
  /** Whether this is the active turn that may be in waitingApproval */
  isLastTurn?: boolean
  /** Show a UI-only Thinking row when an active running turn has no live visible work. */
  showIdleThinkingFallback?: boolean
  /** Streaming item highlight source for surfaces that do not use the conversation store. */
  activeItemIdOverride?: string | null
  shellRuntimeScope?: ShellRuntimeScope
  /** Keeps assistant/user text and plans visible while avoiding historical tool-detail mounts. */
  historicalToolContentMode?: HistoricalToolContentMode
  readOnly?: boolean
  renderTurnCompletion?: (turnId: string) => ReactNode
}

export type HistoricalToolContentMode = 'full' | 'trimmed'

type ConversationNodeKind = 'assistant' | 'tool' | 'user' | 'other'

function normalizedErrorMessage(message: string | undefined): string {
  return (message ?? '').trim()
}

interface ConversationRenderNode {
  key: string
  kind: ConversationNodeKind
  node: ReactNode
}

const LABEL_HOLD_MS = 1000

/**
 * Renders agent-side content for a single turn in chronological `turn.items` order.
 * Streaming agentMessage / reasoningContent items arrive as placeholder rows (status
 * `streaming`) so live text keeps its position among committed items. Spec §10.3.3.
 */
export const AgentResponseBlock = memo(function AgentResponseBlock({
  turn,
  streamingMessage = '',
  streamingMessageLastDeltaAt = null,
  streamingReasoning = '',
  isRunning = false,
  isLastTurn = false,
  showIdleThinkingFallback = false,
  activeItemIdOverride,
  shellRuntimeScope = 'conversation',
  historicalToolContentMode = 'full',
  readOnly = false,
  renderTurnCompletion
}: AgentResponseBlockProps): JSX.Element {
  useDesktopPluginRegistry((state) => state.toolRenderers)
  const pendingUserInput = useConversationStore((s) => s.pendingUserInput)
  const activeItemIdFromStore = useConversationStore((s) => s.activeItemId)
  const showThinkingContent = useUIStore((s) => s.showThinkingContent)
  const activeItemId =
    activeItemIdOverride !== undefined ? activeItemIdOverride : activeItemIdFromStore
  const citationSources = useWebCitationSources(turn, streamingMessage)

  const trimHistoricalToolContent = historicalToolContentMode === 'trimmed'
  const hydratedItems = trimHistoricalToolContent ? turn.items : hydrateToolCallItems(turn.items)
  const isSleeping = hydratedItems.some(
    (item) => item.presentation?.presentationId === 'core.sleep'
      && isToolItemLive(item, { turnRunning: isRunning })
  )
  const defaultRenderableItems = hydratedItems
    .filter((item) => !shouldHideCoordinationTool(item, isRunning))
    .filter(isDefaultRenderableItem)

  const renderableItems = trimHistoricalToolContent
    ? defaultRenderableItems.filter(isTrimmedHistoryRenderableItem)
    : defaultRenderableItems

  const offersCapacityRetry = !readOnly && isLastTurn && turn.status === 'failed'
    && turn.providerError === 'serverOverloaded'
  const lastErrorItemId = turn.items.findLast((item) => item.type === 'error')?.id

  const reasoningText = (item: ConversationItem): string =>
    isRunning && item.status === 'streaming' && item.id === activeItemId ? streamingReasoning : (item.reasoning ?? '')

  const renderReasoning = (item: ConversationItem): ReactNode => {
    if (!showThinkingContent) return null
    const streaming = isRunning && item.status === 'streaming' && item.id === activeItemId
    return <ThinkingIndicator key={item.id} elapsedSeconds={item.elapsedSeconds} reasoning={reasoningText(item)} streaming={streaming} />
  }

  const renderToolRunEntry = (entry: AggregatedToolCall): ReactNode => {
    if (entry.kind === 'reasoning') return renderReasoning(entry.item)
    if (entry.kind !== 'group' && entry.kind !== 'active') {
      return renderAggregatedEntry(entry, turn.threadId, turn.id, isRunning, shellRuntimeScope)
    }
    return (
      <ToolActivityRow
        key={`tools-${entry.items[0].id}`}
        items={entry.items}
        active={entry.kind === 'active'}
        threadId={turn.threadId}
        turnId={turn.id}
        turnRunning={isRunning}
        shellRuntimeScope={shellRuntimeScope}
        renderReasoning={renderReasoning}
      />
    )
  }

  const renderItemSequence = (
    itemsToRender: ConversationItem[]
  ): ConversationRenderNode[] => {
    const nodes: ConversationRenderNode[] = []
    let i = 0

    while (i < itemsToRender.length) {
      const item = itemsToRender[i]

      if (isToolRunItem(item)) {
        const toolRun: ConversationItem[] = [item]
        while (i + 1 < itemsToRender.length && isToolRunItem(itemsToRender[i + 1])) {
          i++
          toolRun.push(itemsToRender[i])
        }
        const isTrailingRun = toolRun[toolRun.length - 1] === renderableItems[renderableItems.length - 1]
        const entries = planToolRunRender(toolRun, { isRunning, isTrailingRun })
        const runNodes = entries.map(renderToolRunEntry)

        if (entries.every((entry) => entry.kind === 'reasoning')) {
          entries.forEach((entry, index) => {
            if (runNodes[index] && entry.kind === 'reasoning') {
              nodes.push({ key: entry.item.id, kind: 'assistant', node: runNodes[index] })
            }
          })
        } else {
          nodes.push({
            key: `tool-run-${item.id}`,
            kind: 'tool',
            node: <ToolRunStack>{runNodes}</ToolRunStack>
          })
        }
      } else if (item.type === 'imageGeneration') {
        nodes.push({
          key: item.id,
          kind: 'tool',
          node: <ImageGenerationEntry item={item} />
        })
      } else if (item.type === 'userMessage' && item.deliveryMode === 'guidance' && !isBackgroundTaskNotice(item)) {
        nodes.push({
          key: item.id,
          kind: 'user',
          node: (
            <UserMessageBlock
              text={item.text ?? ''}
              nativeInputParts={item.nativeInputParts}
              imageDataUrls={item.imageDataUrls}
              images={item.images}
              createdAt={item.createdAt}
              triggerKind={item.triggerKind}
              triggerLabel={item.triggerLabel}
              triggerRefId={item.triggerRefId}
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
        const isFooterMessage = item.id === footerAgentMessageId
        nodes.push({
          key: item.id,
          kind: 'assistant',
          node: (
            <AgentMessage
              text={displayText}
              threadId={turn.threadId}
              turnId={turn.id}
              itemId={item.id}
              citations={citationSources}
              streaming={isLiveStreaming}
              turnRunning={isRunning}
              createdAt={item.createdAt}
              isLastTurn={isLastTurn}
              readOnly={readOnly}
              showFooter={isFooterMessage}
              afterContent={isFooterMessage ? turnCompletionContent : undefined}
            />
          )
        })
      } else if (item.type === 'error') {
        nodes.push({
          key: item.id,
          kind: 'other',
          node: (
            <TurnFailureNotice
              message={item.text ?? 'Unknown error'}
              providerError={turn.providerError}
              retry={offersCapacityRetry && item.id === lastErrorItemId ? <CapacityRetryRow /> : undefined}
            />
          )
        })
      } else if (item.type === 'approvalCard' && (item.approvalState ?? 'pending') === 'pending') {
        nodes.push({ key: item.id, kind: 'other', node: <ApprovalCard item={item} /> })
      } else if (item.type === 'systemNotice') {
        nodes.push({
          key: item.id,
          kind: 'other',
          node: <SystemNoticeBlock item={item} />
        })
      }

      i++
    }

    return nodes
  }

  const collapseSourceItems = trimHistoricalToolContent ? defaultRenderableItems : renderableItems
  const activity = getTurnActivityState(turn, collapseSourceItems, isRunning)
  const thinkingStatus = useTurnThinkingStatus({
    items: renderableItems,
    enabled: showIdleThinkingFallback && isRunning && !isSleeping,
    activeItemId,
    streamingMessage,
    streamingMessageLastDeltaAt,
    streamingReasoning,
    waitingForAnswer: pendingUserInput != null
  })
  const lastFinalAgentMessageIndex = activity.finalIndex
  const lastAgentMessageIndex =
    !isRunning && turn.status === 'completed'
      ? findLastVisibleAgentMessageIndex(renderableItems)
      : -1
  const footerAgentMessageId =
    lastAgentMessageIndex >= 0 ? renderableItems[lastAgentMessageIndex]?.id : null
  const turnCompletionContent =
    !trimHistoricalToolContent && turn.status === 'completed'
      ? renderTurnCompletion ? renderTurnCompletion(turn.id) : <TurnCompletionContent turnId={turn.id} />
      : null
  const collapsesIntermediate = activity.status === 'worked' && lastFinalAgentMessageIndex > 0
  const renderNodes: ConversationRenderNode[] = []
  const thinkingStatusNode: ConversationRenderNode = {
    key: `thinking-status-${turn.id}`,
    kind: 'assistant',
    node: <ThinkingStatusSlot status={thinkingStatus} />
  }
  const activitySummary = (intermediateNodes: ConversationRenderNode[]): ConversationRenderNode => ({
    key: `turn-activity-${turn.id}`,
    kind: 'other',
    node: (
      <TurnActivitySummary
        status={activity.status!}
        collapsible={collapsesIntermediate}
        threadId={turn.threadId} turnId={turn.id}
        startedAt={turn.startedAt} completedAt={activity.completedAt}
        taskWake={getTaskWake(turn)}
      >
        {intermediateNodes.length > 0 ? <ConversationNodeFlow nodes={intermediateNodes} defaultGap="var(--conversation-tool-assistant-gap)" /> : null}
      </TurnActivitySummary>
    )
  })

  if (collapsesIntermediate && trimHistoricalToolContent) {
    const beforeFinalItems = collapseSourceItems.slice(0, lastFinalAgentMessageIndex)
    const pinnedTrimmedIndices = collectTrimmedPinnedIntermediateIndices(beforeFinalItems)
    renderNodes.push(activitySummary(renderItemSequence(
      beforeFinalItems.filter((item, index) =>
        !pinnedTrimmedIndices.has(index) && isTrimmedHistoryCollapsedItem(item)
      )
    )))
    renderNodes.push(...Array.from(pinnedTrimmedIndices)
      .sort((a, b) => a - b)
      .flatMap((pinnedIndex) => renderItemSequence([beforeFinalItems[pinnedIndex]])))
    renderNodes.push(...renderItemSequence(
      collapseSourceItems.slice(lastFinalAgentMessageIndex).filter(isTrimmedHistoryRenderableItem)
    ))
  } else if (activity.status) {
    // Pinned results stay outside the collapsed summary (desktop-client.md §5.8).
    const splitIndex = collapsesIntermediate ? lastFinalAgentMessageIndex
      : activity.status === 'worked' ? 0 : renderableItems.length
    const pinnedIndices = collapsesIntermediate ? collectPinnedIntermediateIndices(renderableItems, splitIndex) : []
    const intermediateNodes: ConversationRenderNode[] = []
    let segmentStart = 0
    for (const boundary of [...pinnedIndices, splitIndex]) {
      intermediateNodes.push(...renderItemSequence(renderableItems.slice(segmentStart, boundary)))
      segmentStart = boundary + 1
    }
    if (thinkingStatus.reserved && activity.status === 'working') intermediateNodes.push(thinkingStatusNode)
    renderNodes.push(activitySummary(intermediateNodes))
    renderNodes.push(...pinnedIndices.flatMap((pinnedIndex) => renderItemSequence([renderableItems[pinnedIndex]])))
    renderNodes.push(...renderItemSequence(renderableItems.slice(splitIndex)))
  } else {
    renderNodes.push(...renderItemSequence(renderableItems))
  }

  if (thinkingStatus.reserved && activity.status !== 'working') renderNodes.push(thinkingStatusNode)

  const hasMatchingErrorItem = turn.status === 'failed' && turn.error != null && turn.items.some(
    (item) => item.type === 'error' &&
      normalizedErrorMessage(item.text) === normalizedErrorMessage(turn.error)
  )

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--conversation-tool-assistant-gap)' }}>
      <ConversationNodeFlow nodes={renderNodes} defaultGap="var(--conversation-tool-assistant-gap)" />

      {turn.status === 'failed' && turn.error && !hasMatchingErrorItem && (
        <TurnFailureNotice
          message={turn.error}
          providerError={turn.providerError}
          retry={offersCapacityRetry && lastErrorItemId === undefined ? <CapacityRetryRow /> : undefined}
        />
      )}

      {/* Fallback for completed turns that have file changes but no visible final message footer. */}
      {turnCompletionContent && !footerAgentMessageId && turnCompletionContent}
    </div>
  )
})

function shouldHideCoordinationTool(item: ConversationItem, turnRunning: boolean): boolean {
  if (!isToolLikeItemType(item.type)) return false
  if (item.presentation?.presentationId === CORE_TOOL_PRESENTATION_IDS.sendUserMessageAsync) return true
  if (item.presentation?.presentationId === 'core.sleep') return true
  if (resolveToolKind(item) === 'writeStdin') return !isToolExecutionFailure(item)
  return item.presentation?.presentationId === 'core.request-user-input'
    && isToolItemLive(item, { turnRunning })
}

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
            key={entry.key}
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

function ImageGenerationEntry({ item }: { item: ConversationItem }): JSX.Element {
  const image = useGeneratedOutputImage(item)
  return <ToolEntryWithOutputs images={image ? [image] : []}><ImageGenerationStatus item={item} /></ToolEntryWithOutputs>
}

function TurnCompletionContent({ turnId }: { turnId: string }): JSX.Element {
  return (
    <>
      <TurnThreadActions turnId={turnId} />
      <TurnArtifacts turnId={turnId} />
      <TurnCompletionSummary turnId={turnId} />
    </>
  )
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
  entry: Extract<AggregatedToolCall, { kind: 'single' | 'subagents' | 'images' }>,
  threadId: string,
  turnId: string,
  turnRunning: boolean,
  shellRuntimeScope: ShellRuntimeScope
): ReactNode {
  if (entry.kind === 'single') {
    const images = getToolOutputImages([entry.item])
    return (
      <ToolEntryWithOutputs key={entry.item.id} images={images}>
        {getSubAgentChipDisplay(entry.item) != null ? (
          <SubAgentGroupChips
            items={[entry.item]}
            threadId={threadId}
            turnId={turnId}
            turnRunning={turnRunning}
            shellRuntimeScope={shellRuntimeScope}
          />
        ) : (
          <ToolItemRow
            item={entry.item}
            threadId={threadId}
            turnId={turnId}
            turnRunning={turnRunning}
            shellRuntimeScope={shellRuntimeScope}
          />
        )}
      </ToolEntryWithOutputs>
    )
  }
  if (entry.kind === 'images') return <ImageViewRow key={`images-${entry.items[0].id}`} items={entry.items} />
  return (
    <ToolEntryWithOutputs key={`tools-${entry.items[0].id}`} images={getToolOutputImages(entry.items)}>
      <SubAgentGroupChips
        items={entry.items}
        threadId={threadId}
        turnId={turnId}
        turnRunning={turnRunning}
        shellRuntimeScope={shellRuntimeScope}
      />
    </ToolEntryWithOutputs>
  )
}

function ToolItemRow(props: ComponentProps<typeof ToolCallCard>): JSX.Element {
  return props.item.type === 'webSearch' ? <WebSearchItemRow item={props.item} /> : <ToolCallCard {...props} />
}

function ImageViewRow({ items }: { items: ConversationItem[] }): JSX.Element {
  const locale = useLocale()
  const [expanded, setExpanded] = useState(false)
  const images = getToolOutputImages(items)
  return (
    <ToolDisclosure
      expanded={expanded}
      onToggle={() => setExpanded((v) => !v)}
      icon={{ icon: ImageIcon }}
      title={images.length === 1
        ? translate(locale, 'toolCall.activity.viewedImages.one')
        : translate(locale, 'toolCall.activity.viewedImages.other', { count: images.length })}
    >
      <ToolOutputImageGallery images={images} />
    </ToolDisclosure>
  )
}

function ToolEntryWithOutputs({
  children,
  images
}: {
  children: ReactNode
  images: ToolOutputImageItem[]
}): JSX.Element {
  return (
    <div style={toolEntryWithOutputsStyle}>
      {children}
      {images.length > 0 ? <ToolOutputImageGallery images={images} /> : null}
    </div>
  )
}

const toolRunStackStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 'var(--conversation-tool-run-gap)'
}

const toolEntryWithOutputsStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '6px'
}

interface ToolActivityRowProps {
  items: ConversationItem[]
  active: boolean
  threadId: string
  turnId: string
  turnRunning: boolean
  shellRuntimeScope: ShellRuntimeScope
  renderReasoning: (item: ConversationItem) => ReactNode
}

function ToolActivityRow({
  items,
  active,
  threadId,
  turnId,
  turnRunning,
  shellRuntimeScope,
  renderReasoning
}: ToolActivityRowProps): JSX.Element {
  const locale = useLocale()
  const itemDiffs = useTranscriptItemDiffs()
  const [expanded, setExpanded] = useState(false)
  const revealNonce = useToolCallRevealTarget((target) =>
    target.threadId === threadId && items.some((item) => item.toolCallId === target.callId) ? target.nonce : null)
  useEffect(() => {
    if (revealNonce != null) setExpanded(true)
  }, [revealNonce])
  const tools = items.filter((item) => item.type !== 'reasoningContent')
  const liveTool = active ? tools.findLast((item) => isToolItemLive(item, { turnRunning })) : undefined
  const working = liveTool != null
  const iconItem = liveTool ?? getToolGroupIconItem(tools)
  const { label, icon } = useHeldRowHeader({
    label: liveTool ? formatActiveToolLabel(liveTool, locale)
      : formatToolGroupLabel(tools, locale, itemDiffs),
    icon: iconItem ? getToolRowIcon(iconItem) : undefined
  }, active)

  return (
    <ToolDisclosure
      expanded={expanded}
      onToggle={() => setExpanded((v) => !v)}
      icon={icon}
      title={<RunningShimmer active={working} className="dc-tool-row-running-label">{label}</RunningShimmer>}
    >
      <div className="dc-tool-children">
        {items.map((item) => item.type === 'reasoningContent' ? renderReasoning(item) : (
          <ToolEntryWithOutputs key={item.id} images={getToolOutputImages([item])}>
            <ToolItemRow item={item} threadId={threadId} turnId={turnId} turnRunning={turnRunning} shellRuntimeScope={shellRuntimeScope} />
          </ToolEntryWithOutputs>
        ))}
      </div>
    </ToolDisclosure>
  )
}

interface RowHeader {
  label: string
  icon?: ToolRowIcon
}

function sameRowHeader(a: RowHeader, b: RowHeader): boolean {
  return a.label === b.label && a.icon?.icon === b.icon?.icon && a.icon?.tone === b.icon?.tone
}

function useHeldRowHeader(header: RowHeader, hold: boolean): RowHeader {
  const [held, setHeld] = useState(header)
  const shownAt = useRef(Date.now())
  const latest = useRef(header)
  latest.current = header

  useEffect(() => {
    if (sameRowHeader(latest.current, held)) return
    const show = (): void => {
      shownAt.current = Date.now()
      setHeld(latest.current)
    }
    const wait = shownAt.current + LABEL_HOLD_MS - Date.now()
    if (!hold || wait <= 0) {
      show()
      return
    }
    const timer = setTimeout(show, wait)
    return () => clearTimeout(timer)
  }, [header.label, header.icon?.icon, header.icon?.tone, held, hold])

  return hold ? held : header
}

function isToolRowItem(item: ConversationItem): boolean {
  return isToolLikeItemType(item.type) || item.type === 'webSearch'
}

function isToolRunItem(item: ConversationItem): boolean {
  return isToolRowItem(item) || item.type === 'reasoningContent'
}

function resolveCoreFallbackPlan(item: ConversationItem) {
  const presentationId = item.presentation?.presentationId
  return presentationId && resolveDesktopPluginToolRenderer(presentationId)
    ? null
    : resolveCoreToolRenderPlan(item)
}


function findLastVisibleAgentMessageIndex(items: ConversationItem[]): number {
  for (let i = items.length - 1; i >= 0; i--) {
    const item = items[i]
    if (item.type === 'agentMessage' && (item.text ?? '').trim().length > 0) {
      return i
    }
  }
  return -1
}

function findLastPinnedCoreRendererIndexBefore(items: ConversationItem[], beforeIndex: number): number {
  for (let i = beforeIndex - 1; i >= 0; i--) {
    const item = items[i]
    const isToolCall = isToolLikeItemType(item.type)
    if (
      isToolCall
      && resolveCoreFallbackPlan(item)?.placement === 'pin-last-per-turn'
      && item.status === 'completed'
      && item.success !== false
    ) {
      return i
    }
  }
  return -1
}

/**
 * App availability is independent of tool success, because a failed result may still
 * provide user-actionable UI (desktop-client.md §5.8.2).
 */
function isInteractiveCardItem(item: ConversationItem): boolean {
  return isToolLikeItemType(item.type)
    && item.status === 'completed'
    && hasAvailableMcpApp(item)
}

function findLastInteractiveCardIndexBefore(items: ConversationItem[], beforeIndex: number): number {
  for (let i = beforeIndex - 1; i >= 0; i--) {
    if (isInteractiveCardItem(items[i])) return i
  }
  return -1
}

function isCompletedImageGenerationResult(item: ConversationItem): boolean {
  const status = item.imageGenerationStatus ?? (item.status === 'completed' ? 'completed' : undefined)
  return item.type === 'imageGeneration'
    && status === 'completed'
    && (item.result ?? '').trim().length > 0
}

function findLastImageGenerationIndexBefore(items: ConversationItem[], beforeIndex: number): number {
  for (let i = beforeIndex - 1; i >= 0; i--) {
    if (isCompletedImageGenerationResult(items[i])) return i
  }
  return -1
}

/** Returned ascending so the renderer can split the intermediate run at each pinned boundary. */
function collectPinnedIntermediateIndices(items: ConversationItem[], beforeIndex: number): number[] {
  const indices = new Set<number>()
  items.slice(0, beforeIndex).forEach((item, index) => { if (isGuidanceUserMessage(item)) indices.add(index) })
  const planIndex = findLastPinnedCoreRendererIndexBefore(items, beforeIndex)
  if (planIndex >= 0) indices.add(planIndex)
  const cardIndex = findLastInteractiveCardIndexBefore(items, beforeIndex)
  if (cardIndex >= 0) indices.add(cardIndex)
  const imageIndex = findLastImageGenerationIndexBefore(items, beforeIndex)
  if (imageIndex >= 0) indices.add(imageIndex)
  const workflowIndex = findLastSuccessfulWorkflowIndexBefore(items, beforeIndex)
  if (workflowIndex >= 0) indices.add(workflowIndex)
  return Array.from(indices).sort((a, b) => a - b)
}

function collectTrimmedPinnedIntermediateIndices(items: ConversationItem[]): Set<number> {
  const indices = new Set<number>()
  items.forEach((item, index) => {
    if (isCreatePlanItem(item) || isGuidanceUserMessage(item)) indices.add(index)
  })
  const imageIndex = findLastImageGenerationIndexBefore(items, items.length)
  if (imageIndex >= 0) indices.add(imageIndex)
  const workflowIndex = findLastSuccessfulWorkflowIndexBefore(items, items.length)
  if (workflowIndex >= 0) indices.add(workflowIndex)
  return indices
}

function isGuidanceUserMessage(item: ConversationItem): boolean {
  return item.type === 'userMessage' && item.deliveryMode === 'guidance'
}

function isDefaultRenderableItem(item: ConversationItem): boolean {
  if (isCodeModeExecItem(item)) return false
  // Successful CreateThread / SendMessageToThread calls render as a dedicated card
  // before the agent footer (TurnThreadActions), so suppress their inline tool row.
  if (isThreadActionToolItem(item) && parseThreadToolAction(item) != null) return false
  const plan = resolveCoreFallbackPlan(item)
  if (plan?.family === 'todo') return false
  if (plan?.family === 'subagent') {
    if (plan.options.operation !== 'spawn' && !isToolExecutionFailure(item)) return false
  }
  return (
    (item.type !== 'userMessage' || item.deliveryMode === 'guidance')
    && item.type !== 'toolResult'
    && item.type !== 'commandExecution'
    && item.type !== 'toolExecution'
  )
}

function isCreatePlanItem(item: ConversationItem): boolean {
  return isToolLikeItemType(item.type)
    && resolveCoreFallbackPlan(item)?.family === 'createPlan'
}

function isSuccessfulWorkflowLaunchItem(item: ConversationItem): boolean {
  return isToolLikeItemType(item.type)
    && item.status === 'completed'
    && item.success !== false
    && parseWorkflowLaunch(item.toolName ?? '', item.result) != null
}

function findLastSuccessfulWorkflowIndexBefore(
  items: ConversationItem[],
  beforeIndex: number
): number {
  for (let index = beforeIndex - 1; index >= 0; index--) {
    if (isSuccessfulWorkflowLaunchItem(items[index])) return index
  }
  return -1
}

function isTrimmedHistoryRenderableItem(item: ConversationItem): boolean {
  if (item.type === 'agentMessage') return true
  if (item.type === 'userMessage') return item.deliveryMode === 'guidance'
  if (item.type === 'error') return true
  if (item.type === 'systemNotice') return true
  if (isCompletedImageGenerationResult(item)) return true
  return isCreatePlanItem(item) || isSuccessfulWorkflowLaunchItem(item)
}

function isTrimmedHistoryCollapsedItem(item: ConversationItem): boolean {
  if (item.type === 'agentMessage') return true
  if (item.type === 'reasoningContent') return true
  if (item.type === 'userMessage') return item.deliveryMode === 'guidance'
  if (item.type === 'error') return true
  if (item.type === 'systemNotice') return true
  return false
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
      const resultPreview = toolExecution.executionStatus === 'failed'
        ? toolExecution.errorMessage ?? toolExecution.resultPreview ?? hydrated.resultPreview
        : toolExecution.resultPreview ?? toolExecution.errorMessage ?? hydrated.resultPreview
      hydrated = {
        ...hydrated,
        status: 'completed',
        result: hydrated.result ?? resultPreview,
        resultPreview,
        success: toolExecution.success ?? hydrated.success,
        errorMessage: toolExecution.errorMessage ?? hydrated.errorMessage,
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
        contentItems: resultItem.contentItems ?? hydrated.contentItems,
        success: resultItem.success ?? hydrated.success ?? true,
        errorCode: resultItem.errorCode ?? hydrated.errorCode,
        duration: hydrated.duration ?? computeItemDurationMs(hydrated.createdAt, resultItem.completedAt),
        completedAt: resultItem.completedAt ?? hydrated.completedAt
      }
    }

    if (commandExecution) {
      const staleInProgressCommand =
        commandExecution.executionStatus === 'inProgress'
        && isTerminalExecutionStatus(hydrated.executionStatus)
      const terminalPreviewOutput =
        staleInProgressCommand && !commandExecution.aggregatedOutput
          ? (hydrated.aggregatedOutput && hydrated.aggregatedOutput.length > 0
              ? hydrated.aggregatedOutput
              : hydrated.resultPreview ?? hydrated.result)
          : undefined
      hydrated = {
        ...hydrated,
        status: commandExecution.status === 'completed' ? 'completed' : hydrated.status,
        command: commandExecution.command ?? hydrated.command,
        workingDirectory: commandExecution.workingDirectory ?? hydrated.workingDirectory,
        commandSource: commandExecution.commandSource ?? hydrated.commandSource,
        aggregatedOutput: terminalPreviewOutput
          ?? commandExecution.aggregatedOutput
          ?? hydrated.aggregatedOutput,
        exitCode: commandExecution.exitCode ?? hydrated.exitCode,
        executionStatus: staleInProgressCommand
          ? hydrated.executionStatus
          : commandExecution.executionStatus ?? hydrated.executionStatus,
        duration: commandExecution.duration
          ?? hydrated.duration
          ?? computeItemDurationMs(hydrated.createdAt, commandExecution.completedAt),
        completedAt: commandExecution.completedAt ?? hydrated.completedAt
      }
    }

    return hydrated
  })
}

function isTerminalExecutionStatus(status: ConversationItem['executionStatus'] | undefined): boolean {
  return status === 'completed' || status === 'failed' || status === 'cancelled'
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

import { memo, useEffect, useState, type CSSProperties, type ReactNode } from 'react'
import { Image as ImageIcon, Info } from 'lucide-react'
import type { ConversationItem, ConversationTurn, PluginFunctionContentItem } from '../../types/conversation'
import { isToolLikeItemType } from '../../types/conversation'
import { ThinkingIndicator } from './ThinkingIndicator'
import { renderSubAgentTitle, ToolCallCard, type ShellRuntimeScope } from './ToolCallCard'
import { hasAvailableMcpApp } from './McpAppView'
import { AgentMessage } from './AgentMessage'
import { ErrorBlock } from './ErrorBlock'
import { CancelledNotice } from './CancelledNotice'
import { TurnCompletionSummary } from './TurnCompletionSummary'
import { TurnArtifacts } from './TurnArtifacts'
import { TurnThreadActions } from './TurnThreadActions'
import { ImageLightbox } from './ImageLightbox'
import { isThreadActionToolItem, parseThreadToolAction } from '../../utils/threadToolDisplay'
import { ApprovalCard } from './ApprovalCard'
import { SystemNoticeBlock } from './SystemNoticeBlock'
import { UserMessageBlock } from './UserMessageBlock'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../ui/ContextMenu'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Skeleton } from '../ui/Skeleton'
import { planToolRunRender } from '../../utils/toolCallAggregation'
import type { AggregatedToolCall } from '../../utils/toolCallAggregation'
import type { ToolGroupCategory } from '../../utils/toolCallAggregation'
import { isToolItemLive } from '../../utils/toolCallAggregation'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { ToolCollapseChevron } from './ToolCollapseChevron'
import { useLocale } from '../../contexts/LocaleContext'
import { formatToolGroupLabel } from '../../utils/toolGroupLabel'
import { resolveCoreToolRenderPlan } from '../../utils/toolRendererRegistry'
import { TurnCollapsedSummary } from './TurnCollapsedSummary'
import { translate, type AppLocale } from '../../../shared/locales'
import {
  formatSubAgentMeta,
  getSubAgentAccent,
  getSubAgentIdentitySeed
} from '../../utils/subAgentPresentation'
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
  /** Selects the transient shell runtime owned by this rendering surface. */
  shellRuntimeScope?: ShellRuntimeScope
  /**
   * Main conversation optimization for older history: keep assistant/user text
   * and plans visible while avoiding historical tool-detail component mounts.
   */
  historicalToolContentMode?: HistoricalToolContentMode
}

export type HistoricalToolContentMode = 'full' | 'trimmed'

type ConversationNodeKind = 'assistant' | 'tool' | 'user' | 'other'

const STREAMING_MESSAGE_STALL_MS = 2000

function normalizedErrorMessage(message: string | undefined): string {
  return (message ?? '').trim()
}

interface ConversationRenderNode {
  kind: ConversationNodeKind
  node: ReactNode
}

interface ToolOutputImageItem {
  id: string
  mediaType: string
  dataBase64: string
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
  isLastTurn = false,
  showIdleThinkingFallback = false,
  activeItemIdOverride,
  shellRuntimeScope = 'conversation',
  historicalToolContentMode = 'full'
}: AgentResponseBlockProps): JSX.Element {
  const pendingApproval = useConversationStore((s) => s.pendingApproval)
  const activeItemIdFromStore = useConversationStore((s) => s.activeItemId)
  const showThinkingContent = useUIStore((s) => s.showThinkingContent)
  const activeItemId =
    activeItemIdOverride !== undefined ? activeItemIdOverride : activeItemIdFromStore

  const trimHistoricalToolContent = historicalToolContentMode === 'trimmed'
  const hydratedItems = trimHistoricalToolContent ? turn.items : hydrateToolCallItems(turn.items)
  const defaultRenderableItems = hydratedItems.filter(isDefaultRenderableItem)

  // Exclude user messages and toolResult items (toolResults are merged into their
  // parent toolCall items before rendering, not rendered independently)
  const renderableItems = trimHistoricalToolContent
    ? defaultRenderableItems.filter(isTrimmedHistoryRenderableItem)
    : defaultRenderableItems

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
            shellRuntimeScope,
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
      } else if (item.type === 'imageGeneration') {
        nodes.push({
          kind: 'tool',
          node: <ImageGenerationEntry key={item.id} item={item} />
        })
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
              threadId={turn.threadId}
              turnId={turn.id}
              itemId={item.id}
              streaming={isLiveStreaming}
              createdAt={item.createdAt}
              isLastTurn={isLastTurn}
              showFooter={item.id === footerAgentMessageId}
              afterContent={item.id === footerAgentMessageId ? turnCompletionContent : undefined}
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

  const collapseSourceItems = trimHistoricalToolContent ? defaultRenderableItems : renderableItems
  const lastFinalAgentMessageIndex =
    !isRunning &&
    turn.status === 'completed' &&
    !collapseSourceItems.some(isGuidanceUserMessage)
      ? findLastAgentMessageIndex(collapseSourceItems)
      : -1
  const lastAgentMessageIndex =
    !isRunning && turn.status === 'completed'
      ? findLastVisibleAgentMessageIndex(renderableItems)
      : -1
  const footerAgentMessageId =
    lastAgentMessageIndex >= 0 ? renderableItems[lastAgentMessageIndex]?.id : null
  const turnCompletionContent =
    !trimHistoricalToolContent && turn.status === 'completed'
      ? <TurnCompletionContent turnId={turn.id} />
      : null
  const shouldCollapseIntermediate = lastFinalAgentMessageIndex > 0
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
    if (trimHistoricalToolContent) {
      const beforeFinalItems = collapseSourceItems.slice(0, lastFinalAgentMessageIndex)
      const pinnedTrimmedIndices = collectTrimmedPinnedIntermediateIndices(beforeFinalItems)
      const intermediateNodes = renderItemSequence(
        beforeFinalItems.filter((item, index) =>
          !pinnedTrimmedIndices.has(index) && isTrimmedHistoryCollapsedItem(item)
        ),
        'trimmed-history-intermediate'
      )
      const pinnedTrimmedNodes = Array.from(pinnedTrimmedIndices)
        .sort((a, b) => a - b)
        .flatMap((pinnedIndex, position) =>
          renderItemSequence([beforeFinalItems[pinnedIndex]], `trimmed-history-pinned-${position}`)
        )
      const trailingNodes = renderItemSequence(
        collapseSourceItems.slice(lastFinalAgentMessageIndex).filter(isTrimmedHistoryRenderableItem),
        'trimmed-history-trailing'
      )
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
      renderNodes.push(...pinnedTrimmedNodes)
      renderNodes.push(...trailingNodes)
    } else {
      // Pin durable user-facing results out of the collapsed
      // summary (desktop-client.md §5.8). Split the intermediate run at each
      // pinned boundary so a pinned card never merges the tool runs on either side.
      const pinnedIndices = collectPinnedIntermediateIndices(renderableItems, lastFinalAgentMessageIndex)
      const trailingNodes = renderItemSequence(renderableItems.slice(lastFinalAgentMessageIndex))

      const intermediateNodes: ConversationRenderNode[] = []
      if (pinnedIndices.length === 0) {
        intermediateNodes.push(...renderItemSequence(renderableItems.slice(0, lastFinalAgentMessageIndex)))
      } else {
        let segmentStart = 0
        pinnedIndices.forEach((pinnedIndex, segment) => {
          intermediateNodes.push(...renderItemSequence(
            renderableItems.slice(segmentStart, pinnedIndex),
            `intermediate-${segment}`
          ))
          segmentStart = pinnedIndex + 1
        })
        intermediateNodes.push(...renderItemSequence(
          renderableItems.slice(segmentStart, lastFinalAgentMessageIndex),
          'intermediate-tail'
        ))
      }

      const pinnedNodes = pinnedIndices.flatMap((pinnedIndex, position) =>
        renderItemSequence([renderableItems[pinnedIndex]], `pinned-${position}`)
      )

      if (intermediateNodes.length > 0) {
        const elapsedMs = getIntermediateElapsedMs(turn, renderableItems[lastFinalAgentMessageIndex])
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

      renderNodes.push(...pinnedNodes)
      renderNodes.push(...trailingNodes)
    }
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

  const hasMatchingErrorItem = turn.status === 'failed' && turn.error != null && turn.items.some(
    (item) => item.type === 'error' &&
      normalizedErrorMessage(item.text) === normalizedErrorMessage(turn.error)
  )

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--conversation-tool-assistant-gap)' }}>
      <ConversationNodeFlow nodes={renderNodes} defaultGap="var(--conversation-tool-assistant-gap)" />

      {/* Turn-level failure */}
      {turn.status === 'failed' && turn.error && !hasMatchingErrorItem && (
        <ErrorBlock message={turn.error} />
      )}

      {/* Cancellation notice */}
      {turn.status === 'cancelled' && (
        <CancelledNotice reason={turn.cancelReason} />
      )}

      {/* Fallback for completed turns that have file changes but no visible final message footer. */}
      {turnCompletionContent && !footerAgentMessageId && turnCompletionContent}
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

function ImageGenerationEntry({ item }: { item: ConversationItem }): JSX.Element {
  const locale = useLocale()
  const status = item.imageGenerationStatus ?? (item.status === 'completed' ? 'completed' : 'inProgress')
  const image = status === 'completed' ? getImageGenerationOutputImage(item) : null
  const isInProgress = status === 'inProgress'

  if (status === 'failed') {
    return (
      <ErrorBlock
        message={item.errorMessage?.trim() || translate(locale, 'conversation.imageGeneration.failed')}
      />
    )
  }

  if (status === 'completed' && image == null) {
    return <ErrorBlock message={translate(locale, 'conversation.imageGeneration.noImageData')} />
  }

  const label = status === 'completed'
    ? translate(locale, 'conversation.imageGeneration.completed')
    : translate(locale, 'conversation.imageGeneration.generating')

  return (
    <ToolEntryWithOutputs images={image ? [image] : []}>
      <div
        role={isInProgress ? 'status' : undefined}
        aria-live={isInProgress ? 'polite' : undefined}
        aria-busy={isInProgress ? true : undefined}
        aria-label={isInProgress ? label : undefined}
        style={isInProgress ? imageGenerationProgressStyle : undefined}
      >
        <div
          data-testid="image-generation-row"
          style={imageGenerationRowStyle}
        >
          <ImageIcon size={15} strokeWidth={1.8} aria-hidden="true" style={imageGenerationIconStyle} />
          <span
            className={isInProgress ? 'tool-running-gradient-text' : undefined}
            style={imageGenerationLabelStyle}
          >
            {label}
          </span>
        </div>
        {isInProgress && (
          <div data-testid="image-generation-skeleton" style={imageGenerationSkeletonFrameStyle}>
            <Skeleton width="100%" height="100%" radius={4} />
          </div>
        )}
      </div>
    </ToolEntryWithOutputs>
  )
}

function getImageGenerationOutputImage(item: ConversationItem): ToolOutputImageItem | null {
  const dataBase64 = item.result?.trim()
  if (!dataBase64) return null
  return {
    id: `${item.id}-image-0`,
    mediaType: item.mediaType?.trim() || 'image/png',
    dataBase64
  }
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
  shellRuntimeScope: ShellRuntimeScope,
  keyPrefix = ''
): React.ReactNode {
  if (entry.kind === 'single') {
    const images = getToolOutputImages([entry.item])
    return (
      <ToolEntryWithOutputs
        key={entry.item.id}
        images={images}
      >
        <ToolCallCard
          item={entry.item}
          turnId={turnId}
          turnRunning={turnRunning}
          shellRuntimeScope={shellRuntimeScope}
        />
      </ToolEntryWithOutputs>
    )
  }
  const images = getToolOutputImages(entry.items)
  return (
    <ToolEntryWithOutputs
      key={`group-${keyPrefix}-${turnId}-${offset}`}
      images={images}
    >
      <GroupedToolCallRow
        category={entry.category}
        items={entry.items}
        turnId={turnId}
        turnRunning={turnRunning}
        shellRuntimeScope={shellRuntimeScope}
      />
    </ToolEntryWithOutputs>
  )
}

function ToolEntryWithOutputs({
  children,
  images
}: {
  children: ReactNode
  images: ToolOutputImageItem[]
}): JSX.Element {
  if (images.length === 0) return <>{children}</>

  return (
    <div style={toolEntryWithOutputsStyle}>
      {children}
      <ToolOutputImageGallery images={images} />
    </div>
  )
}

function ToolOutputImageGallery({ images }: { images: ToolOutputImageItem[] }): JSX.Element {
  const locale = useLocale()
  const [lightboxImage, setLightboxImage] = useState<ToolOutputImageItem | null>(null)
  const [contextMenu, setContextMenu] = useState<{ position: ContextMenuPosition; image: ToolOutputImageItem } | null>(null)
  const contextItems: ContextMenuEntry[] = contextMenu
    ? [
        {
          label: translate(locale, 'conversation.selectAll'),
          onClick: () => {
            try {
              document.execCommand('selectAll')
            } catch {
              // Ignore selection command failures in read-only output.
            }
          }
        },
        { type: 'separator' },
        {
          label: translate(locale, 'conversation.copyImage'),
          onClick: () => {
            void copyToolOutputImage(contextMenu.image, locale)
          }
        }
      ]
    : []

  return (
    <>
      <div data-testid="tool-output-image-gallery" style={toolOutputImageGalleryStyle}>
        {images.map((image, index) => {
          const dataUrl = toolOutputImageDataUrl(image)
          return (
            <button
              key={image.id}
              type="button"
              aria-label={`Preview tool output image ${index + 1}`}
              onClick={() => setLightboxImage(image)}
              onContextMenu={(event) => {
                event.preventDefault()
                event.stopPropagation()
                setContextMenu({
                  position: { x: event.clientX, y: event.clientY },
                  image
                })
              }}
              style={toolOutputImageButtonStyle}
            >
              <img
                data-testid="tool-output-image"
                src={dataUrl}
                alt={`Tool output image ${index + 1}`}
                style={toolOutputImageStyle}
              />
            </button>
          )
        })}
      </div>
      {lightboxImage && (
        <ImageLightbox
          src={toolOutputImageDataUrl(lightboxImage)}
          alt="Tool output image"
          onClose={() => setLightboxImage(null)}
        />
      )}
      {contextMenu && (
        <ContextMenu
          items={contextItems}
          position={contextMenu.position}
          onClose={() => setContextMenu(null)}
        />
      )}
    </>
  )
}

function getToolOutputImages(items: ConversationItem[]): ToolOutputImageItem[] {
  return items.flatMap((item) =>
    (item.contentItems ?? [])
      .map((contentItem, index) => toToolOutputImage(item.id, contentItem, index))
      .filter((image): image is ToolOutputImageItem => image != null)
  )
}

function toToolOutputImage(
  itemId: string,
  contentItem: PluginFunctionContentItem,
  index: number
): ToolOutputImageItem | null {
  const dataBase64 = contentItem.dataBase64?.trim()
  if (contentItem.type !== 'image' || !dataBase64) return null
  return {
    id: `${itemId}-image-${index}`,
    mediaType: contentItem.mediaType?.trim() || 'image/png',
    dataBase64
  }
}

function toolOutputImageDataUrl(image: ToolOutputImageItem): string {
  return `data:${image.mediaType};base64,${image.dataBase64}`
}

async function copyToolOutputImage(image: ToolOutputImageItem, locale: AppLocale): Promise<void> {
  const dataUrl = toolOutputImageDataUrl(image)
  const clipboard = navigator.clipboard as (Clipboard & {
    write?: (items: ClipboardItem[]) => Promise<void>
    writeText?: (text: string) => Promise<void>
  }) | undefined
  const ClipboardItemCtor = (globalThis as typeof globalThis & {
    ClipboardItem?: new (items: Record<string, Blob>) => ClipboardItem
  }).ClipboardItem

  if (clipboard?.write && ClipboardItemCtor) {
    try {
      await clipboard.write([
        new ClipboardItemCtor({
          [image.mediaType]: base64ToBlob(image.dataBase64, image.mediaType)
        })
      ])
      addToast(translate(locale, 'toast.copied'), 'success', 2000)
      return
    } catch {
      // Fall back to copying the data URL text when binary image clipboard fails.
    }
  }

  try {
    await clipboard?.writeText?.(dataUrl)
    addToast(translate(locale, 'toast.copied'), 'success', 2000)
  } catch {
    // Clipboard failures should not block image preview interactions.
  }
}

function base64ToBlob(dataBase64: string, mediaType: string): Blob {
  const binary = atob(dataBase64)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i)
  }
  return new Blob([bytes], { type: mediaType })
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

const toolOutputImageGalleryStyle: CSSProperties = {
  display: 'flex',
  flexWrap: 'wrap',
  alignItems: 'flex-start',
  gap: '8px',
  padding: '0 6px'
}

const toolOutputImageButtonStyle: CSSProperties = {
  display: 'block',
  padding: 0,
  border: 'none',
  borderRadius: '4px',
  background: 'transparent',
  lineHeight: 0,
  cursor: 'zoom-in'
}

const toolOutputImageStyle: CSSProperties = {
  display: 'block',
  maxWidth: '240px',
  maxHeight: '180px',
  objectFit: 'contain',
  border: '1px solid var(--border-default)',
  borderRadius: '4px',
  background: 'var(--bg-primary)'
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

const imageGenerationProgressStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '6px'
}

const imageGenerationRowStyle: CSSProperties = {
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

const imageGenerationIconStyle: CSSProperties = {
  flex: '0 0 auto',
  color: 'var(--text-dimmed)'
}

const imageGenerationLabelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontWeight: 600
}

const imageGenerationSkeletonFrameStyle: CSSProperties = {
  width: '180px',
  height: '180px',
  padding: '0 6px'
}

// ── Grouped tool call row ─────────────────────────────────────────────────────

interface GroupedToolCallRowProps {
  category: ToolGroupCategory
  items: ConversationItem[]
  turnId: string
  turnRunning: boolean
  shellRuntimeScope: ShellRuntimeScope
}

/**
 * Collapsed summary row for a group of consecutive aggregated tool calls.
 * Expandable to show each individual child tool card.
 */
function GroupedToolCallRow({
  category,
  items,
  turnId,
  turnRunning,
  shellRuntimeScope
}: GroupedToolCallRowProps): JSX.Element {
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
          ? <SubAgentActionGroupItems items={items} locale={locale} turnId={turnId} turnRunning={turnRunning} shellRuntimeScope={shellRuntimeScope} />
          : (
            <div style={{ paddingLeft: '16px' }}>
              {items.map((item) => (
                <ToolCallCard key={item.id} item={item} turnId={turnId} turnRunning={turnRunning} shellRuntimeScope={shellRuntimeScope} />
              ))}
            </div>
          )
      )}
    </div>
  )
}

interface SubAgentActionGroupDisplay {
  id: string
  operation: 'spawn' | 'followupTask'
  name: string
  meta: string
  prompt: string
  accentColor: string
}

function SubAgentActionGroupItems({
  items,
  locale,
  turnId,
  turnRunning,
  shellRuntimeScope
}: {
  items: ConversationItem[]
  locale: AppLocale
  turnId: string
  turnRunning: boolean
  shellRuntimeScope: ShellRuntimeScope
}): JSX.Element {
  const displays = items
    .map((item) => getSubAgentActionGroupDisplay(item, locale))
    .filter((display): display is SubAgentActionGroupDisplay => display != null)

  if (displays.length === 0) {
    return (
      <div style={{ paddingLeft: '16px' }}>
        {items.map((item) => (
          <ToolCallCard key={item.id} item={item} turnId={turnId} turnRunning={turnRunning} shellRuntimeScope={shellRuntimeScope} />
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
            <ActionTooltip label={display.prompt} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}>
            <div style={{ ...spawnAgentPromptPreviewStyle, display: 'block' }}>
              {display.prompt}
            </div>
            </ActionTooltip>
          )}
        </div>
      ))}
    </div>
  )
}

function renderGroupedSubAgentTitle(
  locale: AppLocale,
  display: SubAgentActionGroupDisplay
): JSX.Element {
  const templateKey = display.operation === 'spawn'
    ? 'toolCall.subAgent.spawnedFromPrompt'
    : 'toolCall.subAgent.updatedFromPrompt'
  const titleKey = display.operation === 'spawn'
    ? 'toolCall.subAgent.spawned'
    : 'toolCall.subAgent.followedUp'
  const template = translate(locale, templateKey, {
    name: '__DOTCRAFT_SUB_AGENT_NAME__'
  })
  const parts = template.split('__DOTCRAFT_SUB_AGENT_NAME__')
  if (parts.length === 1) {
    return (
      <span>
        {renderSubAgentTitle(locale, titleKey, display.name, display.accentColor)}
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

function getSubAgentActionGroupDisplay(
  item: ConversationItem,
  locale: AppLocale
): SubAgentActionGroupDisplay | null {
  const plan = resolveCoreToolRenderPlan(item)
  const operation = plan?.options.operation
  if (plan?.family !== 'subagent' || (operation !== 'spawn' && operation !== 'followupTask')) return null
  const parsed = parseJsonObject(item.result)
  const args = item.arguments
  const agentPath = getString(parsed, 'agentPath') ?? getString(args, 'target')
  const childThreadId = getString(parsed, 'childThreadId')
    ?? getString(parsed, 'agentId')
    ?? getString(args, 'childThreadId')
    ?? getString(args, 'agentId')
  const name = getString(parsed, 'agentNickname')
    ?? getString(parsed, 'nickname')
    ?? getString(parsed, 'taskName')
    ?? getString(args, 'agentNickname')
    ?? getString(args, 'nickname')
    ?? getString(args, 'taskName')
    ?? translate(locale, 'toolCall.subAgent.agent')
  const prompt = getString(args, 'message')
    ?? getString(args, 'agentPrompt')
    ?? getString(args, 'prompt')
    ?? ''
  const meta = formatSubAgentMeta({
    agentRole: getString(parsed, 'agentRole') ?? getString(args, 'agentRole'),
    profileName: getString(parsed, 'profileName') ?? getString(args, 'profile'),
    runtimeType: getString(parsed, 'runtimeType')
  })

  return {
    id: item.id,
    operation,
    name,
    meta,
    prompt: truncateGroupedPrompt(prompt, 180),
    accentColor: getSubAgentAccent(getSubAgentIdentitySeed({ agentPath, childThreadId, nickname: name }))
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
  // Shell tools (Exec/RunCommand/BashCommand) never render as failed in their
  // individual card — ToolCallCard forces success via `isShellTool`. Keep the
  // aggregated row consistent so an exec exit code / failure doesn't redden it.
  if (resolveCoreToolRenderPlan(item)?.successOverride === true) return false
  return isToolExecutionFailure(item)
}

function isToolExecutionFailure(item: ConversationItem): boolean {
  const executionFailed = item.executionStatus === 'failed'
    || item.executionStatus === 'cancelled'
    || (item.exitCode != null && item.exitCode !== 0)
  if (item.success === false || executionFailed) return true

  const parsedResult = parseJsonObject(item.result)
  const resultStatus = getString(parsedResult, 'status')?.toLowerCase()
  if (resultStatus === 'timeout') return false
  return resultStatus === 'failed'
    || resultStatus === 'error'
    || resultStatus === 'cancelled'
    || resultStatus === 'canceled'
    || getString(parsedResult, 'error') != null
}

function findLastAgentMessageIndex(items: ConversationItem[]): number {
  for (let i = items.length - 1; i >= 0; i--) {
    if (items[i].type === 'agentMessage') {
      return i
    }
  }
  return -1
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
      && resolveCoreToolRenderPlan(item)?.placement === 'pin-last-per-turn'
      && item.status === 'completed'
      && item.success !== false
    ) {
      return i
    }
  }
  return -1
}

/**
 * A terminal MCP tool result whose current projection advertises an available
 * MCP App. Availability is independent of tool success because failed results
 * may still provide user-actionable UI, so these surfaces stay pinned out of
 * the collapsed turn summary (desktop-client.md §5.8.2).
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

/**
 * Intermediate items (before the final agent message) that should be pinned out of the
 * collapsed turn summary rather than folded into "Processed in Xs". Returned ascending
 * so the renderer can split the
 * intermediate run at each pinned boundary and render the pinned items in order.
 */
function collectPinnedIntermediateIndices(items: ConversationItem[], beforeIndex: number): number[] {
  const indices = new Set<number>()
  const planIndex = findLastPinnedCoreRendererIndexBefore(items, beforeIndex)
  if (planIndex >= 0) indices.add(planIndex)
  const cardIndex = findLastInteractiveCardIndexBefore(items, beforeIndex)
  if (cardIndex >= 0) indices.add(cardIndex)
  const imageIndex = findLastImageGenerationIndexBefore(items, beforeIndex)
  if (imageIndex >= 0) indices.add(imageIndex)
  return Array.from(indices).sort((a, b) => a - b)
}

function collectTrimmedPinnedIntermediateIndices(items: ConversationItem[]): Set<number> {
  const indices = new Set<number>()
  items.forEach((item, index) => {
    if (isCreatePlanItem(item)) indices.add(index)
  })
  const imageIndex = findLastImageGenerationIndexBefore(items, items.length)
  if (imageIndex >= 0) indices.add(imageIndex)
  return indices
}

function isGuidanceUserMessage(item: ConversationItem): boolean {
  return item.type === 'userMessage' && item.deliveryMode === 'guidance'
}

function isDefaultRenderableItem(item: ConversationItem): boolean {
  // Successful CreateThread / SendMessageToThread calls render as a dedicated card
  // before the agent footer (TurnThreadActions), so suppress their inline tool row.
  if (isThreadActionToolItem(item) && parseThreadToolAction(item) != null) return false
  const plan = resolveCoreToolRenderPlan(item)
  if (plan?.family === 'subagent') {
    const operation = plan.options.operation
    if (operation !== 'spawn' && operation !== 'followupTask' && !isToolExecutionFailure(item)) {
      return false
    }
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
    && resolveCoreToolRenderPlan(item)?.family === 'createPlan'
}

function isTrimmedHistoryRenderableItem(item: ConversationItem): boolean {
  if (item.type === 'agentMessage') return true
  if (item.type === 'userMessage') return item.deliveryMode === 'guidance'
  if (item.type === 'error') return true
  if (item.type === 'systemNotice') return true
  if (isCompletedImageGenerationResult(item)) return true
  return isCreatePlanItem(item)
}

function isTrimmedHistoryCollapsedItem(item: ConversationItem): boolean {
  if (item.type === 'agentMessage') return true
  if (item.type === 'reasoningContent') return true
  if (item.type === 'userMessage') return item.deliveryMode === 'guidance'
  if (item.type === 'error') return true
  if (item.type === 'systemNotice') return true
  return false
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
    if (
      item.type === 'imageGeneration' &&
      (item.imageGenerationStatus === 'inProgress' || item.status === 'started')
    ) {
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

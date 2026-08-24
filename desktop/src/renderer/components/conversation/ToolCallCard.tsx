import { memo, useEffect, useRef, useState, type CSSProperties } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import { useLocale } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useReviewPanelStore } from '../../stores/reviewPanelStore'
import {
  formatCronRunningLabel,
  formatCronResultLines,
  hasCronCreatedDisplayData
} from '../../utils/cronToolDisplay'
import {
  formatInvocationDisplay,
  formatResultSummary,
  formatToolSearchCompletedLabel,
  getWebToolIcon,
  getWebToolSectionLabel,
  invocationNeedsCallingPrefix,
  parseWebSearchResultDisplay,
  type WebSearchResultRow
} from '../../utils/webToolDisplay'
import { FileResultHeader, InlineDiffView } from './InlineDiffView'
import { ActionTooltip } from '../ui/ActionTooltip'
import {
  formatCollapsedToolLabel,
  formatExpandedInvocation,
  formatWorkflowFailureLabel,
  getStreamingToolDisplay
} from '../../utils/toolCallDisplay'
import { PlanToolOutput } from './PlanToolOutput'
import { CreatePlanCard, hasCreatePlanDisplayData } from './CreatePlanCard'
import { CronCreatedCard } from './CronCreatedCard'
import { SkillManageCard } from './SkillManageCard'
import { SkillViewCard } from './SkillViewCard'
import { McpAppView, hasAvailableMcpApp } from './McpAppView'
import { ToolCollapseChevron } from './ToolCollapseChevron'
import { CollapsibleContent } from './CollapsibleContent'
import { AnsiPre } from './AnsiPre'
import { stripAnsi } from '../../utils/ansi'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { openConversationLink } from '../../utils/conversationDeepLink'
import type { FileDiff } from '../../types/toolCall'
import type { Thread, ThreadSummary } from '../../types/thread'
import {
  buildSkillManageDiff,
  formatSkillManageLabel,
  formatSkillManageRunningLabel,
  getSkillManageDisplay,
  shouldRenderSkillManageCard
} from '../../utils/skillManageToolDisplay'
import {
  formatSkillViewLabel,
  formatSkillViewRunningLabel,
  getSkillViewDisplay
} from '../../utils/skillViewToolDisplay'
import { useThreadStore } from '../../stores/threadStore'
import { useSubAgentStore, type SubAgentChild } from '../../stores/subAgentStore'
import { isToolItemLive } from '../../utils/toolCallAggregation'
import { formatDefaultToolResultForDisplay } from '../../utils/toolResultDisplay'
import {
  formatSubAgentMeta,
  getSubAgentAccent,
  getSubAgentIdentitySeed
} from '../../utils/subAgentPresentation'
import {
  formatRequestUserInputResultLines,
  type RequestUserInputResultLine
} from '../../utils/requestUserInputToolDisplay'
import { resolveCoreToolRenderPlan, type ToolRendererFamily } from '../../utils/toolRendererRegistry'
import { toAbsoluteWorkspacePath } from '../../utils/diffExtractor'
import { FileDiffStats } from './FileDiffStats'
import { parseWorkflowRunId, WorkflowToolCard } from '../workflow/WorkflowToolCard'

export type ShellRuntimeScope = 'conversation' | 'review' | 'none'

interface ToolCallCardProps {
  item: ConversationItem
  turnId: string
  turnRunning?: boolean
  shellRuntimeScope?: ShellRuntimeScope
}

function formatRunningToolLabel(
  rendererFamily: ToolRendererFamily | undefined,
  toolName: string,
  args: Record<string, unknown> | undefined,
  locale: AppLocale,
  streamingLabel: string,
  shellCommand?: string
): string {
  if (rendererFamily === 'shell') {
    const firstLine = shellCommand?.split(/\r?\n/, 1)[0]
    return firstLine
      ? translate(locale, 'toolCall.streaming.runningCommand', {
        command: firstLine.length > 80 ? `${firstLine.slice(0, 80)}…` : firstLine
      })
      : translate(locale, 'toolCall.runningCommand')
  }
  if (rendererFamily === 'cron' && args) {
    return formatCronRunningLabel(args, locale)
  }
  if (rendererFamily === 'skillManage' && args) {
    return formatSkillManageRunningLabel(args, locale)
  }
  if (rendererFamily === 'skillView' && args) {
    return formatSkillViewRunningLabel(args, locale)
  }
  if (rendererFamily === 'web' && args && !invocationNeedsCallingPrefix(toolName, args)) {
    return formatInvocationDisplay(toolName, args, locale) ?? streamingLabel
  }
  return streamingLabel
}

interface SubAgentLookupSources {
  childrenByParent: Map<string, SubAgentChild[]>
  threadList: ThreadSummary[]
  activeThread: Thread | null
}

function getFilename(path: string): string {
  return path.split(/[\\/]/).pop() ?? path
}

function formatFileToolLabel(
  operation: unknown,
  diff: FileDiff | undefined,
  fallbackLabel: string,
  locale: AppLocale
): string {
  if (!diff) return fallbackLabel
  const filename = getFilename(diff.filePath)
  const action = operation === 'write' && diff.isNewFile
    ? translate(locale, 'toolCall.created', { filename })
    : translate(locale, 'toolCall.edited', { filename })
  return action
}

function formatExpandedFileToolLabel(
  operation: unknown,
  diff: FileDiff | undefined,
  locale: AppLocale
): string {
  return operation === 'write' && diff?.isNewFile
    ? translate(locale, 'toolCall.createdFile')
    : translate(locale, 'toolCall.editedFile')
}

function FileToolDiffStats({ diff, colorized }: { diff: FileDiff; colorized: boolean }): JSX.Element | null {
  return (
    <FileDiffStats
      additions={diff.additions}
      deletions={diff.deletions}
      tone={colorized ? 'semantic' : 'inherit'}
      testId="tool-row-diff-stats"
    />
  )
}

function formatReadRange(args: Record<string, unknown> | undefined): string | undefined {
  const offset = toPositiveInt(args?.offset)
  const limit = toPositiveInt(args?.limit)
  if (offset && limit) return `L${offset}-${offset + limit - 1}`
  if (offset) return `L${offset}+`
  return undefined
}

function toPositiveInt(value: unknown): number | undefined {
  const parsed = typeof value === 'number' ? value : typeof value === 'string' ? Number.parseInt(value, 10) : NaN
  return Number.isFinite(parsed) && parsed > 0 ? parsed : undefined
}

function hasVisibleText(value: string | undefined): boolean {
  if (!value) return false
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index)
    if (code === 0x1b) {
      index++
      if (value.charCodeAt(index) === 0x5b) {
        while (index < value.length) {
          const ansiCode = value.charCodeAt(index)
          if (ansiCode >= 0x40 && ansiCode <= 0x7e) break
          index++
        }
      }
      continue
    }
    if (code > 32) return true
  }
  return false
}

function hasRenderableDiff(diff: FileDiff | undefined): boolean {
  return (diff?.diffHunks.length ?? 0) > 0
}

function resolveShellCommand(
  itemCommand: string | undefined,
  args: Record<string, unknown> | undefined,
  streamedCommand: string | null | undefined
): string | undefined {
  const finalArgumentCommand = typeof args?.command === 'string' ? args.command : undefined
  return [itemCommand, finalArgumentCommand, streamedCommand]
    .find((value) => typeof value === 'string' && value.trim().length > 0) ?? undefined
}

export const ToolCallCard = memo(function ToolCallCard({
  item,
  turnId,
  turnRunning = false,
  shellRuntimeScope = 'conversation'
}: ToolCallCardProps): JSX.Element {
  const locale = useLocale()
  const workspacePath = useConversationStore((state) => state.workspacePath)
  const threadId = useThreadStore((state) => state.activeThreadId)
  const [hovered, setHovered] = useState(false)
  const [expanded, setExpanded] = useState(false)
  const [renderExpanded, setRenderExpanded] = useState(false)
  const [autoExpanded, setAutoExpanded] = useState(false)
  const [userInteracted, setUserInteracted] = useState(false)
  const [elapsedMs, setElapsedMs] = useState(0)
  const autoExpandTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const toolName = item.toolName ?? 'tool'
  const rendererPlan = resolveCoreToolRenderPlan(item)
  const rendererFamily = rendererPlan?.family
  const rendererOperation = rendererPlan?.options.operation
  const args = item.arguments
  const isWebFetchTool = rendererFamily === 'web' && rendererOperation === 'fetch'
  const isSkillManageTool = rendererFamily === 'skillManage'
  const isSkillViewTool = rendererFamily === 'skillView'
  const isWorkflowTool = toolName === 'Workflow'
  const isTodoTool = rendererFamily === 'todo'
  const isShellTool = rendererFamily === 'shell'
  const isStreamingFileTool = rendererFamily === 'fileWrite'
  const streamingDisplay = rendererPlan || isWorkflowTool
    ? getStreamingToolDisplay(toolName, item.argumentsPreview ?? null, locale)
    : { label: translate(locale, 'toolCall.streaming.genericExternal', { toolName }) }
  const shellCommand = isShellTool
    ? resolveShellCommand(item.command, args, streamingDisplay.parsedPreview?.command)
    : undefined
  const shellDisplayArgs = isShellTool && shellCommand
    ? { ...args, command: shellCommand }
    : args
  const isRunning = isToolItemLive(item, { turnRunning })
  const toolResult = item.result ?? item.errorMessage ?? item.resultPreview
  const conversationShellRuntime = useConversationStore((state) =>
    shellRuntimeScope === 'conversation' && item.toolCallId
      ? state.shellRuntimeByCallId.get(item.toolCallId)
      : undefined
  )
  const reviewShellRuntime = useReviewPanelStore((state) =>
    shellRuntimeScope === 'review' && item.toolCallId
      ? state.shellRuntimeByCallId.get(item.toolCallId)
      : undefined
  )
  const liveShellRuntime = shellRuntimeScope === 'review'
    ? reviewShellRuntime
    : conversationShellRuntime
  const shellOutput = liveShellRuntime?.output ?? item.aggregatedOutput ?? toolResult ?? ''
  const skillManageDisplay = isSkillManageTool ? getSkillManageDisplay(args, item.result) : null
  const skillViewDisplay = isSkillViewTool ? getSkillViewDisplay(args, item.result) : null
  const success = (rendererPlan?.successOverride === true || item.success !== false)
    && (!isSkillManageTool || skillManageDisplay?.result?.success !== false)
    && (!isSkillViewTool || skillViewDisplay?.loaded !== false)

  useEffect(() => {
    if (expanded) {
      setRenderExpanded(true)
    }
  }, [expanded])

  useEffect(() => {
    const start = item.createdAt ? new Date(item.createdAt).getTime() : Date.now()
    if (!isRunning) {
      setElapsedMs(Math.max(0, Date.now() - start))
      return
    }
    setElapsedMs(Math.max(0, Date.now() - start))
    const interval = setInterval(() => {
      setElapsedMs(Math.max(0, Date.now() - start))
    }, 100)
    return () => clearInterval(interval)
  }, [isRunning, item.createdAt])

  const runningElapsedLabel = `${(elapsedMs / 1000).toFixed(1)}s`

  const fileDiff = useConversationStore((s) =>
    isStreamingFileTool ? s.itemDiffs.get(item.id) : undefined
  )
  const streamingFileDiff = useConversationStore((s) =>
    isStreamingFileTool ? s.streamingItemDiffs.get(item.id) : undefined
  )
  const planTodos = useConversationStore((s) => s.plan?.todos)
  const subAgentChildrenByParent = useSubAgentStore((s) => s.childrenByParent)
  const threadList = useThreadStore((s) => s.threadList)
  const activeThread = useThreadStore((s) => s.activeThread)
  const subAgentLookup: SubAgentLookupSources = {
    childrenByParent: subAgentChildrenByParent,
    threadList,
    activeThread
  }
  const skillManageDiff = isSkillManageTool ? buildSkillManageDiff(args, item.result, turnId) : null
  const renderableFileDiff = hasRenderableDiff(fileDiff) ? fileDiff : undefined
  const renderableStreamingFileDiff = hasRenderableDiff(streamingFileDiff) ? streamingFileDiff : undefined
  const hasRunningExpandableContent = isShellTool
    ? hasVisibleText(shellOutput)
    : isStreamingFileTool
      ? !!renderableStreamingFileDiff
      : false
  const hasCompletedExpandableContent = isShellTool
    ? hasVisibleText(shellOutput)
    : isStreamingFileTool
      ? !!renderableFileDiff || hasVisibleText(toolResult)
      : hasVisibleText(toolResult)
  const canExpandWhileRunning =
    !isWebFetchTool
    && !isSkillManageTool
    && !isSkillViewTool
    && !isTodoTool
    && hasRunningExpandableContent
  const canExpandCompleted =
    !isWebFetchTool
    && !isSkillManageTool
    && !isSkillViewTool
    && !isTodoTool
    && hasCompletedExpandableContent
  const autoExpandEligible = (isShellTool || isStreamingFileTool)
    && (isRunning ? hasRunningExpandableContent : hasCompletedExpandableContent)
  const hasFinalArgs = args != null && Object.keys(args).length > 0
  const subAgentRunningLabel = hasFinalArgs
    ? formatSubAgentRunningLabel(rendererOperation, args, locale, subAgentLookup)
    : null
  const runningBaseLabel = subAgentRunningLabel
    ?? formatRunningToolLabel(
      rendererFamily,
      toolName,
      hasFinalArgs ? shellDisplayArgs : undefined,
      locale,
      streamingDisplay.label,
      shellCommand
    )
  const runningLabel = isStreamingFileTool
    ? formatFileToolLabel(rendererOperation, renderableStreamingFileDiff, runningBaseLabel, locale)
    : runningBaseLabel

  function toggleExpand(): void {
    if ((isRunning && canExpandWhileRunning) || (!isRunning && canExpandCompleted)) {
      setUserInteracted(true)
      if (autoExpandTimerRef.current != null) {
        clearTimeout(autoExpandTimerRef.current)
        autoExpandTimerRef.current = null
      }
      setAutoExpanded(false)
      setExpanded((v) => !v)
    }
  }

  useEffect(() => {
    const canExpand = isRunning ? canExpandWhileRunning : canExpandCompleted
    if (canExpand) return
    if (autoExpandTimerRef.current != null) {
      clearTimeout(autoExpandTimerRef.current)
      autoExpandTimerRef.current = null
    }
    if (expanded) {
      setExpanded(false)
    }
    if (autoExpanded) {
      setAutoExpanded(false)
    }
  }, [autoExpanded, canExpandCompleted, canExpandWhileRunning, expanded, isRunning])

  useEffect(() => {
    if (!autoExpandEligible) {
      if (autoExpandTimerRef.current != null) {
        clearTimeout(autoExpandTimerRef.current)
        autoExpandTimerRef.current = null
      }
      if (autoExpanded) {
        setAutoExpanded(false)
      }
      return
    }

    if (isRunning) {
      if (!userInteracted && !expanded && autoExpandTimerRef.current == null) {
        autoExpandTimerRef.current = setTimeout(() => {
          setExpanded(true)
          setAutoExpanded(true)
          autoExpandTimerRef.current = null
        }, 400)
      }
      return
    }

    if (autoExpandTimerRef.current != null) {
      clearTimeout(autoExpandTimerRef.current)
      autoExpandTimerRef.current = null
    }

    const shouldAutoCollapse = !userInteracted && expanded && autoExpanded
    if (shouldAutoCollapse) {
      setExpanded(false)
      setAutoExpanded(false)
      return
    }

    if (autoExpanded) {
      setAutoExpanded(false)
    }
  }, [autoExpandEligible, autoExpanded, expanded, isRunning, userInteracted])

  useEffect(() => {
    return () => {
      if (autoExpandTimerRef.current != null) {
        clearTimeout(autoExpandTimerRef.current)
        autoExpandTimerRef.current = null
      }
    }
  }, [])

  if (rendererFamily === 'createPlan' && hasCreatePlanDisplayData(item)) {
    return <CreatePlanCard item={item} locale={locale} />
  }

  if (
    rendererFamily === 'cron'
    && !isRunning
    && success
    && hasCronCreatedDisplayData(item.result, locale)
  ) {
    return <CronCreatedCard item={item} locale={locale} />
  }

  if (
    isSkillManageTool
    && !isRunning
    && success
    && shouldRenderSkillManageCard(args, item.result)
  ) {
    return <SkillManageCard item={item} locale={locale} diff={skillManageDiff} />
  }

  if (
    isSkillViewTool
    && !isRunning
    && success
    && skillViewDisplay?.loaded
  ) {
    return <SkillViewCard item={item} locale={locale} />
  }

  const workflowRunId = !isRunning ? parseWorkflowRunId(toolName, item.result) : null
  if (workflowRunId && threadId) {
    return <WorkflowToolCard threadId={threadId} runId={workflowRunId} createdAt={item.createdAt} />
  }

  const subAgentDisplay = !isRunning
    ? getSubAgentToolDisplay(rendererOperation, args, item.result, success, locale, subAgentLookup)
    : null
  if (subAgentDisplay) {
    return <SubAgentToolResultCard display={subAgentDisplay} locale={locale} />
  }

  if (!isRunning && hasAvailableMcpApp(item)) {
    return <McpAppView item={item} threadId={threadId} turnId={turnId} />
  }

  if (isRunning) {
    const runningExpanded = expanded && canExpandWhileRunning
    const runningDisplayLabel = runningExpanded && isStreamingFileTool
      ? formatExpandedFileToolLabel(rendererOperation, renderableStreamingFileDiff, locale)
      : runningExpanded && isShellTool
        ? translate(locale, 'toolCall.runningCommand')
      : runningLabel
    const runningFilePath = renderableStreamingFileDiff?.filePath
      ?? streamingDisplay.parsedPreview?.path
    const runningResolvedPath = runningFilePath && workspacePath
      ? toAbsoluteWorkspacePath(workspacePath, runningFilePath)
      : runningFilePath ?? undefined
    return (
      <div
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        style={{
          borderRadius: '4px',
          overflow: 'hidden',
          border: runningExpanded ? '1px solid var(--border-default)' : 'none'
        }}
      >
        <button
          onClick={toggleExpand}
          onFocus={() => setHovered(true)}
          onBlur={() => setHovered(false)}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            width: '100%',
            padding: '4px 8px',
            background: runningExpanded ? 'var(--bg-tertiary)' : 'transparent',
            border: 'none',
            borderBottom: runningExpanded ? '1px solid var(--border-default)' : 'none',
            borderRadius: runningExpanded ? '4px 4px 0 0' : '4px',
            color: hovered || runningExpanded ? 'var(--text-secondary)' : 'var(--text-dimmed)',
            fontSize: '13px',
            textAlign: 'left',
            cursor: canExpandWhileRunning ? 'pointer' : 'default'
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
              maxWidth: '100%'
            }}
          >
            {runningFilePath && !runningExpanded ? (
              <ActionTooltip
                label={runningResolvedPath ?? runningFilePath}
                wrapperStyle={{ minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
              >
                <span
                  className="tool-running-gradient-text"
                  style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
                >
                  {runningDisplayLabel}
                </span>
              </ActionTooltip>
            ) : (
              <span
                className="tool-running-gradient-text"
                style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
              >
                {runningDisplayLabel}
              </span>
            )}
            {!runningExpanded && renderableStreamingFileDiff && (
              <FileToolDiffStats diff={renderableStreamingFileDiff} colorized={hovered} />
            )}
            {canExpandWhileRunning && (
              <ToolCollapseChevron expanded={runningExpanded} visible={hovered || runningExpanded} />
            )}
          </span>
          <span style={{ color: 'var(--text-dimmed)', flexShrink: 0 }}>
            {runningElapsedLabel}
          </span>
        </button>

        <CollapsibleContent
          expanded={expanded && canExpandWhileRunning}
          renderExpanded={renderExpanded && canExpandWhileRunning}
          setRenderExpanded={setRenderExpanded}
        >
          <div
            style={{
              background: 'var(--bg-secondary)',
              padding: isStreamingFileTool && renderableStreamingFileDiff ? 0 : '8px'
            }}
          >
            {isShellTool ? (
              <ExpandedContent
                itemId={item.id}
                rendererFamily={rendererFamily}
                rendererOptions={rendererPlan?.options}
                toolName={toolName}
                args={shellDisplayArgs}
                result={shellOutput}
                success
                fileDiff={undefined}
                locale={locale}
                shellCommand={shellCommand}
                planTodos={planTodos}
              />
            ) : isStreamingFileTool ? (
              renderableStreamingFileDiff ? (
                <InlineDiffView
                  diff={renderableStreamingFileDiff}
                  streaming
                  variant="embedded"
                  headerMode="compact"
                  presentation="conversation-file-tool"
                  resolvedPath={runningResolvedPath}
                  locale={locale}
                />
              ) : null
            ) : null}
          </div>
        </CollapsibleContent>
      </div>
    )
  }

  const completedToolSearchLabel = success && rendererFamily === 'deferredSearch'
    ? formatToolSearchCompletedLabel(toolName, item.result, locale)
    : null
  const fallbackLabel = completedToolSearchLabel
    ?? (isSkillManageTool
      ? formatSkillManageLabel(args, item.result, locale)
      : isSkillViewTool
        ? formatSkillViewLabel(args, locale)
        : isShellTool && !shellCommand
          ? translate(locale, 'toolCall.ranCommand')
        : rendererPlan || isWorkflowTool
          ? formatCollapsedToolLabel(toolName, shellDisplayArgs, locale, { planTodos })
          : translate(locale, 'toolCall.called', { toolName }))
  const label = isStreamingFileTool
    ? formatFileToolLabel(rendererOperation, fileDiff, fallbackLabel, locale)
    : fallbackLabel
  const failureText = item.errorMessage ?? item.resultPreview ?? item.result ?? shellOutput
  const failurePreviewSource = isSkillManageTool
    ? (skillManageDisplay?.message ?? '')
    : isSkillViewTool
      ? (skillViewDisplay?.message ?? '')
      : (failureText ?? '')
  const hasFailurePreview = hasVisibleText(failurePreviewSource)
  const failedPreview = hasFailurePreview
    ? stripAnsi(failurePreviewSource.slice(0, 512)).trim()
    : ''
  const hasFlushWebSearchTable =
    rendererFamily === 'web'
    && rendererOperation === 'search'
    && parseWebSearchResultDisplay(item.result)?.kind === 'results'
  const hasInlineFileDiff = isStreamingFileTool && !!renderableFileDiff
  const hasFlushReadFile = rendererFamily === 'readFile'
  const completedExpanded = canExpandCompleted && expanded
  const completedDisplayLabel = completedExpanded && isStreamingFileTool
    ? formatExpandedFileToolLabel(rendererOperation, renderableFileDiff, locale)
    : completedExpanded && isShellTool
      ? translate(locale, 'toolCall.ranCommand')
    : completedExpanded && rendererFamily === 'readFile'
      ? translate(locale, 'toolCall.readFile.file')
      : label
  const readFilePath = rendererFamily === 'readFile' && typeof args?.path === 'string'
    ? args.path
    : undefined
  const completedFilePath = renderableFileDiff?.filePath ?? readFilePath
  const completedResolvedPath = completedFilePath && workspacePath
    ? toAbsoluteWorkspacePath(workspacePath, completedFilePath)
    : completedFilePath
  const completedRowColor = hovered || completedExpanded ? 'var(--text-secondary)' : 'var(--text-dimmed)'

  return (
    <div
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        borderRadius: '4px',
        overflow: 'hidden',
        border: completedExpanded ? '1px solid var(--border-default)' : 'none'
      }}
    >
      <button
        onClick={toggleExpand}
        onFocus={() => setHovered(true)}
        onBlur={() => setHovered(false)}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          width: '100%',
          padding: '3px 6px',
          background: completedExpanded ? 'var(--bg-tertiary)' : 'transparent',
          border: 'none',
          borderBottom: completedExpanded ? '1px solid var(--border-default)' : 'none',
          cursor: canExpandCompleted ? 'pointer' : 'default',
          color: success ? completedRowColor : 'var(--error)',
          fontSize: '12px',
          textAlign: 'left',
          borderRadius: completedExpanded ? '4px 4px 0 0' : '4px'
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
            color: success ? completedRowColor : 'var(--error)'
          }}
        >
          {success && completedFilePath && !completedExpanded ? (
            <ActionTooltip
              label={completedResolvedPath ?? completedFilePath}
              wrapperStyle={{ minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
            >
              <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {completedDisplayLabel}
              </span>
            </ActionTooltip>
          ) : (
            <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {success
                ? completedDisplayLabel
                : isWorkflowTool
                  ? formatWorkflowFailureLabel(args, locale)
                  : translate(locale, 'toolCall.failed', { label })}
              {!success && hasFailurePreview && failedPreview && (
                <span style={{ color: 'var(--error)', marginLeft: '6px' }}>
                  - {failedPreview.slice(0, 80)}{failedPreview.length > 80 ? '…' : ''}
                </span>
              )}
            </span>
          )}
          {success && !completedExpanded && renderableFileDiff && (
            <FileToolDiffStats diff={renderableFileDiff} colorized={hovered} />
          )}
          {canExpandCompleted && (
            <ToolCollapseChevron expanded={expanded} visible={hovered || expanded} />
          )}
        </span>
      </button>

      {canExpandCompleted && (
        <CollapsibleContent
          expanded={expanded}
          renderExpanded={renderExpanded}
          setRenderExpanded={setRenderExpanded}
        >
          <div
            data-testid="tool-expanded-content"
            style={{
              background: 'var(--bg-secondary)',
              padding: hasFlushWebSearchTable || hasInlineFileDiff || hasFlushReadFile ? 0 : '8px'
            }}
          >
            <ExpandedContent
              itemId={item.id}
              rendererFamily={rendererFamily}
              rendererOptions={rendererPlan?.options}
              toolName={toolName}
              args={shellDisplayArgs}
              result={isShellTool ? shellOutput : toolResult}
              success={success}
              fileDiff={renderableFileDiff ? { diff: renderableFileDiff } : undefined}
              locale={locale}
              workspacePath={workspacePath}
              shellCommand={shellCommand}
              planTodos={planTodos}
            />
          </div>
        </CollapsibleContent>
      )}
    </div>
  )
})

interface ExpandedContentProps {
  itemId: string
  rendererFamily?: ToolRendererFamily
  rendererOptions?: Readonly<Record<string, unknown>>
  toolName: string
  args: Record<string, unknown> | undefined
  result: string | undefined
  success: boolean
  fileDiff: { diff: FileDiff } | undefined
  locale: AppLocale
  workspacePath?: string
  shellCommand?: string
  planTodos?: Array<{ id: string; content: string }>
}

function ExpandedContent({
  itemId,
  rendererFamily,
  rendererOptions,
  toolName,
  args,
  result,
  success,
  fileDiff,
  locale,
  workspacePath,
  shellCommand,
  planTodos
}: ExpandedContentProps): JSX.Element {
  if (rendererFamily === 'createPlan') {
    const parsedPlan = parseCompletedCreatePlanArgs(args)
    return (
      <PlanToolOutput
        itemId={itemId}
        title={parsedPlan.title}
        overview={parsedPlan.overview}
        content={parsedPlan.content}
        todos={parsedPlan.todos}
        locale={locale}
      />
    )
  }

  if (rendererFamily === 'fileWrite' && fileDiff) {
    return (
      <InlineDiffView
        diff={fileDiff.diff}
        variant="embedded"
        headerMode="compact"
        presentation="conversation-file-tool"
        resolvedPath={workspacePath
          ? toAbsoluteWorkspacePath(workspacePath, fileDiff.diff.filePath)
          : fileDiff.diff.filePath}
        locale={locale}
      />
    )
  }

  if (rendererFamily === 'readFile') {
    const filePath = typeof args?.path === 'string' ? args.path : ''
    const resultText = formatDefaultToolResultForDisplay(result)
    const resolvedPath = filePath && workspacePath
      ? toAbsoluteWorkspacePath(workspacePath, filePath)
      : filePath
    return (
      <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: '12px', lineHeight: 1.5 }}>
        {filePath && (
          <FileResultHeader
            filePath={filePath}
            resolvedPath={resolvedPath}
            meta={formatReadRange(args)}
            copyPath
            inlineStats
            locale={locale}
          />
        )}
        {resultText && (
          <div style={{ padding: '6px 8px' }}>
            <AnsiPre
              text={resultText}
              truncatedLinesOver={10}
              maxHeight={160}
              colorWhenNoSgr={success ? 'var(--text-secondary)' : 'var(--error)'}
            />
          </div>
        )}
      </div>
    )
  }

  if (rendererFamily === 'cron') {
    const lines = formatCronResultLines(result, locale)
    if (lines && lines.length > 0) {
      const errSample = translate(locale, 'cron.result.errorPrefix', { error: 'x' })
      const errMarker = errSample.indexOf('x')
      const errPrefix = errMarker >= 0 ? errSample.slice(0, errMarker) : 'Error: '
      return (
        <div className="selectable" style={{ fontSize: '12px', lineHeight: 1.5, color: 'var(--text-secondary)' }}>
          <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px', fontSize: '11px', display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span aria-hidden>⏰</span>
            <span>Cron</span>
          </div>
          {lines.map((line, i) => (
            <div key={i} style={{ color: line.startsWith(errPrefix) ? 'var(--error)' : 'var(--text-secondary)' }}>
              {line}
            </div>
          ))}
        </div>
      )
    }
  }

  if (rendererFamily === 'requestUserInput') {
    const lines = formatRequestUserInputResultLines(args, result, locale)
    if (lines && lines.length > 0) {
      return <RequestUserInputResultList lines={lines} />
    }
  }

  if (rendererFamily === 'web') {
    if (rendererOptions?.operation === 'search') {
      const parsedSearch = parseWebSearchResultDisplay(result)
      if (parsedSearch?.kind === 'results') {
        return <WebSearchResultsTable rows={parsedSearch.rows} locale={locale} />
      }
    }

    const lines = formatResultSummary(toolName, result)
    const inv = formatInvocationDisplay(toolName, args, locale)
    const section = getWebToolSectionLabel(toolName, locale)
    const icon = getWebToolIcon(toolName)
    const errPrefix = 'Error: '

    if (lines && lines.length > 0) {
      return (
        <div className="selectable" style={{ fontSize: '12px', lineHeight: 1.5, color: 'var(--text-secondary)' }}>
          <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px', fontSize: '11px', display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span aria-hidden>{icon}</span>
            <span>{section}</span>
          </div>
          {inv && (
            <div style={{ color: 'var(--text-dimmed)', marginBottom: '8px', fontSize: '11px', lineHeight: 1.4 }}>
              {inv}
            </div>
          )}
          {lines.map((line, i) => (
            <div key={i} style={{ color: line.startsWith(errPrefix) ? 'var(--error)' : 'var(--text-secondary)', fontFamily: 'var(--font-mono)', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
              {line}
            </div>
          ))}
        </div>
      )
    }
  }

  if (rendererFamily === 'shell') {
    const output = result ?? ''

    return (
      <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: '12px', lineHeight: '1.5', color: 'var(--text-secondary)' }}>
        {shellCommand && (
          <div
            data-testid="shell-command"
            style={{
              display: 'grid',
              gridTemplateColumns: '12px minmax(0, 1fr)',
              gap: '4px',
              color: 'var(--text-dimmed)',
              marginBottom: '6px'
            }}
          >
            <span aria-hidden style={{ color: 'var(--text-dimmed)' }}>$</span>
            <span style={{ color: 'var(--text-primary)', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
              {shellCommand}
            </span>
          </div>
        )}
        {output ? (
          <AnsiPre
            text={output}
            truncatedLinesOver={40}
            maxHeight={200}
            colorWhenNoSgr={success ? 'var(--text-secondary)' : 'var(--error)'}
          />
        ) : null}
      </div>
    )
  }

  const resultText = formatDefaultToolResultForDisplay(result)
  const invocation = rendererFamily
    ? formatExpandedInvocation(toolName, args, locale, { planTodos })
    : null

  return (
    <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: '12px', lineHeight: '1.5' }}>
      {invocation && (
        <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px', fontSize: '11px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {invocation}
        </div>
      )}
      {resultText && (
        <AnsiPre
          text={resultText}
          truncatedLinesOver={10}
          maxHeight={160}
          colorWhenNoSgr={success ? 'var(--text-secondary)' : 'var(--error)'}
        />
      )}
    </div>
  )
}

function RequestUserInputResultList({ lines }: { lines: RequestUserInputResultLine[] }): JSX.Element {
  return (
    <div
      className="selectable"
      style={{
        display: 'grid',
        gap: '5px',
        fontSize: '12px',
        lineHeight: 1.5,
        color: 'var(--text-secondary)'
      }}
    >
      {lines.map((line, index) => (
        <div key={`${line.question}-${index}`} style={{ wordBreak: 'break-word' }}>
          <span style={{ color: 'var(--text-dimmed)' }}>{line.question}: </span>
          <span>{line.answer}</span>
        </div>
      ))}
    </div>
  )
}

export function WebSearchResultsTable({
  rows,
  locale
}: {
  rows: WebSearchResultRow[]
  locale: AppLocale
}): JSX.Element {
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)

  const openResult = (url: string): void => {
    if (!workspacePath || !currentThreadId) return
    void openConversationLink({
      target: url,
      workspacePath,
      threadId: currentThreadId,
      t: (key) => translate(locale, key)
    })
  }

  return (
    <div
      style={{
        overflow: 'hidden',
        border: 'none',
        borderRadius: 0
      }}
    >
      <table
        style={{
          width: '100%',
          borderCollapse: 'collapse',
          tableLayout: 'fixed',
          fontSize: '12px'
        }}
      >
        <thead>
          <tr style={{ background: 'var(--bg-tertiary)', color: 'var(--text-dimmed)' }}>
            <th
              scope="col"
              style={{
                width: '64%',
                padding: '6px 8px',
                textAlign: 'left',
                fontWeight: 500,
                borderBottom: '1px solid var(--border-default)'
              }}
            >
              {translate(locale, 'toolCall.webSearch.tableTitle')}
            </th>
            <th
              scope="col"
              style={{
                width: '36%',
                padding: '6px 8px',
                textAlign: 'left',
                fontWeight: 500,
                borderBottom: '1px solid var(--border-default)'
              }}
            >
              {translate(locale, 'toolCall.webSearch.tableLink')}
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr
              key={`${row.url}-${index}`}
              style={{
                borderTop: index === 0 ? 'none' : '1px solid var(--border-muted, var(--border-default))'
              }}
            >
              <WebSearchResultCell
                label={row.title}
                title={row.url}
                onClick={() => openResult(row.url)}
              />
              <WebSearchResultCell
                label={row.linkLabel}
                title={row.url}
                onClick={() => openResult(row.url)}
                monospace
              />
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function WebSearchResultCell({
  label,
  title,
  onClick,
  monospace = false
}: {
  label: string
  title: string
  onClick: () => void
  monospace?: boolean
}): JSX.Element {
  return (
    <td style={{ padding: 0, minWidth: 0 }}>
      <ActionTooltip label={title} wrapperStyle={{ display: 'block', width: '100%', minWidth: 0, overflow: 'hidden' }}>
      <button
        type="button"
        onClick={onClick}
        style={{
          width: '100%',
          minHeight: '30px',
          padding: '5px 8px',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-secondary)',
          cursor: 'pointer',
          textAlign: 'left',
          fontSize: '12px',
          fontFamily: monospace ? 'var(--font-mono)' : 'inherit',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap'
        }}
        onMouseEnter={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover, rgba(255,255,255,0.06))'
          ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)'
        }}
        onMouseLeave={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
          ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-secondary)'
        }}
      >
        {label}
      </button>
      </ActionTooltip>
    </td>
  )
}

interface SubAgentToolDisplay {
  titleKey: string
  name: string
  subtitle: string
  meta: string
  prompt: string | null
  accentColor: string
  childThreadId: string | null
  message: string | null
  success: boolean
  tone: 'normal' | 'warning' | 'error'
}

function SubAgentToolResultCard({
  display,
  locale
}: {
  display: SubAgentToolDisplay
  locale: AppLocale
}): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const [hovered, setHovered] = useState(false)
  const hasMessage = !!display.message
  const hasPrompt = !!display.prompt
  const normalTextColor = hovered || expanded ? 'var(--text-secondary)' : 'var(--text-dimmed)'
  const textColor = display.tone === 'error'
    ? 'var(--error)'
    : display.tone === 'warning'
      ? 'var(--warning)'
      : normalTextColor
  const rowContent = (
    <span
      data-testid="tool-row-title-group"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '3px',
        flex: '0 1 auto',
        minWidth: 0,
        maxWidth: '100%'
      }}
    >
      <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
        {renderSubAgentTitle(locale, display.titleKey, display.name, display.accentColor)}
        {display.meta && <span style={subAgentMetaStyle}>({display.meta})</span>}
        {display.subtitle && <span style={subAgentMetaStyle}>{display.subtitle}</span>}
      </span>
      {hasMessage && (
        <ToolCollapseChevron expanded={expanded} visible={hovered || expanded} />
      )}
    </span>
  )
  const rowStyle: CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    width: '100%',
    padding: '4px 6px',
    background: expanded ? 'var(--bg-tertiary)' : 'transparent',
    border: 'none',
    borderBottom: expanded ? '1px solid var(--border-default)' : 'none',
    borderRadius: expanded ? '4px 4px 0 0' : '4px',
    color: textColor,
    fontSize: '12px',
    textAlign: 'left'
  }

  return (
    <div
      style={{
        borderRadius: '4px',
        overflow: 'hidden',
        border: expanded ? '1px solid var(--border-default)' : 'none'
      }}
    >
      {hasMessage ? (
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          onMouseEnter={() => setHovered(true)}
          onMouseLeave={() => setHovered(false)}
          onFocus={() => setHovered(true)}
          onBlur={() => setHovered(false)}
          style={{ ...rowStyle, cursor: 'pointer' }}
          aria-label={expanded ? translate(locale, 'toolCall.subAgent.collapse') : translate(locale, 'toolCall.subAgent.expand')}
        >
          <span style={subAgentResultContentStyle}>
            {rowContent}
            {hasPrompt && (
              <ActionTooltip label={display.prompt ?? ''} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden' }}>
                <span style={{ ...subAgentPromptStyle, display: 'block' }}>
                  {translate(locale, 'toolCall.subAgent.prompt', { prompt: display.prompt ?? '' })}
                </span>
              </ActionTooltip>
            )}
          </span>
        </button>
      ) : (
        <div
          onMouseEnter={() => setHovered(true)}
          onMouseLeave={() => setHovered(false)}
          style={rowStyle}
        >
          <span style={subAgentResultContentStyle}>
            {rowContent}
            {hasPrompt && (
              <ActionTooltip label={display.prompt ?? ''} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden' }}>
                <span style={{ ...subAgentPromptStyle, display: 'block' }}>
                  {translate(locale, 'toolCall.subAgent.prompt', { prompt: display.prompt ?? '' })}
                </span>
              </ActionTooltip>
            )}
          </span>
        </div>
      )}
      {expanded && hasMessage && (
        <div
          className="selectable"
          style={{
            padding: '8px',
            background: 'var(--bg-secondary)',
            color: textColor,
            fontSize: '12px',
            lineHeight: 1.5,
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word'
          }}
        >
          {display.message}
        </div>
      )}
    </div>
  )
}

const subAgentResultContentStyle: CSSProperties = {
  display: 'inline-flex',
  flexDirection: 'column',
  gap: '2px',
  minWidth: 0,
  maxWidth: '100%'
}

const subAgentMetaStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  marginLeft: 6
}

const subAgentPromptStyle: CSSProperties = {
  color: 'var(--text-dimmed)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const SUB_AGENT_NAME_TOKEN = '__DOTCRAFT_SUB_AGENT_NAME__'

export function renderSubAgentTitle(
  locale: AppLocale,
  titleKey: string,
  name: string,
  accentColor: string
): JSX.Element {
  const template = translate(locale, titleKey, { name: SUB_AGENT_NAME_TOKEN })
  const parts = template.split(SUB_AGENT_NAME_TOKEN)
  if (parts.length === 1) {
    return <span>{translate(locale, titleKey, { name })}</span>
  }

  return (
    <span>
      {parts.map((part, index) => (
        <span key={`${part}-${index}`}>
          {part}
          {index < parts.length - 1 && (
            <span style={{ color: accentColor, fontWeight: 600 }}>{name}</span>
          )}
        </span>
      ))}
    </span>
  )
}

function getSubAgentToolDisplay(
  operation: unknown,
  args: Record<string, unknown> | undefined,
  result: string | undefined,
  success: boolean,
  locale: AppLocale,
  lookup: SubAgentLookupSources
): SubAgentToolDisplay | null {
  if (!isSubAgentOperation(operation)) return null
  if (operation === 'wait' && result === undefined) return null
  const parsed = parseJsonObject(result)
  const profile = getString(parsed, 'profileName') ?? getString(args, 'profile')
  const runtimeType = getString(parsed, 'runtimeType')
  const agentRole = getString(parsed, 'agentRole') ?? getString(args, 'agentRole')
  const agentPath = getString(parsed, 'agentPath')
    ?? getString(args, 'target')
  const explicitChildThreadId = getString(parsed, 'childThreadId')
    ?? getString(parsed, 'agentId')
    ?? getString(args, 'agentId')
    ?? getString(args, 'childThreadId')
  const matchedChild = findSubAgentChild(lookup, explicitChildThreadId, agentPath)
  const childThreadId = explicitChildThreadId ?? matchedChild?.childThreadId ?? null
  const resolvedAgentPath = agentPath ?? matchedChild?.agentPath ?? null
  const status = getString(parsed, 'status')?.toLowerCase()
  const error = getString(parsed, 'error') ?? getString(parsed, 'message')
  const message = operation === 'wait'
    ? getString(parsed, 'message') ?? getString(parsed, 'result')
    : null
  const label = resolveSubAgentDisplayName(parsed, args, childThreadId, resolvedAgentPath, locale, lookup)
  const prompt = operation === 'spawn'
    ? getString(args, 'message') ?? getString(args, 'agentPrompt')
    : null
  const isTimeout = operation === 'wait'
    && (status === 'timeout' || isTimeoutMessage(error) || isTimeoutMessage(message))
  const tone: SubAgentToolDisplay['tone'] = isTimeout
    ? 'warning'
    : (!success || status === 'failed')
      ? 'error'
      : 'normal'
  const titleKey = isTimeout
    ? 'toolCall.subAgent.timeout'
    : !success || status === 'failed'
      ? 'toolCall.subAgent.failed'
      : getSubAgentCompletedTitleKey(operation)
  return {
    titleKey,
    name: label,
    subtitle: '',
    meta: formatSubAgentMeta({ agentRole, profileName: profile, runtimeType }),
    prompt: prompt ? truncateSubAgentPrompt(prompt, 120) : null,
    accentColor: getSubAgentAccent(getSubAgentIdentitySeed({
      agentPath: resolvedAgentPath,
      childThreadId,
      nickname: label
    })),
    childThreadId,
    message: isTimeout
      ? (message && !isTimeoutMessage(message) ? message : null)
      : !success && error
        ? error
        : message,
    success: tone !== 'error',
    tone
  }
}

function truncateSubAgentPrompt(value: string, maxChars: number): string {
  const trimmed = value.trim().replace(/\s+/g, ' ')
  const chars = Array.from(trimmed)
  if (chars.length <= maxChars) return trimmed
  return `${chars.slice(0, maxChars - 1).join('')}…`
}

function formatSubAgentRunningLabel(
  operation: unknown,
  args: Record<string, unknown> | undefined,
  locale: AppLocale,
  lookup: SubAgentLookupSources
): string | null {
  if (!isSubAgentOperation(operation)) return null
  const explicitChildThreadId = getString(args, 'childThreadId') ?? getString(args, 'agentId')
  const agentPath = getString(args, 'target')
  const matchedChild = findSubAgentChild(lookup, explicitChildThreadId, agentPath)
  const childThreadId = explicitChildThreadId ?? matchedChild?.childThreadId ?? null
  const resolvedAgentPath = agentPath ?? matchedChild?.agentPath ?? null
  const label = resolveSubAgentDisplayName(undefined, args, childThreadId, resolvedAgentPath, locale, lookup)
  const key = operation === 'spawn'
    ? 'toolCall.subAgent.starting'
    : operation === 'wait'
      ? 'toolCall.subAgent.waiting'
      : getSubAgentRunningTitleKey(operation)
  return translate(locale, key, { name: label })
}

function getSubAgentCompletedTitleKey(operation: SubAgentOperation): string {
  if (operation === 'spawn') return 'toolCall.subAgent.spawned'
  if (operation === 'wait') return 'toolCall.subAgent.waited'
  if (operation === 'sendMessage') return 'toolCall.subAgent.sentMessage'
  if (operation === 'followupTask') return 'toolCall.subAgent.followedUp'
  if (operation === 'list') return 'toolCall.subAgent.listed'
  if (operation === 'sendInput') return 'toolCall.subAgent.sentInput'
  if (operation === 'resume') return 'toolCall.subAgent.resumed'
  return 'toolCall.subAgent.closed'
}

function getSubAgentRunningTitleKey(operation: SubAgentOperation): string {
  if (operation === 'sendMessage') return 'toolCall.subAgent.sendingMessage'
  if (operation === 'followupTask') return 'toolCall.subAgent.followingUp'
  if (operation === 'list') return 'toolCall.subAgent.listing'
  if (operation === 'sendInput') return 'toolCall.subAgent.sendingInput'
  if (operation === 'resume') return 'toolCall.subAgent.resuming'
  return 'toolCall.subAgent.closing'
}

function resolveSubAgentDisplayName(
  parsed: Record<string, unknown> | undefined,
  args: Record<string, unknown> | undefined,
  childThreadId: string | null | undefined,
  agentPath: string | null | undefined,
  locale: AppLocale,
  lookup: SubAgentLookupSources
): string {
  const explicitDisplayName = getString(parsed, 'displayName') ?? getString(args, 'displayName')
  if (explicitDisplayName && !isThreadIdLike(explicitDisplayName, childThreadId)) return explicitDisplayName

  const matchedChild = findSubAgentChild(lookup, childThreadId, agentPath)
  if (matchedChild?.nickname && !isThreadIdLike(matchedChild.nickname, childThreadId)) {
    return matchedChild.nickname
  }

  if (childThreadId) {
    const threads = lookup.activeThread ? [lookup.activeThread, ...lookup.threadList] : lookup.threadList
    const thread = threads.find((entry) => entry.id === childThreadId)
    if (thread?.displayName && !isThreadIdLike(thread.displayName, childThreadId)) return thread.displayName
    const sourceName = thread?.source?.subAgent?.agentNickname
    if (sourceName && !isThreadIdLike(sourceName, childThreadId)) return sourceName
  }

  const explicitName = getString(parsed, 'agentNickname')
    ?? getString(parsed, 'nickname')
    ?? getString(args, 'agentNickname')
    ?? getString(args, 'nickname')
    ?? getString(parsed, 'taskName')
    ?? getString(args, 'taskName')
    ?? getAgentPathSegment(agentPath ?? childThreadId)
  if (explicitName && !isThreadIdLike(explicitName, childThreadId)) return explicitName

  return translate(locale, 'toolCall.subAgent.agent')
}

function findSubAgentChild(
  lookup: SubAgentLookupSources,
  childThreadId: string | null | undefined,
  agentPath: string | null | undefined
): SubAgentChild | null {
  for (const children of lookup.childrenByParent.values()) {
    const child = children.find((entry) =>
      (childThreadId != null && entry.childThreadId === childThreadId)
      || (agentPath != null && entry.agentPath === agentPath)
    )
    if (child) return child
  }
  return null
}

function getAgentPathSegment(value: string | null | undefined): string | null {
  if (!value?.startsWith('/root/')) return null
  const parts = value.split('/').filter((part) => part.length > 0)
  return parts.length > 0 ? parts[parts.length - 1] : null
}

function isThreadIdLike(value: string, childThreadId: string | null | undefined): boolean {
  const normalized = value.trim()
  return normalized.length === 0
    || normalized === childThreadId
    || /^thread[_-]/i.test(normalized)
}

function isTimeoutMessage(value: string | null): boolean {
  if (!value) return false
  const normalized = value.toLowerCase()
  return normalized.includes('timed out') || normalized.includes('timeout')
}

type SubAgentOperation = 'spawn' | 'wait' | 'sendInput' | 'sendMessage' | 'followupTask' | 'resume' | 'list' | 'close'

function isSubAgentOperation(operation: unknown): operation is SubAgentOperation {
  return operation === 'spawn'
    || operation === 'wait'
    || operation === 'sendInput'
    || operation === 'sendMessage'
    || operation === 'followupTask'
    || operation === 'resume'
    || operation === 'list'
    || operation === 'close'
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

function parseCompletedCreatePlanArgs(args: Record<string, unknown> | undefined): {
  title: string
  overview: string
  content: string
  todos: Array<{ id: string; content: string; status: 'pending' | 'in_progress' | 'completed' | 'cancelled' }>
} {
  const title = typeof args?.title === 'string' ? args.title : ''
  const overview = typeof args?.overview === 'string' ? args.overview : ''
  const content = typeof args?.plan === 'string' ? args.plan : ''
  const todos = Array.isArray(args?.todos)
    ? args.todos
      .filter((entry): entry is Record<string, unknown> => typeof entry === 'object' && entry != null)
      .map((entry, index) => ({
        id: typeof entry.id === 'string' && entry.id.trim().length > 0 ? entry.id : `todo-${index}`,
        content: typeof entry.content === 'string' ? entry.content : '',
        status: normalizeTodoStatus(entry.status)
      }))
      .filter((todo) => todo.content.trim().length > 0)
    : []

  return { title, overview, content, todos }
}

function normalizeTodoStatus(value: unknown): 'pending' | 'in_progress' | 'completed' | 'cancelled' {
  if (value === 'in_progress' || value === 'completed' || value === 'cancelled') {
    return value
  }
  return 'pending'
}

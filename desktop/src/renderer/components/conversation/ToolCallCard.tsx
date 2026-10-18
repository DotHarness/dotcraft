import { Fragment, memo, useEffect, useState, type ReactNode } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import { useLocale } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'

import {
  formatInvocationDisplay,
  formatToolSearchCompletedLabel,
  invocationNeedsCallingPrefix,
  isWebToolKind
} from '../../utils/webToolDisplay'
import { FileResultHeader, InlineDiffView } from './InlineDiffView'
import { ActionTooltip } from '../ui/ActionTooltip'
import { RunningShimmer } from '../ui/RunningShimmer'
import {
  formatCollapsedToolLabel,
  formatExpandedInvocation,
  formatWorkflowFailureLabel,
  getToolRowIcon,
  isToolExecutionFailure
} from '../../utils/toolCallDisplay'
import { getStreamingToolDisplay } from '../../utils/streamingToolDisplay'
import { resolveMemoryOperation, resolveToolKind, type ToolKind, type ToolKindSource } from '../../utils/toolKind'
import { MemoryUpdateCard } from './MemoryUpdateCard'
import { PlanToolOutput } from './PlanToolOutput'
import { CreatePlanCard, hasCreatePlanDisplayData } from './CreatePlanCard'
import { AutomationToolCard, parseAutomationResult } from './AutomationToolCard'
import { AgentBuilderEditCard } from './AgentBuilderEditCard'
import { BUILDER_FIELD_LABEL_KEYS, isBuilderField, type BuilderField } from '../agents/agentBuilderDraftSync'
import { renderSkillToolLabel } from './SkillToolLabel'
import { McpAppView, hasAvailableMcpApp } from './McpAppView'
import { ToolDisclosure } from './ToolDisclosure'
import { useTranscriptItemDiff, useTranscriptWorkspacePath } from './transcriptDiffs'
import { useRemoteToolHostRow } from './RemoteToolHostRow'
import { RemoteToolHostListResult } from './RemoteToolHostListResult'
import { RemoteFileTransferResult } from './RemoteFileTransferResult'
import { parseRemoteToolHostCatalog } from '../../utils/remoteToolHostDisplay'
import { AnsiPre } from './AnsiPre'
import { stripAnsi } from '../../utils/ansi'
import type { FileDiff } from '../../types/toolCall'
import {
  buildSkillManageDiff,
  formatSkillManageLabel,
  formatSkillManageRunningLabel,
  getSkillManageDisplay,
  getSkillManageLabel
} from '../../utils/skillManageToolDisplay'
import {
  formatSkillViewLabel,
  formatSkillViewRunningLabel,
  getSkillViewDisplay
} from '../../utils/skillViewToolDisplay'
import { isToolItemLive, resolveShellBackgroundState, type ShellBackgroundState } from '../../utils/toolCallAggregation'
import { formatDefaultToolResultForDisplay } from '../../utils/toolResultDisplay'
import { normalizeNodeReplTitle } from '../../utils/nodeReplToolDisplay'
import { useSubAgentLookup } from '../../hooks/useSubAgentLookup'
import { SubAgentToolResultCard, getSubAgentToolDisplay, formatSubAgentRunningLabel } from './SubAgentToolResultCard'
import { BoardToolCard } from './BoardToolCard'
import { TaskStopToolRow } from './TaskStopToolRow'
import { getTaskStopDisplay } from '../../utils/taskStopDisplay'
import { ChannelMessageToolRow } from './ChannelMessageToolRow'
import { getChannelMessageDisplay } from '../../utils/channelMessageDisplay'
import type { BoardOperation } from '../../utils/boardToolDisplay'
import {
  formatRequestUserInputResultLines,
  type RequestUserInputResultLine
} from '../../utils/requestUserInputToolDisplay'
import { resolveCoreToolRenderPlan, type ToolRendererFamily } from '../../utils/toolRendererRegistry'
import { useDesktopPluginRegistry } from '../../plugins/desktopPluginRegistry'
import { DesktopPluginToolRendererOutlet } from '../desktopPlugins/DesktopPluginToolRenderer'
import { toAbsoluteWorkspacePath } from '../../utils/workspacePaths'
import { FileDiffStats } from './FileDiffStats'
import { parseWorkflowRunId, WorkflowToolCard } from '../workflow/WorkflowToolCard'
import { WebSearchToolRow } from './WebSearchItemRow'
import { useToolCallRevealTarget } from '../../stores/toolCallRevealStore'

export type ShellRuntimeScope = 'conversation' | 'review' | 'none'

interface ToolCallCardProps {
  item: ConversationItem
  threadId: string
  turnId: string
  turnRunning?: boolean
  shellRuntimeScope?: ShellRuntimeScope
  allowPluginRenderer?: boolean
}

function formatRunningToolLabel(
  rendererFamily: ToolRendererFamily | undefined,
  kind: ToolKind | null,
  args: Record<string, unknown> | undefined,
  locale: AppLocale,
  streamingLabel: string,
  shellCommand?: string
): string {
  if (rendererFamily === 'shell') {
    const firstLine = shellCommand?.split(/\r?\n/, 1)[0]
    return firstLine
      ? translate(locale, 'toolCall.streaming.runningCommand', { command: firstLine })
      : translate(locale, 'toolCall.runningCommand')
  }
  if (rendererFamily === 'skillManage' && args) {
    return formatSkillManageRunningLabel(args, locale)
  }
  if (rendererFamily === 'skillView' && args) {
    return formatSkillViewRunningLabel(args, locale)
  }
  if (rendererFamily === 'nodeRepl' && args) {
    return normalizeNodeReplTitle(args.title) ?? streamingLabel
  }
  if (isWebToolKind(kind) && kind !== 'toolSearch' && args && !invocationNeedsCallingPrefix(kind, args)) {
    return formatInvocationDisplay(kind, args, locale) ?? streamingLabel
  }
  return streamingLabel
}

function formatShellBackgroundLabel(
  state: ShellBackgroundState,
  locale: AppLocale,
  shellCommand?: string
): string {
  const firstLine = shellCommand?.split(/\r?\n/, 1)[0]
  if (state === 'backgroundRunning') {
    return firstLine
      ? translate(locale, 'toolCall.backgroundTerminalStartedWith', { command: firstLine })
      : translate(locale, 'toolCall.backgroundTerminalStarted')
  }
  return firstLine
    ? translate(locale, 'toolCall.backgroundTerminalStoppedWith', { command: firstLine })
    : translate(locale, 'toolCall.backgroundTerminalStopped')
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
  threadId,
  turnId,
  turnRunning = false,
  shellRuntimeScope = 'conversation',
  allowPluginRenderer = true
}: ToolCallCardProps): JSX.Element {
  const locale = useLocale()
  const workspacePath = useTranscriptWorkspacePath()
  const [hovered, setHovered] = useState(false)
  const [expanded, setExpanded] = useState(false)
  const [elapsedMs, setElapsedMs] = useState(0)

  const toolName = item.toolName ?? 'tool'
  const pluginRenderer = useDesktopPluginRegistry((state) => {
    if (!allowPluginRenderer) return null
    const presentationId = item.presentation?.presentationId
    return presentationId
      ? state.toolRenderers.find((entry) => entry.presentationId === presentationId) ?? null
      : null
  })
  const rendererPlan = resolveCoreToolRenderPlan(item)
  const rendererFamily = rendererPlan?.family
  const rendererOperation = rendererPlan?.options.operation
  const args = item.arguments
  const isWebFetchTool = rendererFamily === 'web' && rendererOperation === 'fetch'
  const isSkillManageTool = rendererFamily === 'skillManage'
  const isSkillViewTool = rendererFamily === 'skillView'
  const toolKind = resolveToolKind(item)
  const isWorkflowTool = toolKind === 'workflow'
  const builderField: BuilderField | null = rendererFamily === 'agentBuilder' && isBuilderField(rendererPlan?.options.field as string | undefined)
    ? rendererPlan!.options.field as BuilderField
    : null
  const isTodoTool = rendererFamily === 'todo'
  const isRemoteToolHostTool = rendererFamily === 'remoteToolHost'
  const isShellTool = rendererFamily === 'shell'
  const isStreamingFileTool = rendererFamily === 'fileWrite'
  const streamingDisplay = getStreamingToolDisplay(item, item.argumentsPreview ?? null, locale)
  const shellCommand = isShellTool
    ? resolveShellCommand(item.command, args, streamingDisplay.parsedPreview?.command)
    : undefined
  const shellDisplayArgs = isShellTool && shellCommand
    ? { ...args, command: shellCommand }
    : args
  const isRunning = isToolItemLive(item, { turnRunning })
  const shellBackgroundState = resolveShellBackgroundState(item, { turnRunning })
  const liveInTurn = isRunning && shellBackgroundState == null
  const toolResult = item.result ?? item.errorMessage ?? item.resultPreview
  const conversationShellRuntime = useConversationStore((state) =>
    shellRuntimeScope === 'conversation' && item.toolCallId
      ? state.shellRuntimeByCallId.get(item.toolCallId)
      : undefined
  )
  const liveShellRuntime = conversationShellRuntime
  const shellOutput = liveShellRuntime?.output ?? item.aggregatedOutput ?? toolResult ?? ''
  const skillManageDisplay = isSkillManageTool ? getSkillManageDisplay(args, item.result) : null
  const skillViewDisplay = isSkillViewTool ? getSkillViewDisplay(args, item.result) : null
  const baseSuccess = (rendererPlan?.successOverride === true || item.success !== false)
    && (!isSkillManageTool || skillManageDisplay?.result?.success !== false)
    && (!isSkillViewTool || skillViewDisplay?.loaded !== false)

  useEffect(() => {
    const start = item.createdAt ? new Date(item.createdAt).getTime() : Date.now()
    if (!liveInTurn) {
      setElapsedMs(Math.max(0, Date.now() - start))
      return
    }
    setElapsedMs(Math.max(0, Date.now() - start))
    const interval = setInterval(() => {
      setElapsedMs(Math.max(0, Date.now() - start))
    }, 100)
    return () => clearInterval(interval)
  }, [liveInTurn, item.createdAt])

  const runningElapsedLabel = `${(elapsedMs / 1000).toFixed(1)}s`

  const fileDiff = useTranscriptItemDiff(item.id, isStreamingFileTool)
  const streamingFileDiff = useConversationStore((s) =>
    isStreamingFileTool ? s.streamingItemDiffs.get(item.id) : undefined
  )
  const planTodos = useConversationStore((s) => s.plan?.todos)
  const { lookup: subAgentLookup } = useSubAgentLookup(threadId, rendererFamily === 'subagent')
  const skillManageDiff = isSkillManageTool ? buildSkillManageDiff(args, item.result) : null
  const renderableFileDiff = hasRenderableDiff(fileDiff) ? fileDiff : undefined
  const renderableStreamingFileDiff = hasRenderableDiff(streamingFileDiff) ? streamingFileDiff : undefined
  const remoteToolHostRow = useRemoteToolHostRow({
    item,
    enabled: isRemoteToolHostTool,
    threadId,
    locale,
    running: isRunning,
    success: baseSuccess
  })
  const remoteTransfer = remoteToolHostRow?.transfer
  const success = baseSuccess && remoteToolHostRow?.failed !== true
  const hasRunningExpandableContent = isShellTool
    ? hasVisibleText(shellOutput)
    : isStreamingFileTool
      ? !!renderableStreamingFileDiff
      : !!remoteTransfer
  const hasCompletedExpandableContent = isShellTool
    ? hasVisibleText(shellOutput)
    : isStreamingFileTool
      ? !!renderableFileDiff || hasVisibleText(toolResult)
      : remoteTransfer ? true : hasVisibleText(toolResult)
  // Only a skill edit has something behind the row, and it is the diff.
  const renderableSkillManageDiff = hasRenderableDiff(skillManageDiff ?? undefined)
    ? skillManageDiff ?? undefined
    : undefined
  const canExpandWhileRunning =
    !isWebFetchTool
    && !isSkillManageTool
    && !isSkillViewTool
    && !isTodoTool
    && hasRunningExpandableContent
  const canExpandCompleted =
    !isWebFetchTool
    && !isSkillViewTool
    && !isTodoTool
    && (remoteToolHostRow?.expandable ?? true)
    && (isSkillManageTool ? !!renderableSkillManageDiff : hasCompletedExpandableContent)
  const hasFinalArgs = args != null && Object.keys(args).length > 0
  const subAgentRunningLabel = hasFinalArgs && rendererFamily === 'subagent'
    ? formatSubAgentRunningLabel(rendererOperation, args, locale, subAgentLookup)
    : null
  const runningBaseLabel = builderField
    ? translate(locale, 'agentBuilder.editing.updatingField', { field: translate(locale, BUILDER_FIELD_LABEL_KEYS[builderField]) })
    : subAgentRunningLabel
    ?? formatRunningToolLabel(
      rendererFamily,
      toolKind,
      hasFinalArgs ? shellDisplayArgs : undefined,
      locale,
      streamingDisplay.label,
      shellCommand
    )
  const runningLabel = isStreamingFileTool
    ? formatFileToolLabel(rendererOperation, renderableStreamingFileDiff, runningBaseLabel, locale)
    : runningBaseLabel

  const canExpand = isRunning ? canExpandWhileRunning : canExpandCompleted

  function toggleExpand(): void {
    if (canExpand) setExpanded((v) => !v)
  }

  useEffect(() => {
    if (!canExpand) setExpanded(false)
  }, [canExpand])

  const revealNonce = useToolCallRevealTarget((target) =>
    target.threadId === threadId && target.callId === item.toolCallId ? target.nonce : null)
  useEffect(() => {
    if (revealNonce != null && canExpand) setExpanded(true)
  }, [revealNonce, canExpand])

  if (!isRunning && hasAvailableMcpApp(item)) {
    return <McpAppView item={item} threadId={threadId} turnId={turnId} />
  }

  if (pluginRenderer) {
    return (
      <DesktopPluginToolRendererOutlet
        contribution={pluginRenderer}
        item={item}
        threadId={threadId}
        turnId={turnId}
        running={isRunning}
        fallback={(
          <ToolCallCard
            item={item}
            threadId={threadId}
            turnId={turnId}
            turnRunning={turnRunning}
            shellRuntimeScope={shellRuntimeScope}
            allowPluginRenderer={false}
          />
        )}
      />
    )
  }

  if (rendererFamily === 'web' && rendererOperation === 'search') {
    return <WebSearchToolRow item={item} running={isRunning} success={success} />
  }

  if (resolveMemoryOperation(item) === 'update' && (isRunning || !isToolExecutionFailure(item))) {
    return <MemoryUpdateCard items={[item]} running={isRunning} />
  }

  if (rendererFamily === 'createPlan' && hasCreatePlanDisplayData(item)) {
    return <CreatePlanCard item={item} locale={locale} />
  }

  if (rendererFamily === 'automation' && !isRunning && success && parseAutomationResult(item.result)) {
    return <AutomationToolCard item={item} locale={locale} />
  }

  const workflowRunId = !isRunning ? parseWorkflowRunId(toolName, item.result) : null
  if (workflowRunId && threadId) {
    return <WorkflowToolCard threadId={threadId} runId={workflowRunId} createdAt={item.createdAt} callId={item.toolCallId} />
  }

  const taskStopDisplay = rendererFamily === 'task' ? getTaskStopDisplay(item, isRunning, locale) : null
  if (taskStopDisplay) {
    return <TaskStopToolRow item={item} display={taskStopDisplay} running={isRunning} />
  }

  if (rendererFamily === 'channel') {
    return <ChannelMessageToolRow item={item} display={getChannelMessageDisplay(item, isRunning, locale)} running={isRunning} locale={locale} />
  }

  const subAgentDisplay = !isRunning && rendererFamily === 'subagent'
    ? getSubAgentToolDisplay(rendererOperation, args, item.result, success, locale, subAgentLookup)
    : null
  if (subAgentDisplay) {
    return <SubAgentToolResultCard display={subAgentDisplay} locale={locale} sourceThreadId={threadId} />
  }

  if (rendererFamily === 'board') {
    return <BoardToolCard item={item} operation={rendererOperation as BoardOperation} threadId={threadId} running={isRunning} />
  }

  if (builderField && !isRunning) {
    return <AgentBuilderEditCard item={item} field={builderField} locale={locale} />
  }

  const rowIcon = rendererFamily === 'subagent' ? undefined : getToolRowIcon(item)

  if (isRunning) {
    const runningExpanded = expanded && canExpandWhileRunning
    const runningDisplayLabel = shellBackgroundState
      ? formatShellBackgroundLabel(shellBackgroundState, locale, runningExpanded ? undefined : shellCommand)
      : runningExpanded && isStreamingFileTool
      ? formatExpandedFileToolLabel(rendererOperation, renderableStreamingFileDiff, locale)
      : runningExpanded && isShellTool
        ? translate(locale, 'toolCall.runningCommand')
      : runningLabel
    const runningFilePath = renderableStreamingFileDiff?.filePath
      ?? streamingDisplay.parsedPreview?.path
    const runningResolvedPath = runningFilePath && workspacePath
      ? toAbsoluteWorkspacePath(workspacePath, runningFilePath)
      : runningFilePath ?? undefined
    const runningTitle = shellBackgroundState ? (
      <span className="dc-tool-row-running-label">{runningDisplayLabel}</span>
    ) : runningFilePath && !runningExpanded ? (
      <ActionTooltip
        label={runningResolvedPath ?? runningFilePath}
        wrapperStyle={{ minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
      >
        <RunningShimmer className="dc-tool-row-running-label">{runningDisplayLabel}</RunningShimmer>
      </ActionTooltip>
    ) : (
      <RunningShimmer className="dc-tool-row-running-label">{runningDisplayLabel}</RunningShimmer>
    )
    const runningAccessory = (
      <>
        {!runningExpanded && renderableStreamingFileDiff && (
          <FileToolDiffStats diff={renderableStreamingFileDiff} colorized={hovered} />
        )}
        {liveInTurn && <span>{runningElapsedLabel}</span>}
      </>
    )

    return (
      <ToolDisclosure
        expanded={runningExpanded}
        onToggle={toggleExpand}
        expandable={canExpandWhileRunning}
        onHoverChange={setHovered}
        callId={item.toolCallId}
        icon={rowIcon}
        title={remoteToolHostRow?.title ?? runningTitle}
        accessory={runningAccessory}
      >
          <div
            className="dc-tool-panel-surface"
            data-padded={isStreamingFileTool && renderableStreamingFileDiff ? 'false' : 'true'}
          >
            {remoteTransfer ? (
              <RemoteFileTransferResult display={remoteTransfer} locale={locale} />
            ) : isShellTool ? (
              <ExpandedContent
                itemId={item.id}
                rendererFamily={rendererFamily}
                rendererOptions={rendererPlan?.options}
                tool={item}
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
      </ToolDisclosure>
    )
  }

  const completedToolSearchLabel = success && rendererFamily === 'deferredSearch'
    ? formatToolSearchCompletedLabel(item.result, locale)
    : null
  const fallbackLabel = completedToolSearchLabel
    ?? (isSkillManageTool
      ? formatSkillManageLabel(args, item.result, locale)
      : isSkillViewTool
        ? formatSkillViewLabel(args, locale)
        : shellBackgroundState
          ? formatShellBackgroundLabel(shellBackgroundState, locale, shellCommand)
        : isShellTool && !shellCommand
          ? translate(locale, 'toolCall.ranCommand')
        : formatCollapsedToolLabel(item, shellDisplayArgs, locale, { planTodos }))
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
  const hasInlineFileDiff = isStreamingFileTool && !!renderableFileDiff
  const hasFlushReadFile = rendererFamily === 'readFile'
  const completedExpanded = canExpandCompleted && expanded
  const completedDisplayLabel = completedExpanded && isStreamingFileTool
    ? formatExpandedFileToolLabel(rendererOperation, renderableFileDiff, locale)
    : completedExpanded && shellBackgroundState
      ? formatShellBackgroundLabel(shellBackgroundState, locale)
    : completedExpanded && isShellTool
      ? translate(locale, 'toolCall.ranCommand')
    : completedExpanded && rendererFamily === 'readFile' && resolveMemoryOperation(item) !== 'recall'
      ? translate(locale, 'toolCall.readFile.file')
      : label
  const readFilePath = rendererFamily === 'readFile' && typeof args?.path === 'string'
    ? args.path
    : undefined
  const completedFilePath = renderableFileDiff?.filePath ?? readFilePath
  const completedResolvedPath = completedFilePath && workspacePath
    ? toAbsoluteWorkspacePath(workspacePath, completedFilePath)
    : completedFilePath
  const skillTitle = isSkillViewTool && skillViewDisplay?.name
    ? renderSkillToolLabel(locale, 'skillView.tool.loadedSkill', skillViewDisplay.name)
    : isSkillManageTool
      ? renderSkillManageTitle(locale, args, item.result)
      : null

  const completedTitle = (
    <>
      {skillTitle && success ? (
        skillTitle
      ) : success && completedFilePath && !completedExpanded ? (
        <ActionTooltip
          label={completedResolvedPath ?? completedFilePath}
          wrapperStyle={{ minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
        >
          <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {completedDisplayLabel}
          </span>
        </ActionTooltip>
      ) : (
        <>
          {success
            ? completedDisplayLabel
            : isWorkflowTool
              ? formatWorkflowFailureLabel(args, locale)
              : translate(locale, 'toolCall.failed', { label })}
          {!success && hasFailurePreview && failedPreview && (
            <span style={{ marginLeft: '6px' }}>
              - {failedPreview.slice(0, 80)}{failedPreview.length > 80 ? '…' : ''}
            </span>
          )}
        </>
      )}
    </>
  )
  const completedAccessory = success && !completedExpanded && renderableFileDiff
    ? <FileToolDiffStats diff={renderableFileDiff} colorized={hovered} />
    : undefined

  return (
    <ToolDisclosure
      expanded={completedExpanded}
      onToggle={toggleExpand}
      expandable={canExpandCompleted}
      onHoverChange={setHovered}
      callId={item.toolCallId}
      icon={rowIcon}
      title={remoteToolHostRow?.title ?? completedTitle}
      accessory={completedAccessory}
    >
      <div
        data-testid="tool-expanded-content"
        className="dc-tool-panel-surface"
        data-padded={hasInlineFileDiff || hasFlushReadFile || !!renderableSkillManageDiff ? 'false' : 'true'}
      >
        {remoteTransfer ? (
          <RemoteFileTransferResult display={remoteTransfer} locale={locale} />
        ) : renderableSkillManageDiff ? (
          <InlineDiffView
            diff={renderableSkillManageDiff}
            variant="embedded"
            headerMode="compact"
            locale={locale}
          />
        ) : (
        <ExpandedContent
          itemId={item.id}
          rendererFamily={rendererFamily}
          rendererOptions={rendererPlan?.options}
          tool={item}
          args={shellDisplayArgs}
          result={isShellTool ? shellOutput : toolResult}
          success={success}
          fileDiff={renderableFileDiff ? { diff: renderableFileDiff } : undefined}
          locale={locale}
          workspacePath={workspacePath}
          shellCommand={shellCommand}
          shellInputs={item.terminalInputs}
          planTodos={planTodos}
        />
        )}
      </div>
    </ToolDisclosure>
  )
})

function renderSkillManageTitle(
  locale: AppLocale,
  args: Record<string, unknown> | undefined,
  result: string | undefined
): ReactNode {
  const label = getSkillManageLabel(args, result)
  return label.name
    ? renderSkillToolLabel(locale, label.key, label.name, label.vars)
    : translate(locale, label.key, label.vars)
}

interface ExpandedContentProps {
  itemId: string
  rendererFamily?: ToolRendererFamily
  rendererOptions?: Readonly<Record<string, unknown>>
  tool: ToolKindSource
  args: Record<string, unknown> | undefined
  result: string | undefined
  success: boolean
  fileDiff: { diff: FileDiff } | undefined
  locale: AppLocale
  workspacePath?: string
  shellCommand?: string
  shellInputs?: string[]
  planTodos?: Array<{ id: string; content: string }>
}

function ExpandedContent({
  itemId,
  rendererFamily,
  rendererOptions,
  tool,
  args,
  result,
  success,
  fileDiff,
  locale,
  workspacePath,
  shellCommand,
  shellInputs,
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
      <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--text-code-size)', lineHeight: 1.5 }}>
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

  if (rendererFamily === 'remoteToolHost' && rendererOptions?.operation === 'list') {
    const catalog = parseRemoteToolHostCatalog(result)
    if (catalog) return <RemoteToolHostListResult catalog={catalog} locale={locale} />
  }

  if (rendererFamily === 'requestUserInput') {
    const lines = formatRequestUserInputResultLines(args, result, locale)
    if (lines && lines.length > 0) {
      return <RequestUserInputResultList lines={lines} />
    }
  }

  if (rendererFamily === 'shell') {
    const output = result ?? ''

    return (
      <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--conversation-secondary-size)', lineHeight: 'var(--conversation-line-height)', color: 'var(--text-secondary)' }}>
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
            {[shellCommand, ...(shellInputs ?? [])].map((line, index) => (
              <Fragment key={index}>
                <span aria-hidden style={{ color: 'var(--text-dimmed)' }}>$</span>
                <span style={{ color: 'var(--text-primary)', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                  {line}
                </span>
              </Fragment>
            ))}
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
    ? formatExpandedInvocation(tool, args, locale, { planTodos })
    : null

  return (
    <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--conversation-secondary-size)', lineHeight: 'var(--conversation-line-height)' }}>
      {invocation && (
        <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
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
        fontSize: 'var(--conversation-secondary-size)',
        lineHeight: 'var(--conversation-line-height)',
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

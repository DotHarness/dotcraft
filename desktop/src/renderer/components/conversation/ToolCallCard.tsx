import { memo, useEffect, useRef, useState, type CSSProperties } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem, PluginFunctionContentItem } from '../../types/conversation'
import { useLocale } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import {
  CRON_TOOL_NAME,
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
  isWebToolName,
  parseWebSearchResultDisplay,
  type WebSearchResultRow
} from '../../utils/webToolDisplay'
import { InlineDiffView } from './InlineDiffView'
import { isShellToolName } from '../../utils/shellTools'
import {
  FILE_WRITE_TOOLS,
  formatCollapsedToolLabel,
  formatExpandedInvocation,
  getStreamingToolDisplay
} from '../../utils/toolCallDisplay'
import { PlanToolOutput } from './PlanToolOutput'
import { CreatePlanCard, hasCreatePlanDisplayData } from './CreatePlanCard'
import { CronCreatedCard } from './CronCreatedCard'
import { SkillManageCard } from './SkillManageCard'
import { SkillViewCard } from './SkillViewCard'
import { ToolCollapseChevron } from './ToolCollapseChevron'
import { CollapsibleContent } from './CollapsibleContent'
import { AnsiPre } from './AnsiPre'
import { stripAnsi } from '../../utils/ansi'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { openConversationLink } from '../../utils/conversationDeepLink'
import type { FileDiff } from '../../types/toolCall'
import type { Thread, ThreadSummary } from '../../types/thread'
import {
  SKILL_MANAGE_TOOL_NAME,
  buildSkillManageDiff,
  formatSkillManageLabel,
  formatSkillManageRunningLabel,
  getSkillManageDisplay,
  shouldRenderSkillManageCard
} from '../../utils/skillManageToolDisplay'
import {
  SKILL_VIEW_TOOL_NAME,
  formatSkillViewLabel,
  formatSkillViewRunningLabel,
  getSkillViewDisplay
} from '../../utils/skillViewToolDisplay'
import { useThreadStore } from '../../stores/threadStore'
import { useSubAgentStore, type SubAgentChild } from '../../stores/subAgentStore'
import { isToolItemLive } from '../../utils/toolCallAggregation'
import { formatSubAgentMeta, getSubAgentAccent } from '../../utils/subAgentPresentation'
import {
  formatRequestUserInputResultLines,
  type RequestUserInputResultLine
} from '../../utils/requestUserInputToolDisplay'

interface ToolCallCardProps {
  item: ConversationItem
  turnId: string
  turnRunning?: boolean
}

function formatRunningToolLabel(
  toolName: string,
  args: Record<string, unknown> | undefined,
  locale: AppLocale,
  streamingLabel: string,
  planTodos?: Array<{ id: string; content: string }>
): string {
  if (isShellToolName(toolName) && args) {
    return formatCollapsedToolLabel(toolName, args, locale, { planTodos })
  }
  if (toolName === CRON_TOOL_NAME && args) {
    return formatCronRunningLabel(args, locale)
  }
  if (toolName === SKILL_MANAGE_TOOL_NAME && args) {
    return formatSkillManageRunningLabel(args, locale)
  }
  if (toolName === SKILL_VIEW_TOOL_NAME && args) {
    return formatSkillViewRunningLabel(args, locale)
  }
  if (isWebToolName(toolName) && args && !invocationNeedsCallingPrefix(toolName, args)) {
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

function formatDiffStats(diff: FileDiff | undefined): string {
  if (!diff) return ''
  const parts: string[] = []
  if (diff.additions > 0) parts.push(`+${diff.additions}`)
  if (diff.deletions > 0) parts.push(`-${diff.deletions}`)
  return parts.join(' ')
}

function formatFileToolLabel(
  toolName: string,
  diff: FileDiff | undefined,
  fallbackLabel: string,
  locale: AppLocale
): string {
  if (!diff) return fallbackLabel
  const filename = getFilename(diff.filePath)
  const action = toolName !== 'EditFile' && diff.isNewFile
    ? translate(locale, 'toolCall.created', { filename })
    : translate(locale, 'toolCall.edited', { filename })
  const stats = formatDiffStats(diff)
  return stats ? `${action} ${stats}` : action
}

function hasVisibleText(value: string | undefined): boolean {
  return stripAnsi(value ?? '').trim().length > 0
}

function hasRenderableDiff(diff: FileDiff | undefined): boolean {
  return (diff?.diffHunks.length ?? 0) > 0
}

function hasImageContent(contentItems: PluginFunctionContentItem[] | undefined): boolean {
  return contentItems?.some((item) => item.type === 'image' && !!item.dataBase64?.trim()) ?? false
}

export const ToolCallCard = memo(function ToolCallCard({
  item,
  turnId,
  turnRunning = false
}: ToolCallCardProps): JSX.Element {
  const locale = useLocale()
  const [hovered, setHovered] = useState(false)
  const [expanded, setExpanded] = useState(false)
  const [renderExpanded, setRenderExpanded] = useState(false)
  const [autoExpanded, setAutoExpanded] = useState(false)
  const [userInteracted, setUserInteracted] = useState(false)
  const [elapsedMs, setElapsedMs] = useState(0)
  const autoExpandTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const toolName = item.toolName ?? 'tool'
  const args = item.arguments
  const isWebFetchTool = toolName === 'WebFetch'
  const isSkillManageTool = toolName === SKILL_MANAGE_TOOL_NAME
  const isSkillViewTool = toolName === SKILL_VIEW_TOOL_NAME
  const isTodoTool = toolName === 'TodoWrite' || toolName === 'UpdateTodos'
  const isShellTool = isShellToolName(toolName)
  const isStreamingFileTool = FILE_WRITE_TOOLS.has(toolName)
  const streamingDisplay = getStreamingToolDisplay(
    toolName,
    item.argumentsPreview ?? null,
    locale
  )
  const isRunning = isToolItemLive(item, { turnRunning })
  const shellOutput = item.aggregatedOutput ?? item.result ?? ''
  const skillManageDisplay = isSkillManageTool ? getSkillManageDisplay(args, item.result) : null
  const skillViewDisplay = isSkillViewTool ? getSkillViewDisplay(args, item.result) : null
  const success = (isShellTool || item.success !== false)
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

  const itemDiffs = useConversationStore((s) => s.itemDiffs)
  const streamingItemDiffs = useConversationStore((s) => s.streamingItemDiffs)
  const plan = useConversationStore((s) => s.plan)
  const subAgentChildrenByParent = useSubAgentStore((s) => s.childrenByParent)
  const threadList = useThreadStore((s) => s.threadList)
  const activeThread = useThreadStore((s) => s.activeThread)
  const subAgentLookup: SubAgentLookupSources = {
    childrenByParent: subAgentChildrenByParent,
    threadList,
    activeThread
  }
  const planTodos = plan?.todos
  const fileDiff = FILE_WRITE_TOOLS.has(toolName) ? itemDiffs.get(item.id) : undefined
  const streamingFileDiff = FILE_WRITE_TOOLS.has(toolName) ? streamingItemDiffs.get(item.id) : undefined
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
    : FILE_WRITE_TOOLS.has(toolName)
      ? !!renderableFileDiff || hasVisibleText(item.result)
      : hasVisibleText(item.result) || hasImageContent(item.contentItems)
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
    ? formatSubAgentRunningLabel(toolName, args, locale, subAgentLookup)
    : null
  const runningBaseLabel = subAgentRunningLabel
    ?? formatRunningToolLabel(
      toolName,
      args,
      locale,
      streamingDisplay.label,
      planTodos
    )
  const runningLabel = FILE_WRITE_TOOLS.has(toolName)
    ? formatFileToolLabel(toolName, renderableStreamingFileDiff, runningBaseLabel, locale)
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

  if (toolName === 'CreatePlan' && hasCreatePlanDisplayData(item)) {
    return <CreatePlanCard item={item} locale={locale} />
  }

  if (
    toolName === CRON_TOOL_NAME
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

  const subAgentDisplay = !isRunning
    ? getSubAgentToolDisplay(toolName, args, item.result, success, locale, subAgentLookup)
    : null
  if (subAgentDisplay) {
    return <SubAgentToolResultCard display={subAgentDisplay} locale={locale} />
  }

  if (isRunning) {
    const runningExpanded = expanded && canExpandWhileRunning
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
            <span
              className="tool-running-gradient-text"
              style={{
                minWidth: 0,
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap'
              }}
            >
              {runningLabel}
            </span>
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
                toolName={toolName}
                args={args}
                result={shellOutput}
                success
                fileDiff={undefined}
                contentItems={item.contentItems}
                locale={locale}
                planTodos={planTodos}
              />
            ) : isStreamingFileTool ? (
              renderableStreamingFileDiff ? (
                <InlineDiffView
                  diff={renderableStreamingFileDiff}
                  streaming
                  variant="embedded"
                  showStreamingIndicator={false}
                  headerMode="compact"
                />
              ) : null
            ) : null}
          </div>
        </CollapsibleContent>
      </div>
    )
  }

  const completedToolSearchLabel = success
    ? formatToolSearchCompletedLabel(toolName, item.result, locale)
    : null
  const fallbackLabel = completedToolSearchLabel
    ?? (isSkillManageTool
      ? formatSkillManageLabel(args, item.result, locale)
      : isSkillViewTool
        ? formatSkillViewLabel(args, locale)
        : formatCollapsedToolLabel(toolName, args, locale, { planTodos }))
  const label = FILE_WRITE_TOOLS.has(toolName)
    ? formatFileToolLabel(toolName, fileDiff, fallbackLabel, locale)
    : fallbackLabel
  const failedPreview = stripAnsi(
    isSkillManageTool
      ? (skillManageDisplay?.message ?? '')
      : isSkillViewTool
        ? (skillViewDisplay?.message ?? '')
        : (item.result ?? shellOutput)
  )
  const hasFlushWebSearchTable =
    toolName === 'WebSearch'
    && parseWebSearchResultDisplay(item.result)?.kind === 'results'
  const hasInlineFileDiff = FILE_WRITE_TOOLS.has(toolName) && !!renderableFileDiff
  const completedExpanded = canExpandCompleted && expanded
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
          <span style={{ minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {success ? label : translate(locale, 'toolCall.failed', { label })}
            {!success && (item.result || shellOutput) && (
              <span style={{ color: 'var(--error)', marginLeft: '6px' }}>
                - {failedPreview.slice(0, 80)}{failedPreview.length > 80 ? '…' : ''}
              </span>
            )}
          </span>
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
              padding: hasFlushWebSearchTable || hasInlineFileDiff ? 0 : '8px'
            }}
          >
            <ExpandedContent
              itemId={item.id}
              toolName={toolName}
              args={args}
              result={isShellTool ? shellOutput : item.result}
              success={success}
              fileDiff={renderableFileDiff ? { diff: renderableFileDiff } : undefined}
              contentItems={item.contentItems}
              locale={locale}
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
  toolName: string
  args: Record<string, unknown> | undefined
  result: string | undefined
  success: boolean
  fileDiff: { diff: FileDiff } | undefined
  contentItems?: PluginFunctionContentItem[]
  locale: AppLocale
  planTodos?: Array<{ id: string; content: string }>
}

function ExpandedContent({
  itemId,
  toolName,
  args,
  result,
  success,
  fileDiff,
  contentItems,
  locale,
  planTodos
}: ExpandedContentProps): JSX.Element {
  if (toolName === 'CreatePlan') {
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

  if (FILE_WRITE_TOOLS.has(toolName) && fileDiff) {
    return (
      <InlineDiffView
        diff={fileDiff.diff}
        variant="embedded"
        showStreamingIndicator={false}
        headerMode="compact"
      />
    )
  }

  if (toolName === CRON_TOOL_NAME) {
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

  if (toolName === 'RequestUserInput') {
    const lines = formatRequestUserInputResultLines(args, result, locale)
    if (lines && lines.length > 0) {
      return <RequestUserInputResultList lines={lines} />
    }
  }

  if (isWebToolName(toolName)) {
    if (toolName === 'WebSearch') {
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

  if (isShellToolName(toolName)) {
    const command = (args?.command as string | undefined) ?? toolName
    const output = result ?? ''

    return (
      <div className="selectable" style={{ fontFamily: 'var(--font-mono)', fontSize: '12px', lineHeight: '1.5', color: 'var(--text-secondary)' }}>
        <div style={{ color: 'var(--text-dimmed)', marginBottom: '6px' }}>
          <span style={{ color: 'var(--text-dimmed)' }}>$ </span>
          <span style={{ color: 'var(--text-primary)' }}>{command}</span>
        </div>
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

  const resultText = result ?? ''
  const invocation = formatExpandedInvocation(toolName, args, locale, { planTodos })
  const imageItems = contentItems?.filter((item) => item.type === 'image' && item.dataBase64) ?? []

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
      {imageItems.length > 0 && <PluginFunctionImages items={imageItems} />}
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

function PluginFunctionImages({ items }: { items: PluginFunctionContentItem[] }): JSX.Element {
  return (
    <div
      style={{
        display: 'grid',
        gap: '8px',
        marginTop: '8px'
      }}
    >
      {items.map((item, index) => {
        const mediaType = item.mediaType?.trim() || 'image/png'
        const src = `data:${mediaType};base64,${item.dataBase64}`
        return (
          <img
            key={`${mediaType}-${index}`}
            src={src}
            alt={`Plugin output ${index + 1}`}
            style={{
              display: 'block',
              maxWidth: '100%',
              maxHeight: '320px',
              objectFit: 'contain',
              border: '1px solid var(--border-default)',
              borderRadius: '4px',
              background: 'var(--bg-primary)'
            }}
          />
        )
      })}
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
      <button
        type="button"
        title={title}
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
              <span style={subAgentPromptStyle} title={display.prompt ?? undefined}>
                {translate(locale, 'toolCall.subAgent.prompt', { prompt: display.prompt ?? '' })}
              </span>
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
              <span style={subAgentPromptStyle} title={display.prompt ?? undefined}>
                {translate(locale, 'toolCall.subAgent.prompt', { prompt: display.prompt ?? '' })}
              </span>
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
  toolName: string,
  args: Record<string, unknown> | undefined,
  result: string | undefined,
  success: boolean,
  locale: AppLocale,
  lookup: SubAgentLookupSources
): SubAgentToolDisplay | null {
  if (!isSubAgentToolName(toolName)) return null
  if (toolName === 'WaitAgent' && result === undefined) return null
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
    ?? agentPath
  const childThreadId = explicitChildThreadId
    ?? (toolName === 'WaitAgent' ? resolveImplicitWaitAgentChildThreadId(lookup) : null)
  const status = getString(parsed, 'status')?.toLowerCase()
  const error = getString(parsed, 'error') ?? getString(parsed, 'message')
  const message = toolName === 'WaitAgent'
    ? getString(parsed, 'message') ?? getString(parsed, 'result')
    : null
  const label = resolveSubAgentDisplayName(parsed, args, childThreadId, locale, lookup)
  const prompt = toolName === 'SpawnAgent'
    ? getString(args, 'message') ?? getString(args, 'agentPrompt')
    : null
  const isTimeout = toolName === 'WaitAgent'
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
      : getSubAgentCompletedTitleKey(toolName)
  return {
    titleKey,
    name: label,
    subtitle: '',
    meta: formatSubAgentMeta({ agentRole, profileName: profile, runtimeType }),
    prompt: prompt ? truncateSubAgentPrompt(prompt, 120) : null,
    accentColor: getSubAgentAccent(childThreadId ?? label),
    childThreadId,
    message: isTimeout
      ? (message ?? translate(locale, 'toolCall.subAgent.timeoutMessage'))
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
  toolName: string,
  args: Record<string, unknown> | undefined,
  locale: AppLocale,
  lookup: SubAgentLookupSources
): string | null {
  if (!isSubAgentToolName(toolName)) return null
  const explicitChildThreadId = getString(args, 'childThreadId') ?? getString(args, 'agentId') ?? getString(args, 'target')
  const childThreadId = explicitChildThreadId
    ?? (toolName === 'WaitAgent' ? resolveImplicitWaitAgentChildThreadId(lookup) : null)
  const label = resolveSubAgentDisplayName(undefined, args, childThreadId, locale, lookup)
  const key = toolName === 'SpawnAgent'
    ? 'toolCall.subAgent.starting'
    : toolName === 'WaitAgent'
      ? 'toolCall.subAgent.waiting'
      : getSubAgentRunningTitleKey(toolName)
  return translate(locale, key, { name: label })
}

function getSubAgentCompletedTitleKey(toolName: string): string {
  if (toolName === 'SpawnAgent') return 'toolCall.subAgent.spawned'
  if (toolName === 'WaitAgent') return 'toolCall.subAgent.waited'
  if (toolName === 'SendMessage') return 'toolCall.subAgent.sentMessage'
  if (toolName === 'FollowupTask') return 'toolCall.subAgent.followedUp'
  if (toolName === 'ListAgents') return 'toolCall.subAgent.listed'
  if (toolName === 'SendInput') return 'toolCall.subAgent.sentInput'
  if (toolName === 'ResumeAgent') return 'toolCall.subAgent.resumed'
  return 'toolCall.subAgent.closed'
}

function getSubAgentRunningTitleKey(toolName: string): string {
  if (toolName === 'SendMessage') return 'toolCall.subAgent.sendingMessage'
  if (toolName === 'FollowupTask') return 'toolCall.subAgent.followingUp'
  if (toolName === 'ListAgents') return 'toolCall.subAgent.listing'
  if (toolName === 'SendInput') return 'toolCall.subAgent.sendingInput'
  if (toolName === 'ResumeAgent') return 'toolCall.subAgent.resuming'
  return 'toolCall.subAgent.closing'
}

function resolveSubAgentDisplayName(
  parsed: Record<string, unknown> | undefined,
  args: Record<string, unknown> | undefined,
  childThreadId: string | null | undefined,
  locale: AppLocale,
  lookup: SubAgentLookupSources
): string {
  const explicitDisplayName = getString(parsed, 'displayName') ?? getString(args, 'displayName')
  if (explicitDisplayName && !isThreadIdLike(explicitDisplayName, childThreadId)) return explicitDisplayName

  if (childThreadId) {
    for (const children of lookup.childrenByParent.values()) {
      const child = children.find((entry) => entry.childThreadId === childThreadId)
      if (child?.nickname && !isThreadIdLike(child.nickname, childThreadId)) {
        return child.nickname
      }
    }

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
    ?? getAgentPathSegment(getString(parsed, 'agentPath') ?? getString(args, 'target') ?? childThreadId)
  if (explicitName && !isThreadIdLike(explicitName, childThreadId)) return explicitName

  return translate(locale, 'toolCall.subAgent.agent')
}

function resolveImplicitWaitAgentChildThreadId(lookup: SubAgentLookupSources): string | null {
  const activeParentId = lookup.activeThread?.id
  if (activeParentId) {
    const activeParentChild = getSingleSubAgentChild(lookup.childrenByParent.get(activeParentId) ?? [])
    if (activeParentChild) return activeParentChild.childThreadId
  }

  const allChildren = Array.from(lookup.childrenByParent.values()).flat()
  return getSingleSubAgentChild(allChildren)?.childThreadId ?? null
}

function getSingleSubAgentChild(children: SubAgentChild[]): SubAgentChild | null {
  const candidates = children.filter((child) => child.childThreadId.trim().length > 0)
  return candidates.length === 1 ? candidates[0] : null
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

function isSubAgentToolName(toolName: string): boolean {
  return toolName === 'SpawnAgent'
    || toolName === 'WaitAgent'
    || toolName === 'SendInput'
    || toolName === 'SendMessage'
    || toolName === 'FollowupTask'
    || toolName === 'ResumeAgent'
    || toolName === 'ListAgents'
    || toolName === 'CloseAgent'
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
